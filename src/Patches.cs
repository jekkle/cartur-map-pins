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

            // Locations (dungeons, altars, runestones) are handled by the Location sweep instead,
            // which gives us the Location component's own data to classify and label from.
            if (category == PinCategory.BossAltar || category == PinCategory.Runestone)
                return;

            if (category == PinCategory.Pickable && !Plugin.PickableGroupEnabled(PinCatalog.GroupOf(hash)))
            {
                _skippedGroup++;
                return;
            }

            GameObject go = nview.gameObject;

            if (category == PinCategory.Beehive && !IsWildHive(zdo))
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

            _enqueued++;
            PinPlacer.Enqueue(category, zdo.GetPosition(), go);
        }

        /// Read the creator straight off the ZDO rather than using Piece.IsPlacedByPlayer():
        /// Piece.m_creator is populated in Piece.Awake, Unity doesn't guarantee component Awake
        /// order on the same GameObject, and a player-built hive read too early looks wild.
        private static bool IsWildHive(ZDO zdo) => zdo.GetLong(ZDOVars.s_creator, 0L) == 0L;
    }
}
