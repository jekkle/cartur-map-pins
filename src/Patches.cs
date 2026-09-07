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
        private static void Postfix(ZDO zdo, ZNetView nview)
        {
            if (!PinCatalog.Built || zdo == null || nview == null)
                return;

            int hash = zdo.GetPrefab();
            if (!PinCatalog.TryGet(hash, out PinCategory category))
                return;   // the fast path: one int lookup for the overwhelming majority of calls

            // Locations (dungeons, altars, runestones) are handled by the Location sweep instead,
            // which gives us the Location component's own data to classify and label from.
            if (category == PinCategory.BossAltar || category == PinCategory.Runestone)
                return;

            if (category == PinCategory.Pickable && !Plugin.PickableGroupEnabled(PinCatalog.GroupOf(hash)))
                return;

            GameObject go = nview.gameObject;

            if (category == PinCategory.Beehive && !IsWildHive(zdo))
                return;

            if (category == PinCategory.Pickable)
            {
                // Pickable.Awake destroys stale one-shot pickables, so without this we can pin
                // something that is about to delete itself.
                Pickable pickable = go.GetComponent<Pickable>();
                if (pickable != null && !pickable.CanBePicked())
                    return;
            }

            PinPlacer.Enqueue(category, zdo.GetPosition(), go);
        }

        /// Read the creator straight off the ZDO rather than using Piece.IsPlacedByPlayer():
        /// Piece.m_creator is populated in Piece.Awake, Unity doesn't guarantee component Awake
        /// order on the same GameObject, and a player-built hive read too early looks wild.
        private static bool IsWildHive(ZDO zdo) => zdo.GetLong(ZDOVars.s_creator, 0L) == 0L;
    }
}
