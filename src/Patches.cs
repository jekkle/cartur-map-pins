using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Patch_ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance)
        {
            PinCatalog.Build(__instance);
            PinPlacer.Clear();
            // Containers from the previous world would otherwise linger as stale references.
            ChestRegistry.Clear();
        }
    }

    /// ZNetScene.AddInstance is public and fires exactly once per spawned networked object, from
    /// the last line of ZNetView.Awake - so this single hook covers every spawn path (network,
    /// zone generation, dungeon spawn) for ore, beehives and pickables alike.
    ///
    /// It also fires for every arrow, dropped item and creature, so the int hash-set test must be
    /// the first thing that happens here and nothing heavier may run before it.
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.AddInstance))]
    internal static class Patch_ZNetScene_AddInstance
    {
        // Diagnostics: the spawn-hook path went completely silent while the Location sweep
        // worked, and there was no way to tell from outside whether the hook wasn't firing or
        // every object was being filtered out. Counters + a periodic summary answer that.
        private static int _seen;
        private static int _matched;
        private static int _enqueued;
        private static int _skippedHive;
        private static int _skippedPicked;
        private static int _skippedGroup;
        private static float _nextReport;

        internal static void ReportIfDue()
        {
            if (UnityEngine.Time.realtimeSinceStartup < _nextReport)
                return;
            _nextReport = UnityEngine.Time.realtimeSinceStartup + 15f;
            Plugin.Log.LogInfo(
                $"AddInstance diag: seen={_seen} matched={_matched} enqueued={_enqueued} " +
                $"skipped(hive={_skippedHive} picked={_skippedPicked} group={_skippedGroup}) " +
                $"catalogBuilt={PinCatalog.Built} catalogSize={PinCatalog.Size} queue={PinPlacer.QueueSize}");
        }

        private static void Postfix(ZDO zdo, ZNetView nview)
        {
            if (!PinCatalog.Built || zdo == null || nview == null)
                return;

            _seen++;

            int hash = zdo.GetPrefab();
            if (!PinCatalog.TryGet(hash, out PinCategory category))
                return;   // the fast path: one int lookup for the overwhelming majority of calls

            _matched++;

            // Boss altars sit inside location prefabs, where the Location sweep can classify and
            // label them from the location's own data. Everything else is handled here - runestones
            // included, since many are placed as standalone world objects rather than locations.
            if (category == PinCategory.BossAltar)
                return;

            if (category == PinCategory.Pickable && !Plugin.PickableGroupEnabled(PinCatalog.GroupOf(hash)))
            {
                _skippedGroup++;
                return;
            }

            GameObject go = nview.gameObject;

            // Beehives and loot chests both exist in player-built form, and pinning someone's
            // base would be useless noise. Read the creator straight off the ZDO rather than via
            // Piece.IsPlacedByPlayer(): Piece.m_creator is populated in Piece.Awake, Unity
            // doesn't guarantee component Awake order, and a built one read too early looks wild.
            if ((category == PinCategory.Beehive || category == PinCategory.Chest) && !IsWild(zdo))
            {
                _skippedHive++;
                return;
            }

            if (category == PinCategory.Pickable)
            {
                // Pickable.Awake destroys stale one-shot pickables, so without this we can pin
                // something that is about to delete itself.
                Pickable pickable = go.GetComponent<Pickable>();
                if (pickable != null && !pickable.CanBePicked())
                {
                    _skippedPicked++;
                    return;
                }
            }

            // Wild loot containers are also kept in a registry, so the looted-chest sweep can
            // walk just these instead of every Container in the scene (bases included).
            if (category == PinCategory.Chest)
                ChestRegistry.Add(go.GetComponent<Container>());

            _enqueued++;
            // Ore carries its type through so the pin can be labelled "Copper" and deduped
            // against other copper only; pickables carry their group so each group gets its own
            // icon and its own dedupe radius.
            string subtype = category == PinCategory.Ore
                ? PinCatalog.OreTypeOf(hash)
                : category == PinCategory.Pickable
                    ? PinCatalog.GroupOf(hash).ToString()
                    : null;
            PinPlacer.Enqueue(category, zdo.GetPosition(), go, subtype);
        }

        private static bool IsWild(ZDO zdo) => zdo.GetLong(ZDOVars.s_creator, 0L) == 0L;
    }
}
