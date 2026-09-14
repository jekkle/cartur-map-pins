using System.Collections.Generic;
using System.Reflection;
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
#if DIAGNOSTICS
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
#endif

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
            if ((category == PinCategory.Beehive || category == PinCategory.Chest ||
                 category == PinCategory.Prop) && !IsWild(zdo))
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

            // Ore nodes are registered whether or not they get pinned: the sweep that forgets a
            // mined-out deposit needs to know a node is still standing, and a node that was pinned
            // by an earlier session is seen again here rather than at pin time.
            if (category == PinCategory.Ore)
                OreRegistry.Add(go);

            _enqueued++;
            // The subtype carries through so the pin gets its own icon and dedupes only against
            // its own kind - copper against copper, a wolf den against other wolf dens.
            PinPlacer.Enqueue(category, zdo.GetPosition(), go, PinPlacer.SubtypeFor(category, hash, go));
        }

        private static bool IsWild(ZDO zdo) => zdo.GetLong(ZDOVars.s_creator, 0L) == 0L;
    }

    /// Marks a bed as home when it becomes your spawn point.
    ///
    /// Bed.Interact, not Bed.SetOwner. SetOwner only fires on the branch where the bed has no
    /// owner at all - claiming a fresh one. Walking back to a house you already own and choosing
    /// "set spawn point" takes the other branch:
    ///
    ///     if (owner == 0L)        { SetOwner(...); SetCustomSpawnPoint(GetSpawnPoint()); }
    ///     else if (IsMine())      { ... SetCustomSpawnPoint(GetSpawnPoint()); }   // no SetOwner
    ///
    /// so a SetOwner hook silently placed nothing for every bed claimed before the mod was
    /// installed, or claimed and then re-selected later. What the player was left looking at was
    /// vanilla's own m_spawnPointPin - which Minimap.UpdateProfilePins re-derives from the profile
    /// with name "" and save: false, so it carries no label and cannot be renamed or edited. With
    /// ReplaceBedMarker on it even wears our house icon, so it reads as a Home pin that has gone
    /// wrong rather than as a different pin entirely.
    ///
    /// A Postfix on Interact covers both branches with one patch, and asks the profile rather than
    /// the bed which branch ran: after the call, this bed is home exactly when the profile's custom
    /// spawn point is this bed's spawn point. Bed.IsMine and IsCurrent say the same thing but are
    /// private; GetCustomSpawnPoint and GetSpawnPoint are public and mean it directly. Somebody
    /// else's bed never matches, so a shared server puts nothing on your map.
    ///
    /// Sleeping in the bed that is already your spawn also matches. That is correct - it is your
    /// home - and dedupe makes the repeat a no-op.
    [HarmonyPatch(typeof(Bed), "Interact")]
    internal static class Patch_Bed_Interact
    {
        private static void Postfix(Bed __instance)
        {
            if (__instance == null || Player.m_localPlayer == null || Game.instance == null)
                return;

            PlayerProfile profile = Game.instance.GetPlayerProfile();
            if (profile == null || !profile.HaveCustomSpawnPoint())
                return;

            // The spawn point rather than the bed's own transform: it is where the game will put
            // you, which is the thing worth walking back to, and it is the position vanilla's own
            // marker uses - so the two land on the same spot and read as one house.
            Vector3 spawn = __instance.GetSpawnPoint();
            if ((profile.GetCustomSpawnPoint() - spawn).sqrMagnitude > 0.01f)
                return;

            PinPlacer.PinHome(spawn, "Home");
        }
    }

    /// Paints the colour, opacity and size a pin has been given.
    ///
    /// A Postfix on UpdatePins, because that is what undoes it: vanilla rewrites every pin's icon
    /// colour on each pass - white normally, grey when ticked - so a colour set once would last
    /// until the next frame. It leaves scale alone, but doing both here keeps the whole appearance
    /// in one place.
    ///
    /// Ticked pins are left to vanilla. The grey is how a ticked pin reads as done, and the
    /// dungeon tick depends on it.
    ///
    /// Costs nothing until a style exists: with none set the whole pass is one bool.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Patch_Minimap_UpdatePins
    {
        private static void Postfix(Minimap __instance)
        {
            if (!PinStyles.Any)
                return;

            List<Minimap.PinData> pins = MinimapAccess.GetPins(__instance);
            if (pins == null)
                return;

            Crowding.Measure(pins);

            foreach (Minimap.PinData pin in pins)
            {
                if (pin?.m_iconElement == null)
                    continue;

                PinStyles.Style style = PinStyles.For(pin.m_pos);

                // Scale is set every pass rather than only when styled, so a pin whose style was
                // taken away goes back to its old size instead of staying big forever.
                Vector3 scale = pin.m_iconElement.transform.localScale;
                float wanted = (style.IsDefault ? 1f : Mathf.Clamp(style.Size, 0.4f, 3f))
                               * Crowding.ScaleFor(pin);
                if (!Mathf.Approximately(scale.x, wanted))
                    pin.m_iconElement.transform.localScale = new Vector3(wanted, wanted, 1f);

                if (pin.m_checked)
                    continue;

                // A colour set on this pin by hand wins. Only when there is none does the ore
                // colour apply, so tinting by type never overrides a deliberate choice.
                Color? colour = PinStyles.ColourFor(style);
                if (colour.HasValue)
                    pin.m_iconElement.color = colour.Value;
                else if (PinStyles.TintFor(pin.m_type, out Color tint))
                    pin.m_iconElement.color = tint;
            }
        }
    }

    /// Removes vanilla's own boss-altar marker wherever we have placed ours, so enabling the
    /// BossAltar category doesn't leave two icons stacked on the same altar.
    ///
    /// Vanilla keeps these in a separate Dictionary&lt;Vector3, PinData&gt; (m_locationPins), refilled
    /// by UpdateLocationPins every 5s from ZoneSystem.GetLocationIcons - so this has to run after
    /// each refill rather than once, and a Postfix on that method is exactly that moment.
    ///
    /// Deliberately NOT a Prefix returning false: that would suppress every location marker,
    /// including Haldor's and the Ashlands upgrade station, which we do not replace. Only pins
    /// that coincide with one of our own BossAltar records are dropped.
    ///
    /// Until an altar is actually discovered and pinned by us, vanilla's marker is left alone -
    /// so nothing disappears, it is only ever replaced by the named, saved version.
    [HarmonyPatch(typeof(Minimap), "UpdateLocationPins")]
    internal static class Patch_Minimap_UpdateLocationPins
    {
        private static readonly FieldInfo LocationPins = AccessTools.Field(typeof(Minimap), "m_locationPins");

        /// Altars are large; vanilla's marker sits at the location centre while ours lands on the
        /// offering bowl, so this is generous rather than exact.
        private const float SamePlace = 12f;

        private static void Postfix(Minimap __instance)
        {
            Plugin.CategorySettings settings = Plugin.SettingsFor(PinCategory.BossAltar);
            if (settings == null || !settings.Enabled.Value)
                return;

            var pins = LocationPins?.GetValue(__instance) as Dictionary<Vector3, Minimap.PinData>;
            if (pins == null || pins.Count == 0)
                return;

            List<Vector3> drop = null;
            foreach (KeyValuePair<Vector3, Minimap.PinData> kv in pins)
            {
                if (!PinRecord.HasCategoryNear(PinCategory.BossAltar, kv.Key, SamePlace))
                    continue;
                (drop ?? (drop = new List<Vector3>())).Add(kv.Key);
            }

            if (drop == null)
                return;

            foreach (Vector3 key in drop)
            {
                __instance.RemovePin(pins[key]);
                pins.Remove(key);
            }
        }
    }
}
