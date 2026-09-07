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

        public static void Enqueue(PinCategory category, Vector3 pos, GameObject go)
        {
            PendingQueue.Add(new Pending { Category = category, Pos = pos, Go = go });
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
            Patch_ZNetScene_AddInstance.ReportIfDue();
            AutoProbe();
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

                TryPin(p.Category, p.Pos, ResolveLabel(p.Category, p.Go));
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

                if (!TryClassifyLocation(loc, out PinCategory category, out string label))
                    continue;

                TryPin(category, pos, label);
            }
        }

        /// Classified from the location's own data, with no hardcoded prefab names.
        private static bool TryClassifyLocation(Location loc, out PinCategory category, out string label)
        {
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

            // m_hasInterior / m_generator is what actually makes something a dungeon - it's the
            // same data vanilla uses, and needs no prefab name list.
            if (loc.m_hasInterior || loc.m_generator != null)
            {
                category = PinCategory.Dungeon;
                // Generalised short name from the prefab ("Crypt2" -> "Crypt") in preference to
                // m_discoverLabel, which gives the game's full proper noun ("Burial Chambers").
                label = Labels.ForLocation(Utils.GetPrefabName(loc.gameObject));
                return true;
            }

            category = default;
            label = null;
            return false;
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
        private static string ResolveLabel(PinCategory category, GameObject go)
        {
            switch (category)
            {
                case PinCategory.Ore:
                    // Derived from the dropped item ("CopperOre" -> "Copper") rather than the
                    // component's m_name: the deposits that matter are plain Destructibles with
                    // no m_name at all, which is why these were showing up as "rock4_copper".
                    return Labels.ForOre(Utils.GetPrefabName(go));

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

        private static void TryPin(PinCategory category, Vector3 pos, string label)
        {
            Plugin.CategorySettings settings = Plugin.SettingsFor(category);
            if (settings == null || !settings.Enabled.Value)
            {
                Plugin.Log.LogInfo($"Skip {category} '{label}': category disabled");
                return;
            }

            if (PinRecord.Exists(category, pos, settings.DedupeRadius.Value))
            {
                Plugin.Log.LogInfo($"Skip {category} '{label}': already pinned within {settings.DedupeRadius.Value:F0}m");
                return;
            }

            // AddPin rather than DiscoverLocation: the latter always fires a MessageHud toast,
            // which would spam the corner of the screen during bulk discovery.
            Minimap.instance.AddPin(pos, settings.PinType.Value, label ?? string.Empty,
                save: true, isChecked: false);

            PinRecord.Add(category, pos);
            Plugin.Log.LogInfo($"Pinned {category} '{label}' at {pos.x:F0},{pos.z:F0}");
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
