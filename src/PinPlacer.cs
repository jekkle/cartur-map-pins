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

            Vector3 playerPos = player.transform.position;

            DrainQueue(playerPos);
            SweepLocations(playerPos);
            SweepLootedChests(playerPos);
            PinRecord.Flush();
            Patch_ZNetScene_AddInstance.ReportIfDue();
            AutoProbe();
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
            if (Plugin.LootedChestIcon.Value < 0)
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
            Minimap.PinType lootedType = CustomIcons.Resolve(Plugin.LootedChestIcon.Value, normalType);
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

                // Replaced rather than mutated in place: a pin's sprite is resolved once when
                // it's created, and the field that forces a UI rebuild is private. Re-adding is
                // both simpler and guaranteed to render and persist correctly.
                string name = match.m_name;
                bool wasChecked = match.m_checked;
                Minimap.instance.RemovePin(match);
                Minimap.instance.AddPin(pos, wanted, name, save: true, isChecked: wasChecked);
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
            Probe.DumpLocations(null);
            Probe.DumpFonts(null);
        }

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

            if (dropped > 0 || interior > 0 || nearest < float.MaxValue)
            {
                Plugin.Log.LogInfo($"Drain: queued={PendingQueue.Count} dropped(unloaded)={dropped} " +
                                   $"interior={interior} nearest={(nearest < float.MaxValue ? nearest.ToString("F0") : "-")}m " +
                                   $"radius={radius:F0}m");
            }

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

            category = default;
            label = null;
            return false;
        }

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
                    break;

                case PinCategory.Chest:
                    Container container = go.GetComponent<Container>();
                    if (container != null && !string.IsNullOrEmpty(container.m_name))
                        return container.m_name;
                    break;

                case PinCategory.Spawner:
                case PinCategory.Wisp:
                    // "Spawner_GreydwarfNest" -> "Greydwarf Nest".
                    return Labels.Prettify(StripSpawnerPrefix(Utils.GetPrefabName(go)));

                case PinCategory.Beehive:
                    Beehive hive = go.GetComponent<Beehive>();
                    if (hive != null && !string.IsNullOrEmpty(hive.m_name))
                        return hive.m_name;
                    break;

                case PinCategory.Pickable:
                    Pickable pickable = go.GetComponent<Pickable>();
                    if (pickable != null)
                        return pickable.GetHoverName();
                    PickableItem item = go.GetComponent<PickableItem>();
                    if (item != null)
                        return item.GetHoverName();
                    break;
            }

            return Utils.GetPrefabName(go);
        }

        private static string StripSpawnerPrefix(string name)
        {
            foreach (string prefix in new[] { "Spawner_", "Spawn_" })
            {
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return name.Substring(prefix.Length);
            }
            return name;
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

            // Icons resolve per subtype where one exists: pickables by group, dungeons and camps
            // by kind. Otherwise the category's own setting applies.
            Minimap.PinType pinType = settings.ResolvedPinType;
            if (category == PinCategory.Pickable &&
                System.Enum.TryParse(subtype ?? string.Empty, out PickableGroup group))
            {
                pinType = Plugin.PickableIconFor(group, settings.PinType.Value);
            }
            else if (category == PinCategory.Dungeon || category == PinCategory.Camp)
            {
                pinType = Plugin.SubtypeIconFor(subtype, settings);
            }

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
