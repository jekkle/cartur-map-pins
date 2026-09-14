using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    /// Owns the actual pinning: a pending queue fed by the spawn hook, drained on a throttled
    /// tick once the player is genuinely close, plus a sweep of loaded Locations.
    internal static class PinPlacer
    {
        private struct Pending
        {
            public PinCategory Category;
            public string Subtype;
            public Vector3 Pos;
            public GameObject Go;
        }

        /// Anything at or above this height is inside a dungeon: Location.Awake instantiates
        /// interiors 5000m straight up, and vanilla's own test is `position.y > 3000f`. Interiors
        /// share their surface zone's X/Z, so without this gate a crypt full of ore would dump a
        /// cluster of pins onto the zone centre.
        private const float InteriorHeight = 3000f;

        private static readonly List<Pending> PendingQueue = new List<Pending>();
        private static readonly List<Pending> Requeue = new List<Pending>();
        private static float _timer;
        private static bool _relabelled;

        public static int QueueSize => PendingQueue.Count;

        public static void Enqueue(PinCategory category, Vector3 pos, GameObject go, string subtype = null)
        {
            PendingQueue.Add(new Pending { Category = category, Subtype = subtype, Pos = pos, Go = go });
        }

        public static void Clear() => PendingQueue.Clear();

        public static void Tick(float dt)
        {
            _timer -= dt;
            if (_timer > 0f)
                return;
            _timer = Plugin.ScanInterval.Value;

            Player player = Player.m_localPlayer;
            if (player == null || Minimap.instance == null)
                return;

            // First tick with a live map is the earliest the save's pins are loaded and readable.
            if (!_relabelled)
            {
                _relabelled = true;
                int fixedUp = PinRecord.RelabelSpawners();
                if (fixedUp > 0)
                    Plugin.Log.LogInfo($"Renamed {fixedUp} spawner pin(s) placed by an older version.");

                // Logged even when it changes nothing: "no line in the log" would otherwise be
                // indistinguishable from "the migration never ran", which is exactly the question
                // you need answered when checking whether an upgrade repaired someone's map.
                int repointed = PinRecord.MigrateIcons();
                Plugin.Log.LogInfo($"Icon migration: repointed {repointed} of {PinRecord.Count} recorded pin(s).");

                FlagUnknownPinsOnce();
            }

            Vector3 playerPos = player.transform.position;

            DrainQueue(playerPos);
            SweepLocations(playerPos);
            SweepLootedChests(playerPos);
            SweepMinedOre(playerPos);
            SweepClearedDungeons(playerPos);
            PinRecord.Flush();
#if DIAGNOSTICS
            Patch_ZNetScene_AddInstance.ReportIfDue();
#endif
#if DIAGNOSTICS
            AutoProbe();
#endif
        }

        /// Marks the player's own custom-icon pins once per world, after the sheet change.
        ///
        /// The mod's own pins are repointed by MigrateIcons, but a hand-placed pin records nothing
        /// except its icon slot - and that slot now holds different art with no way to recover what
        /// was meant. So they get the warning glyph, keeping their names, which says "this one
        /// needs you" instead of showing the wrong picture.
        ///
        /// Once per world, stamped in the pin record. "A custom icon we did not place" also
        /// describes every pin the player places by hand from now on, so without the stamp this
        /// would repaint their new pins on every single load. Per world rather than per install,
        /// because one record file serves every world.
        private static void FlagUnknownPinsOnce()
        {
            ZNet net = ZNet.instance;
            if (net == null || !CustomIcons.Ready)
                return;

            long world = net.GetWorldUID();
            if (world == 0L || PinRecord.WorldFlagged(world))
                return;

            Minimap.PinType warning = CustomIcons.Resolve((int)PinIcon.UtilWarning, Minimap.PinType.Icon3);
            int flagged = CustomIcons.IsCustom(warning) ? PinRecord.FlagUnknownCustomPins(warning) : 0;

            // Stamped even when nothing was flagged: the question is "has this world been through
            // the sheet change", and the answer is yes either way. Without that, a world with no
            // hand-placed pins would be re-checked forever.
            PinRecord.MarkWorldFlagged(world);

            if (flagged > 0)
            {
                Plugin.Log.LogInfo($"Marked {flagged} hand-placed pin(s) with the warning icon - " +
                                   "the icon sheet changed and what they were set to cannot be recovered. " +
                                   "Their names are untouched; shift-click one to pick its icon again.");
            }
        }

        private static float _chestSweepAt = -1f;

        /// Repoints a chest pin to the "looted" icon once its container is empty, and back again
        /// if it gets refilled.
        ///
        /// Runs on its own slower timer than the main tick: it has to enumerate live Containers,
        /// which is far too expensive to do a few times a second. PinData.m_type is what persists
        /// in the save (the sprite is re-derived from it on load), so writing the type is enough
        /// to make this stick across relogs.
        private static void SweepLootedChests(Vector3 playerPos)
        {
            Plugin.CategorySettings chestSettings = Plugin.SettingsFor(PinCategory.Chest);
            if (chestSettings == null || !chestSettings.Enabled.Value)
                return;
            if (Plugin.LootedChestIcon.Value == PinIcon.Default)
                return;

            if (_chestSweepAt < 0f)
                _chestSweepAt = Time.realtimeSinceStartup + 2f;
            if (Time.realtimeSinceStartup < _chestSweepAt)
                return;
            _chestSweepAt = Time.realtimeSinceStartup + 2f;

            List<Minimap.PinData> pins = MinimapAccess.GetPins(Minimap.instance);
            if (pins == null)
                return;

            Minimap.PinType normalType = chestSettings.ResolvedPinType;
            Minimap.PinType lootedType = CustomIcons.Resolve((int)Plugin.LootedChestIcon.Value, normalType);
            if (lootedType == normalType)
                return;

            float radius = Plugin.DiscoveryRadius.Value;

            foreach (Container container in ChestRegistry.Alive())
            {
                if (container == null)
                    continue;

                Vector3 pos = container.transform.position;
                if (pos.y >= InteriorHeight || DistanceXZ(pos, playerPos) > radius)
                    continue;

                Inventory inventory = container.GetInventory();
                if (inventory == null)
                    continue;

                bool empty = inventory.NrOfItems() == 0;
                Minimap.PinType wanted = empty ? lootedType : normalType;

                // A chest wearing a ruin's icon is a place marker, not a chest tracker: flipping it
                // to the open-chest glyph the moment you emptied it would throw away the only
                // thing on the map saying a Dvergr tower is there.
                if (PinRecord.SubtypeNear(PinCategory.Chest, pos, 3f) != null)
                    continue;

                // Our record is the authority on whether a pin here is ours, not the icon it
                // currently carries. Matching on icon alone stranded any chest pin sitting on a
                // value neither of the current two - a chest left on a previous version's looted
                // icon became invisible to this sweep and could never be corrected.
                bool recorded = PinRecord.HasCategoryNear(PinCategory.Chest, pos, 3f);

                Minimap.PinData match = null;
                foreach (Minimap.PinData pin in pins)
                {
                    if (!pin.m_save)
                        continue;
                    if (DistanceXZ(pin.m_pos, pos) > 3f)
                        continue;
                    bool ours = pin.m_type == normalType || pin.m_type == lootedType ||
                                (recorded && CustomIcons.IsCustom(pin.m_type));
                    if (!ours)
                        continue;
                    match = pin;
                    break;
                }

                if (match == null || match.m_type == wanted)
                    continue;

                // Edited in place rather than removed and re-added. PinData carries sixteen fields
                // and AddPin takes five, so re-adding quietly reset the rest - and this runs every
                // couple of seconds for every chest you walk past. See PinRecord.Repoint.
                if (!PinRecord.Repoint(Minimap.instance, match, wanted))
                    continue;
                Plugin.Log.LogInfo($"Chest pin at {pos.x:F0},{pos.z:F0} -> {(empty ? "looted" : "restocked")}");
            }
        }

        private static float _dungeonSweepAt = -1f;

        /// Ticks a dungeon's pin off once there is nothing left inside worth taking.
        ///
        /// The game has no idea whether a dungeon is cleared - there is no flag for it, and the
        /// spawners carry m_respawnTimeMinuts, so the monsters come back. What does persist is
        /// what you took: a chest's inventory and whether a mud pile is still standing. So
        /// "cleared" here means looted, which is the question you actually ask of a crypt pin.
        ///
        /// Runs only while you are inside one. Interiors are instantiated 5000m up and keep their
        /// surface zone's X/Z, so being above InteriorHeight in the same zone as an entrance pin
        /// is what "in this dungeon" means - and everything inside is loaded, which is the only
        /// moment the question can be answered honestly.
        ///
        /// Unticks again if a chest refills, for the same reason the looted-chest icon flips back.
        private static void SweepClearedDungeons(Vector3 playerPos)
        {
            if (!Plugin.TickLootedDungeons.Value || playerPos.y < InteriorHeight)
                return;

            Plugin.CategorySettings settings = Plugin.SettingsFor(PinCategory.Dungeon);
            ZoneSystem zones = ZoneSystem.instance;
            if (settings == null || zones == null || Minimap.instance == null)
                return;

            if (_dungeonSweepAt < 0f)
                _dungeonSweepAt = Time.realtimeSinceStartup + 2f;
            if (Time.realtimeSinceStartup < _dungeonSweepAt)
                return;
            _dungeonSweepAt = Time.realtimeSinceStartup + 2f;

            Vector2s zone = ZoneSystem.GetZone(playerPos);
            bool looted = !LootLeftInInterior(zone);

            foreach (Vector3 pos in PinRecord.PositionsOf(PinCategory.Dungeon))
            {
                if (ZoneSystem.GetZone(pos) != zone)
                    continue;

                // The kind comes off the record rather than from the pin: the record is what knows
                // a Sunken Crypt from a Frost Cave, and it is what the rest of the mod trusts.
                if (!Plugin.TickKindEnabled(PinRecord.SubtypeNear(PinCategory.Dungeon, pos, settings.DedupeRadius.Value)))
                    continue;

                if (!PinRecord.SetChecked(PinCategory.Dungeon, pos, settings.DedupeRadius.Value, looted))
                    continue;
                Plugin.Log.LogInfo(looted
                    ? $"Dungeon at {pos.x:F0},{pos.z:F0} ticked off - nothing left inside."
                    : $"Dungeon at {pos.x:F0},{pos.z:F0} unticked - there is loot in it again.");
            }
        }

        /// Whether anything worth taking is still standing in this zone's interior.
        ///
        /// Both registries are filled by the spawn hook as the interior loads, so this walks a
        /// handful of remembered objects rather than every Container in the game. Mud piles are
        /// ore as far as the catalog is concerned - they drop iron scrap - so a Sunken Crypt with
        /// piles left is not cleared even though it holds barely a chest.
        private static bool LootLeftInInterior(Vector2s zone)
        {
            foreach (Container container in ChestRegistry.Alive())
            {
                Vector3 pos = container.transform.position;
                if (pos.y < InteriorHeight || ZoneSystem.GetZone(pos) != zone)
                    continue;
                Inventory inventory = container.GetInventory();
                if (inventory != null && inventory.NrOfItems() > 0)
                    return true;
            }

            foreach (Vector3 pos in OreRegistry.AlivePositions())
            {
                if (pos.y >= InteriorHeight && ZoneSystem.GetZone(pos) == zone)
                    return true;
            }

            return false;
        }

        private static float _oreSweepAt = -1f;

        /// Drops the pin on an ore deposit once the deposit is gone.
        ///
        /// Ore never respawns, so a pin on a mined-out node is worse than no pin at all: it is a
        /// walk across the map to an empty hole. This is the one place the mod removes a pin
        /// without being asked, which is why it is guarded three ways.
        ///
        /// First, only ore pins we recorded - a pin somebody placed by hand is never ours to take.
        /// Second, only where ZoneSystem says the zone is loaded: an unloaded node and a mined one
        /// look identical from here, both being a null reference, and forgetting a pin because the
        /// player walked away would be unforgivable. Third, only when nothing we have seen is
        /// still standing within the dedupe radius.
        private static void SweepMinedOre(Vector3 playerPos)
        {
            Plugin.CategorySettings settings = Plugin.SettingsFor(PinCategory.Ore);
            if (settings == null || !Plugin.ForgetMinedOre.Value)
                return;

            if (_oreSweepAt < 0f)
                _oreSweepAt = Time.realtimeSinceStartup + 3f;
            if (Time.realtimeSinceStartup < _oreSweepAt)
                return;
            _oreSweepAt = Time.realtimeSinceStartup + 3f;

            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || Minimap.instance == null)
                return;

            float radius = Plugin.DiscoveryRadius.Value;
            float near = Mathf.Max(settings.DedupeRadius.Value, 5f);

            foreach (Vector3 pos in PinRecord.PositionsOf(PinCategory.Ore))
            {
                if (DistanceXZ(pos, playerPos) > radius)
                    continue;
                if (!zones.IsZoneLoaded(pos))
                    continue;
                if (OreRegistry.NodeNear(pos, near))
                    continue;

                if (!PinRecord.Forget(PinCategory.Ore, pos, near))
                    continue;
                Plugin.Log.LogInfo($"Ore pin at {pos.x:F0},{pos.z:F0} removed - the deposit is gone.");
            }
        }

        private static bool _autoProbeDone;
        private static float _autoProbeAt = -1f;

        /// Runs the probe once, a few seconds after the player is in-world, straight to the log.
        /// Deliberately not dependent on the game console, which needs a `-console` launch
        /// argument that isn't set up here.
        ///
        /// Uses an absolute realtimeSinceStartup deadline rather than subtracting deltaTime:
        /// this is only called from the throttled tick (~3Hz), so accumulating per-frame deltas
        /// here counted roughly 0.05s per real second and pushed a 12s delay out to ~4 minutes.
 #if DIAGNOSTICS
       private static void AutoProbe()
        {
            if (_autoProbeDone || !Plugin.AutoProbe.Value)
                return;

            if (_autoProbeAt < 0f)
                _autoProbeAt = Time.realtimeSinceStartup + 12f;
            if (Time.realtimeSinceStartup < _autoProbeAt)
                return;

            _autoProbeDone = true;
            Plugin.Log.LogInfo("=== auto-probe (set Diagnostics/AutoProbeOnSpawn=false to disable) ===");

            string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "carturpins_dump.txt");
            Probe.ToFile(path, () =>
            {
                Probe.Nearby(null, 25f);
                Probe.DumpEverything(null);
                Probe.DumpFonts(null);
            });
            Plugin.Log.LogInfo($"Diagnostics written to {path}");
        }
#endif

        private static void DrainQueue(Vector3 playerPos)
        {
            if (PendingQueue.Count == 0)
                return;

            float radius = Plugin.DiscoveryRadius.Value;
            Requeue.Clear();

            int dropped = 0;
            int interior = 0;
            float nearest = float.MaxValue;

            foreach (Pending p in PendingQueue)
            {
                // The object unloaded before we got close enough - drop it.
                if (p.Go == null)
                {
                    dropped++;
                    continue;
                }

                if (p.Pos.y >= InteriorHeight)
                {
                    interior++;
                    continue;
                }

                float dist = DistanceXZ(p.Pos, playerPos);
                if (dist < nearest)
                    nearest = dist;

                if (dist > radius)
                {
                    // Still too far. Keep it so it pins when the player actually walks up,
                    // rather than pinning things that merely loaded behind them.
                    Requeue.Add(p);
                    continue;
                }

                TryPin(p.Category, p.Subtype, p.Pos, ResolveLabel(p.Category, p.Go, p.Subtype));
            }

#if DIAGNOSTICS
            if (dropped > 0 || interior > 0 || nearest < float.MaxValue)
            {
                Plugin.Log.LogInfo($"Drain: queued={PendingQueue.Count} dropped(unloaded)={dropped} " +
                                   $"interior={interior} nearest={(nearest < float.MaxValue ? nearest.ToString("F0") : "-")}m " +
                                   $"radius={radius:F0}m");
            }
#endif

            PendingQueue.Clear();
            PendingQueue.AddRange(Requeue);
        }

        private static void SweepLocations(Vector3 playerPos)
        {
            List<Location> locations = LocationAccess.GetAll();
            if (locations == null)
                return;

            float radius = Plugin.DiscoveryRadius.Value;

            foreach (Location loc in locations)
            {
                if (loc == null)
                    continue;

                Vector3 pos = loc.transform.position;
                if (pos.y >= InteriorHeight)
                    continue;
                if (DistanceXZ(pos, playerPos) > radius)
                    continue;

                if (!TryClassifyLocation(loc, out PinCategory category, out string label, out string subtype))
                    continue;

                TryPin(category, subtype, pos, label);
            }
        }

        /// Classified from the location's own data, with no hardcoded prefab names.
        /// True when the world generator itself puts a permanent marker on this location, so ours
        /// would be a duplicate. m_iconPlaced is deliberately not included: vanilla's marker for a
        /// trader or Hildir's camp is unnamed and unsaved, which is exactly what this mod replaces.
        ///
        /// ZoneSystem.GetLocation is private, so this reads the public m_locations list the dump
        /// already uses, once - the sweep asks this of every location it classifies, and a linear
        /// scan of 232 definitions per call is the kind of thing that shows up as a frame-rate bug.
        /// Not cached until the list is there, so an early call can't freeze in an empty answer.
        private static HashSet<string> _alwaysMarked;

        private static bool MarkedByVanilla(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return false;

            if (_alwaysMarked == null)
            {
                List<ZoneSystem.ZoneLocation> locations = ZoneSystem.instance?.m_locations;
                if (locations == null)
                    return false;

                _alwaysMarked = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (ZoneSystem.ZoneLocation zl in locations)
                {
                    if (zl != null && zl.m_iconAlways && !string.IsNullOrEmpty(zl.m_prefabName))
                        _alwaysMarked.Add(zl.m_prefabName);
                }
                Plugin.Log.LogInfo($"{_alwaysMarked.Count} location(s) already marked by vanilla - left alone.");
            }

            return _alwaysMarked.Contains(prefabName);
        }

        /// internal so the location dump can ask the real classifier what it would do with each of
        /// the 232 definitions rather than reimplementing the same ladder beside it, which would
        /// drift the moment either side changed. Everything it reads - the child components,
        /// m_hasInterior, m_generator, the prefab name - is on the prefab as well as the instance.
        internal static bool TryClassifyLocation(Location loc, out PinCategory category, out string label, out string subtype)
        {
            subtype = null;

            // Locations the game marks on the map at world generation, visited or not. There is
            // exactly one - StartTemple, the spawn temple - and anything of ours there is a second
            // icon on the same spot. Asked of ZoneSystem rather than tested by name, because the
            // flag is the reason and the name is only today's example of it.
            //
            // Its guardian stones carry a vegvisir that reaches the runestone branch below. Two
            // attempts to disqualify that stone by looking for a BossStone on it and then above it
            // both missed, and the hierarchy is not something this needs to know: the location is
            // already marked, so nothing about what stands inside it matters.
            if (MarkedByVanilla(Utils.GetPrefabName(loc.gameObject)))
            {
                category = default;
                label = null;
                return false;
            }

            OfferingBowl bowl = loc.GetComponentInChildren<OfferingBowl>();
            if (bowl != null)
            {
                category = PinCategory.BossAltar;
                label = BossLabel(bowl);
                // Which boss decides the icon. m_bossPrefab's name is the boss's own prefab
                // ("Eikthyr", "gd_king"), which is a far steadier signal than the altar's.
                string bossPrefab = bowl.m_bossPrefab != null ? bowl.m_bossPrefab.name : bowl.m_name;
                subtype = Subtypes.Match(Subtypes.Bosses, bossPrefab);
                WarnUnmatched("boss", bossPrefab, subtype);
                return true;
            }

            // m_hasInterior is what actually makes something a dungeon, and m_generator alone
            // (without an interior) means a surface camp - Fuling villages and Greydwarf camps
            // use the same DungeonGenerator with a CampGrid/CampRadial algorithm. Splitting on
            // that keeps "went in a crypt" and "found a Fuling village" as separate toggles.
            string prefabName = Utils.GetPrefabName(loc.gameObject);
            // The generator's name (DG_ForestCrypt, DG_SunkenCrypt, DG_GoblinCamp...) is the
            // reliable discriminator - those names are confirmed from the game's asset manifest,
            // unlike the location prefab names.
            string generatorName = loc.m_generator != null ? loc.m_generator.name : null;

            if (loc.m_hasInterior)
            {
                category = PinCategory.Dungeon;
                // The subtype drives both the icon and the label, so a Frost Cave and a Burial
                // Chamber don't share a marker. Unmatched names fall back to a prettified prefab
                // name ("Crypt2" -> "Crypt") and get logged so the table can be extended.
                subtype = Subtypes.Match(Subtypes.Dungeons, prefabName, generatorName);
                label = subtype ?? Labels.ForLocation(prefabName);
                WarnUnmatched("dungeon", $"{prefabName} (generator {generatorName ?? "none"})", subtype);
                return true;
            }

            // A camp used to mean "has a generator, has no interior", and in this build exactly one
            // location in 232 fits that: Hildir's fortress. A Fuling village (GoblinCamp2, quantity
            // 200) and a Greydwarf camp (Greydwarf_camp1, 300) are plain surface locations with no
            // generator at all, so the test that named the category never matched the things the
            // category was written for. A Fuling village carries nothing else the mod pins either -
            // no spawner, no container - so it was invisible on the map.
            //
            // The name table answers first now. The generator stays as the fallback for the ones
            // that do still have one, and for camps added by other mods.
            subtype = Subtypes.Match(Subtypes.Camps, prefabName, generatorName);
            if (subtype != null || loc.m_generator != null)
            {
                category = PinCategory.Camp;
                label = subtype ?? Labels.ForLocation(prefabName);
                WarnUnmatched("camp", $"{prefabName} (generator {generatorName ?? "none"})", subtype);
                return true;
            }

            // A vegvisir sitting inside a location, checked here rather than above the two branches
            // it used to outrank. A vegvisir is a prop: it turns up inside Charred Ruins, stone
            // henges, swamp ruins and Morgen Holes, and asking about it first made a Morgen Hole -
            // a dungeon with an interior - pin as a runestone. A location's own shape is the
            // stronger claim.
            //
            // It still outranks the landmark table below, deliberately: Landmark is off by
            // default, so deferring to it would mean a stone henge with a vegvisir in it pins
            // nothing at all on a fresh install. The stone is the part worth walking back to.
            //
            // Nothing reaches PinCategory.Runestone any other way. The 7 prefabs that used to fill
            // its catalog were all guardian stones, which is why this is now the whole category
            // rather than a second route into it.
            //
            // Guardian stones carry a Vegvisir of their own, so the temple at world spawn arrived
            // here and pinned as a runestone even after they were dropped from the catalog. Same
            // rule, applied on this path too: a stone with a BossStone beside it is a temple stone,
            // and vanilla already marks that temple.
            // GetComponentInParent, not GetComponent: the vegvisir sits on a child of the guardian
            // stone rather than on the stone itself, so a same-object test found nothing and the
            // temple still pinned - as "$enemy_eikthyr", the first of the seven stones.
            Vegvisir vegvisir = loc.GetComponentInChildren<Vegvisir>();
            if (vegvisir != null && vegvisir.GetComponentInParent<BossStone>() == null)
            {
                category = PinCategory.Runestone;
                label = VegvisirLabel(vegvisir);
                return true;
            }

            // Lore runestones are the one location worth pinning that has neither an interior nor
            // a generator, so they fell through this method and were never pinned at all. There
            // are eleven, confirmed from the world generator's own 232 ZoneLocation definitions:
            // Runestone_Boars, _Meadows, _Draugr, _Greydwarfs, _Swamps, _Mountains, _BlackForest,
            // _Plains, _Mistlands, _Ashlands, _DeepNorth.
            //
            // Separate category from Runestone above so the two toggle apart.
            if (prefabName != null &&
                prefabName.StartsWith("Runestone_", System.StringComparison.OrdinalIgnoreCase))
            {
                category = PinCategory.LoreStone;
                label = Labels.ForLoreStone(prefabName);
                return true;
            }

            // Surface landmarks - wells, shipwrecks, dolmens, abandoned houses. These have no
            // interior, no generator and no runestone, so everything above passes them over; they
            // are matched by name against a table built from a live dump of all 232 ZoneLocations.
            subtype = Subtypes.Match(Subtypes.Landmarks, prefabName);
            if (subtype != null)
            {
                category = PinCategory.Landmark;
                label = subtype;
                return true;
            }

            // Everything that reaches here is a location the mod will never pin: no interior, no
            // generator, not a runestone, and no landmark name matched. Logged once per prefab so
            // the blind spot is a list you can read rather than a guess.
            //
            // Reported from here rather than predicted from ZoneSystem.m_locations because a
            // ZoneLocation only holds a SoftReference to its prefab - deciding this up front would
            // mean loading all 232 location prefabs to look at two fields.
            if (_warnedSkipped.Add(prefabName ?? "(unnamed)"))
                Plugin.Log.LogInfo($"Location '{prefabName}' is not pinned: no interior, no generator, not a runestone.");

            category = default;
            label = null;
            return false;
        }

        private static readonly HashSet<string> _warnedSkipped = new HashSet<string>();

        private static readonly HashSet<string> _warnedUnmatched = new HashSet<string>();

        /// Logged once per prefab so an unknown dungeon/camp name can be added to the subtype
        /// table rather than quietly showing the category's generic icon forever.
        private static void WarnUnmatched(string kind, string prefabName, string subtype)
        {
            if (subtype != null || string.IsNullOrEmpty(prefabName))
                return;
            if (_warnedUnmatched.Add(prefabName))
                Plugin.Log.LogInfo($"No {kind} subtype matched '{prefabName}' - using the category icon. Add a fragment to Subtypes if it deserves its own.");
        }

        private static string BossLabel(OfferingBowl bowl)
        {
            if (bowl.m_bossPrefab != null)
            {
                Humanoid boss = bowl.m_bossPrefab.GetComponent<Humanoid>();
                if (boss != null && !string.IsNullOrEmpty(boss.m_name))
                    return boss.m_name;   // an $enemy_* token; localized at render time
            }
            return bowl.m_name;
        }

        private static string VegvisirLabel(Vegvisir vegvisir)
        {
            if (vegvisir.m_locations != null && vegvisir.m_locations.Count > 0)
            {
                string pinName = vegvisir.m_locations[0].m_pinName;
                if (!string.IsNullOrEmpty(pinName))
                    return pinName;
            }
            return vegvisir.m_name;
        }

        /// Labels come back as the raw "$token" strings the components hold; TryPin resolves them
        /// through Labels.Localize on the way to the map. This used to say Minimap.PinNameData
        /// localised pin names for us - it does not, and nothing else did either.
#if DIAGNOSTICS
        /// Diagnostics hook: resolve a label without pinning anything, so every catalogued prefab
        /// can be previewed from one console command instead of by walking to each of them.
        internal static string PreviewLabel(PinCategory category, GameObject go, string subtype) =>
            ResolveLabel(category, go, subtype);
#endif

        private static string ResolveLabel(PinCategory category, GameObject go, string subtype)
        {
            switch (category)
            {
                case PinCategory.Ore:
                    // The ore type resolved at catalog time ("Copper") is already the label we
                    // want; fall back to deriving it from the dropped item. Either way it isn't
                    // the component's m_name, because the deposits that matter are plain
                    // Destructibles with no m_name - which is why these once read "rock4_copper".
                    return !string.IsNullOrEmpty(subtype) ? subtype : Labels.ForOre(Utils.GetPrefabName(go));

                case PinCategory.Leviathan:
                    return "Leviathan";

                case PinCategory.Prop:
                    return !string.IsNullOrEmpty(subtype) ? subtype : Labels.ForLocation(Utils.GetPrefabName(go));

                case PinCategory.Trader:
                    Trader trader = go.GetComponent<Trader>();
                    if (trader != null && !string.IsNullOrEmpty(trader.m_name))
                        return trader.m_name;
                    // BogWitch leaves Trader.m_name empty, so fall back to the NPC's own name.
                    Character npc = go.GetComponent<Character>();
                    if (npc != null && !string.IsNullOrEmpty(npc.m_name))
                        return npc.m_name;
                    break;

                case PinCategory.Chest:
                    // The ruin's name when the chest is standing in one, so the pin reads
                    // "Dvergr Tower" rather than "chest" - see Subtypes.ChestSites.
                    if (!string.IsNullOrEmpty(subtype))
                        return subtype;
                    Container container = go.GetComponent<Container>();
                    if (container != null && !string.IsNullOrEmpty(container.m_name))
                        return container.m_name;
                    break;

                case PinCategory.Wisp:
                    // piece_EternalPyre, piece_FaderEmbers, piece_wisplure - buildables, so they
                    // carry a Piece.m_name token. Grouped with Spawner before, which leaked the
                    // "piece_" prefix and called a wisplure a spawner.
                    Piece wisp = go.GetComponent<Piece>();
                    if (wisp != null && !string.IsNullOrEmpty(wisp.m_name))
                        return wisp.m_name;
                    break;

                case PinCategory.Miniboss:
                    // The creature's own name - "Lord Reto", not "Lord Reto Spawner". You are
                    // walking to him, and there is only ever one of him.
                    if (!string.IsNullOrEmpty(subtype))
                        return subtype;
                    goto case PinCategory.Spawner;

                case PinCategory.Spawner:
                    // Ask the spawner what it spawns before falling back to reading its name.
                    string spawns = SpawnedCreatureName(go);
                    if (!string.IsNullOrEmpty(spawns))
                        return spawns;
                    return Labels.ForSpawner(Utils.GetPrefabName(go));

                case PinCategory.Runestone:
                    Vegvisir vegvisir = go.GetComponent<Vegvisir>();
                    if (vegvisir != null)
                    {
                        string vegLabel = VegvisirLabel(vegvisir);
                        if (!string.IsNullOrEmpty(vegLabel))
                            return vegLabel;
                    }
                    // The category is Vegvisir OR RuneStone, so the second component gets asked
                    // too. m_name before m_pinName, unlike Vegvisir above: a RuneStone that never
                    // had its pin name filled in ships the Unity default "Pin".
                    RuneStone runeStone = go.GetComponent<RuneStone>();
                    if (runeStone != null)
                    {
                        if (!string.IsNullOrEmpty(runeStone.m_name))
                            return runeStone.m_name;
                        if (!string.IsNullOrEmpty(runeStone.m_pinName) && runeStone.m_pinName != "Pin")
                            return runeStone.m_pinName;
                    }
                    // Nothing usable on the component: fall through to the prettified prefab name.
                    break;

                case PinCategory.BossAltar:
                    OfferingBowl altar = go.GetComponent<OfferingBowl>();
                    if (altar != null)
                    {
                        string bossLabel = BossLabel(altar);
                        if (!string.IsNullOrEmpty(bossLabel))
                            return bossLabel;
                    }
                    break;

                case PinCategory.Beehive:
                    Beehive hive = go.GetComponent<Beehive>();
                    if (hive != null && !string.IsNullOrEmpty(hive.m_name))
                        return hive.m_name;
                    break;

                case PinCategory.Pickable:
                    Pickable pickable = go.GetComponent<Pickable>();
                    if (pickable != null)
                    {
                        string hover = pickable.GetHoverName();
                        if (Usable(hover))
                            return hover;
                    }
                    PickableItem item = go.GetComponent<PickableItem>();
                    if (item != null)
                    {
                        string hover = item.GetHoverName();
                        if (Usable(hover))
                            return hover;
                    }
                    // Random-loot pickables roll their contents at spawn, so there is no item to
                    // name and the hover text is the literal string "None" - the same kind of
                    // placeholder as the boss stones' m_pinName of "Pin". Fall back to the prefab,
                    // minus the "Pickable_" prefix, which would otherwise read "Pickable Item".
                    return Labels.Prettify(StripPrefix(Utils.GetPrefabName(go), "Pickable_"));
            }

            // Nothing claimed it. Prettify rather than returning the raw prefab name, so the
            // worst case is "Bog Witch" instead of "BogWitch" - this is the net that catches any
            // category whose component turns out not to carry a name.
            return Labels.Prettify(Utils.GetPrefabName(go));
        }

        /// The creature a spawner spawns, read off the spawner's own component instead of guessed
        /// from its prefab name. This is the same principle the catalog uses to decide what counts
        /// as ore - ask the object, don't pattern-match its name - and it answers every case the
        /// name cannot: Spawner_Hole, Spawner_Location_Elite and EvilHeart_Forest all name a place
        /// or a tuning variant rather than what comes out of them.
        ///
        /// The creature's own m_name is a localisation token, so the label comes out in the
        /// player's language for free, the way Pickable.GetHoverName already does.
        ///
        /// Null when anything is missing, so the caller falls back to reading the prefab name.
        /// "None" is what the game hands back when a component has nothing to name - not a name.
        private static bool Usable(string name) =>
            !string.IsNullOrEmpty(name) && name != "None";

        private static string StripPrefix(string name, string prefix) =>
            name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                ? name.Substring(prefix.Length)
                : name;

        private static GameObject SpawnedCreaturePrefab(GameObject go)
        {
            CreatureSpawner spawner = go.GetComponent<CreatureSpawner>();
            if (spawner != null && spawner.m_creaturePrefab != null)
                return spawner.m_creaturePrefab;

            // A SpawnArea can list several creatures; the first is representative enough for
            // a map label, and they are usually variants of one thing anyway.
            SpawnArea area = go.GetComponent<SpawnArea>();
            if (area != null && area.m_prefabs != null && area.m_prefabs.Count > 0)
                return area.m_prefabs[0].m_prefab;

            return null;
        }

        /// The spawned creature's prefab name ("Greydwarf", "Goblin"), which is what the spawner
        /// icon table matches on. Unlike the label, this must stay untranslated.
        ///
        /// internal so the probe can dump it: this is the one value the Subtypes.Spawners table
        /// is written against, and it cannot be read offline.
        internal static string SpawnedCreaturePrefabName(GameObject go) =>
            SpawnedCreaturePrefab(go)?.name;

        /// Every creature a spawner lists, for diagnostics. The matcher only uses the first, so
        /// dumping all of them is what shows whether that is the right choice.
        internal static string AllSpawnedCreatureNames(GameObject go)
        {
            CreatureSpawner spawner = go.GetComponent<CreatureSpawner>();
            if (spawner != null && spawner.m_creaturePrefab != null)
                return spawner.m_creaturePrefab.name;

            SpawnArea area = go.GetComponent<SpawnArea>();
            if (area == null || area.m_prefabs == null || area.m_prefabs.Count == 0)
                return null;

            var sb = new System.Text.StringBuilder();
            foreach (SpawnArea.SpawnData sd in area.m_prefabs)
            {
                if (sd?.m_prefab == null)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(sd.m_prefab.name);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string SpawnedCreatureName(GameObject go)
        {
            GameObject creature = SpawnedCreaturePrefab(go);
            if (creature == null)
                return null;

            Character character = creature.GetComponent<Character>();
            if (character == null || string.IsNullOrEmpty(character.m_name))
                return null;

            string name = Localization.instance != null
                ? Localization.instance.Localize(character.m_name)
                : character.m_name;

            name = Labels.StripRichText(name);
            return string.IsNullOrEmpty(name) ? null : name + " Spawner";
        }

        /// The subtype for something arriving through the spawn hook. It drives three things at
        /// once: the icon, the dedupe key, and (for ore) the per-type toggle - so a copper pin
        /// can't suppress a tin node metres away.
        ///
        /// Lives here rather than in the hook so that all subtype matching, and the logging of
        /// what failed to match, stays in one place.
        internal static string SubtypeFor(PinCategory category, int hash, GameObject go)
        {
            switch (category)
            {
                case PinCategory.Ore:
                    return PinCatalog.OreTypeOf(hash);

                case PinCategory.Pickable:
                    // The specific plant where the sheet has art for it, otherwise the group it
                    // belongs to. Both are valid subtypes - IconFor tells them apart by whether
                    // the string parses as a PickableGroup.
                    return Subtypes.Match(Subtypes.Pickables, Utils.GetPrefabName(go))
                           ?? PinCatalog.GroupOf(hash).ToString();

                case PinCategory.Trader:
                    return Subtypes.Match(Subtypes.Traders, Utils.GetPrefabName(go));

                case PinCategory.Prop:
                    return Subtypes.Match(Subtypes.Props, Utils.GetPrefabName(go));

                case PinCategory.Chest:
                    // A chest inside a ruin we recognise takes the ruin's name and icon. The ruin
                    // itself is never pinned - there are hundreds of them - so this is the only
                    // marker it gets, and a marker that says "Dvergr Tower" beats one that says
                    // "chest" on a map with forty chests on it.
                    return Subtypes.Match(Subtypes.ChestSites, EnclosingLocationName(go));

                case PinCategory.Miniboss:
                    return Subtypes.Match(Subtypes.Minibosses, SpawnedCreaturePrefabName(go));

                case PinCategory.Spawner:
                    string creature = SpawnedCreaturePrefabName(go);
                    string matched = Subtypes.Match(Subtypes.Spawners, creature);
                    WarnUnmatched("spawner", creature, matched);
                    return matched;

                default:
                    return null;
            }
        }

        /// The location an object is standing inside, or null when it is out in the open.
        ///
        /// m_exteriorRadius is the location's own footprint - the same value the game uses to keep
        /// buildings and other locations out - so "inside" is the game's definition rather than a
        /// radius picked here. Locations are only in this list while loaded, which is the same
        /// moment the object itself is loaded, so a chest always gets asked next to its own ruin.
        private static string EnclosingLocationName(GameObject go)
        {
            List<Location> locations = LocationAccess.GetAll();
            if (locations == null || go == null)
                return null;

            Vector3 pos = go.transform.position;
            foreach (Location loc in locations)
            {
                if (loc == null)
                    continue;
                float radius = loc.m_exteriorRadius;
                if (radius <= 0f)
                    continue;
                if (DistanceXZ(loc.transform.position, pos) <= radius)
                    return Utils.GetPrefabName(loc.gameObject);
            }
            return null;
        }

        /// The icon a pin of this kind should currently carry.
        ///
        /// Shared by the pinning path and the one-shot icon migration, deliberately: if the two
        /// computed it separately they could disagree, and the migration would "fix" pins to
        /// something the mod would never place.
        ///
        /// Pickables resolve by group, everything else by the subtype name. SubtypeIconFor falls
        /// back to the category's own setting when the subtype is null or has no icon bound, so
        /// listing a category here is safe even when a particular instance didn't match.
        internal static Minimap.PinType IconFor(PinCategory category, string subtype, Plugin.CategorySettings settings)
        {
            if (category == PinCategory.Pickable)
            {
                // A subtype that names a group means no per-plant art matched; anything else is
                // a specific plant with its own icon.
                return System.Enum.TryParse(subtype ?? string.Empty, out PickableGroup group)
                    ? Plugin.PickableIconFor(group, settings.PinType.Value)
                    : Plugin.SubtypeIconFor(subtype, settings);
            }

            // Everything else asks for the kind's icon and gets the category's when the kind has
            // none - SubtypeIconFor already falls back, so naming categories here bought nothing
            // and cost the ones left out: chest sites and props were bound icons that could never
            // be reached, because Chest and Prop were not on the list.
            return Plugin.SubtypeIconFor(subtype, settings);
        }

        /// Marks a bed as home. Called when the local player claims one as their spawn.
        ///
        /// Goes straight to TryPin rather than through the queue: the queue exists to hold objects
        /// until the player is close enough to have discovered them, and you are standing on this
        /// one. Dedupe handles sleeping in the same bed twice.
        internal static void PinHome(Vector3 pos, string label)
        {
            if (Minimap.instance == null)
                return;
            TryPin(PinCategory.Home, null, pos, label);
        }

        private static void TryPin(PinCategory category, string subtype, Vector3 pos, string label)
        {
            Plugin.CategorySettings settings = Plugin.SettingsFor(category);
            if (settings == null)
                return;

            // Dedupe on category+subtype: a category-wide radius would let a copper pin suppress
            // a tin node metres away, hiding a resource entirely.
            string key = string.IsNullOrEmpty(subtype) ? category.ToString() : $"{category}:{subtype}";

            Minimap.PinType pinType = IconFor(category, subtype, settings);

            if (PinRecord.Exists(key, pos, settings.DedupeRadius.Value))
            {
                // Already pinned - but a record written before subtypes existed says only
                // "Spawner" where we can now see it is a boar. Standing next to the thing is the
                // only moment that information exists, so take it now rather than leave the pin
                // on the generic category icon forever.
                //
                // Runs before the switches below on purpose. Turning a category off means "place
                // no more of these", not "freeze the pins I already have on the generic glyph" -
                // and with Spawner off by default, gating this behind Enabled left every pin from
                // an older version stuck on the summoning circle with no way to ever repair it.
                PinRecord.Upgrade(category, subtype, pos, settings.DedupeRadius.Value, pinType);
                return;
            }

            if (!settings.Enabled.Value)
                return;

            // Every category with kinds honours its per-kind switches too: copper but not tin,
            // Fuling villages but not Greydwarf camps, a well but not the thirteen abandoned
            // houses beside it. One gate rather than one per category, so a table added later is
            // covered without anybody remembering to come back here.
            if (!Plugin.SubtypeEnabled(category, subtype))
                return;

            // AddPin rather than DiscoverLocation: the latter always fires a MessageHud toast,
            // which would spam the corner of the screen during bulk discovery.
            Minimap.instance.AddPin(pos, pinType, Labels.Localize(label) ?? string.Empty,
                save: true, isChecked: false);

            PinRecord.Add(key, pos);
            Plugin.Log.LogInfo($"Pinned {key} '{label}' at {pos.x:F0},{pos.z:F0}");
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
