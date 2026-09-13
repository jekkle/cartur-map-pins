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
            }

            Vector3 playerPos = player.transform.position;

            DrainQueue(playerPos);
            SweepLocations(playerPos);
            SweepLootedChests(playerPos);
            PinRecord.Flush();
#if DIAGNOSTICS
            Patch_ZNetScene_AddInstance.ReportIfDue();
#endif
#if DIAGNOSTICS
            AutoProbe();
#endif
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

                Minimap.PinData match = null;
                foreach (Minimap.PinData pin in pins)
                {
                    if (!pin.m_save)
                        continue;
                    if (pin.m_type != normalType && pin.m_type != lootedType)
                        continue;   // not one of ours
                    if (DistanceXZ(pin.m_pos, pos) > 3f)
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
            Probe.Nearby(null, 25f);
            Probe.DumpCategory(null, "Ore");
            Probe.DumpSpawners(null);
            // Pickables are bucketed into six groups, so the per-plant names the icon sheet is
            // drawn against only exist on the prefabs themselves - same problem as the spawners.
            Probe.DumpLabels(null, "Pickable");
            // Three prefabs, and the trader icon table is matched against their names - cheap to
            // confirm rather than leave as the one guess nothing else would catch.
            Probe.DumpLabels(null, "Trader");
            Probe.DumpLocations(null);
            Probe.DumpFonts(null);
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
        private static bool TryClassifyLocation(Location loc, out PinCategory category, out string label, out string subtype)
        {
            subtype = null;

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

            Vegvisir vegvisir = loc.GetComponentInChildren<Vegvisir>();
            if (vegvisir != null)
            {
                category = PinCategory.Runestone;
                label = VegvisirLabel(vegvisir);
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

            if (loc.m_generator != null)
            {
                category = PinCategory.Camp;
                subtype = Subtypes.Match(Subtypes.Camps, prefabName, generatorName);
                label = subtype ?? Labels.ForLocation(prefabName);
                WarnUnmatched("camp", $"{prefabName} (generator {generatorName ?? "none"})", subtype);
                return true;
            }

            // Lore runestones are the one location worth pinning that has neither an interior nor
            // a generator, so they fell through this method and were never pinned at all. There
            // are eleven, confirmed from the world generator's own 232 ZoneLocation definitions:
            // Runestone_Boars, _Meadows, _Draugr, _Greydwarfs, _Swamps, _Mountains, _BlackForest,
            // _Plains, _Mistlands, _Ashlands, _DeepNorth.
            //
            // Nothing to do with PinCategory.Runestone, which is the seven BossStone_ prefabs and
            // arrives through the spawn hook instead. Separate category so the two toggle apart.
            if (prefabName != null &&
                prefabName.StartsWith("Runestone_", System.StringComparison.OrdinalIgnoreCase))
            {
                category = PinCategory.LoreStone;
                label = Labels.ForLoreStone(prefabName);
                return true;
            }

            // Everything that reaches here is a location the mod will never pin: no interior, no
            // generator, not a runestone. Logged once per prefab so the blind spot is a list you
            // can read rather than a guess.
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

        /// Labels are stored as raw "$token" strings wherever possible: Minimap.PinNameData runs
        /// pin names through Localization.Localize when rendering them, so this is both less code
        /// and correct in every language.
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

                case PinCategory.Spawner:
                    // Ask the spawner what it spawns before falling back to reading its name.
                    string spawns = SpawnedCreatureName(go);
                    if (!string.IsNullOrEmpty(spawns))
                        return spawns;
                    return Labels.ForSpawner(Utils.GetPrefabName(go));

                case PinCategory.Runestone:
                    // BossStone_* reach here through the spawn hook, not the location sweep, so
                    // VegvisirLabel was never consulted and the map read "BossStone_Eikthyr".
                    Vegvisir vegvisir = go.GetComponent<Vegvisir>();
                    if (vegvisir != null)
                    {
                        string vegLabel = VegvisirLabel(vegvisir);
                        if (!string.IsNullOrEmpty(vegLabel))
                            return vegLabel;
                    }
                    // The category is Vegvisir OR RuneStone, and the boss stones are the second
                    // kind - a different component with its own pin name, which is why handling
                    // only Vegvisir left them reading "BossStone_Eikthyr".
                    // m_name before m_pinName, unlike Vegvisir above. Every BossStone ships
                    // m_pinName = "Pin", a Unity default nobody filled in, and preferring it
                    // labelled all seven boss stones "Pin".
                    RuneStone runeStone = go.GetComponent<RuneStone>();
                    if (runeStone != null)
                    {
                        if (!string.IsNullOrEmpty(runeStone.m_name))
                        {
                            // All seven boss stones share one m_name ($guardianstone_name), so the
                            // component alone cannot say which boss this is - only the prefab can.
                            // Localization.Localize substitutes $tokens anywhere in a string, so
                            // "Eikthyr $guardianstone_name" renders as "Eikthyr Guardian stone"
                            // and stays correct in every language.
                            string prefabName = Utils.GetPrefabName(go);
                            const string bossPrefix = "BossStone_";
                            if (prefabName.StartsWith(bossPrefix, System.StringComparison.OrdinalIgnoreCase))
                                return Labels.Prettify(prefabName.Substring(bossPrefix.Length))
                                       + " " + runeStone.m_name;

                            return runeStone.m_name;
                        }
                        if (!string.IsNullOrEmpty(runeStone.m_pinName) && runeStone.m_pinName != "Pin")
                            return runeStone.m_pinName;
                    }
                    // Nothing usable on the component: fall through to the prettified prefab name,
                    // which gives "Boss Stone Eikthyr". Not ideal, but it names the thing.
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

                case PinCategory.Spawner:
                    string creature = SpawnedCreaturePrefabName(go);
                    string matched = Subtypes.Match(Subtypes.Spawners, creature);
                    WarnUnmatched("spawner", creature, matched);
                    return matched;

                default:
                    return null;
            }
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

            if (category == PinCategory.Dungeon || category == PinCategory.Camp ||
                category == PinCategory.Ore || category == PinCategory.BossAltar ||
                category == PinCategory.Trader || category == PinCategory.Spawner)
            {
                return Plugin.SubtypeIconFor(subtype, settings);
            }

            return settings.ResolvedPinType;
        }

        private static void TryPin(PinCategory category, string subtype, Vector3 pos, string label)
        {
            Plugin.CategorySettings settings = Plugin.SettingsFor(category);
            if (settings == null || !settings.Enabled.Value)
                return;

            // Ore additionally honours its per-type switch, so you can pin copper but ignore tin.
            if (category == PinCategory.Ore && !Plugin.OreTypeEnabled(subtype))
                return;

            // Dedupe on category+subtype: a category-wide radius would let a copper pin suppress
            // a tin node metres away, hiding a resource entirely.
            string key = string.IsNullOrEmpty(subtype) ? category.ToString() : $"{category}:{subtype}";

            if (PinRecord.Exists(key, pos, settings.DedupeRadius.Value))
                return;

            Minimap.PinType pinType = IconFor(category, subtype, settings);

            // AddPin rather than DiscoverLocation: the latter always fires a MessageHud toast,
            // which would spam the corner of the screen during bulk discovery.
            Minimap.instance.AddPin(pos, pinType, label ?? string.Empty,
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
