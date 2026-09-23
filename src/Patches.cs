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
            // Records are per world per character now, so the previous world's must be dropped
            // before anything asks this world whether a thing is already pinned.
            PinRecord.Unload();
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
        //
        // Counted through Tally rather than written to directly. ReportIfDue is the only thing
        // that ever reads them and it is not compiled into a release, so in a shipped build the
        // additions were work done for nobody - in the one method that runs for every arrow,
        // dropped item and creature in the world. [Conditional] removes the call at each site,
        // which leaves the method reading identically in both builds; bracketing six increments
        // in #if would not. The fields themselves stay because the calls still have to bind.
        [System.Diagnostics.Conditional("DIAGNOSTICS")]
        private static void Tally(ref int counter) => counter++;

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

            Tally(ref _seen);

            int hash = zdo.GetPrefab();
            if (!PinCatalog.TryGet(hash, out PinCategory category))
                return;   // the fast path: one int lookup for the overwhelming majority of calls

            Tally(ref _matched);

            // Boss altars sit inside location prefabs, where the Location sweep can classify and
            // label them from the location's own data. Everything else is handled here - runestones
            // included, since many are placed as standalone world objects rather than locations.
            if (category == PinCategory.BossAltar)
                return;

            if (category == PinCategory.Pickable && !Plugin.PickableGroupEnabled(PinCatalog.GroupOf(hash)))
            {
                Tally(ref _skippedGroup);
                return;
            }

            GameObject go = nview.gameObject;

            // Beehives and loot chests both exist in player-built form, and pinning someone's
            // base would be useless noise. Read the creator straight off the ZDO rather than via
            // Piece.IsPlacedByPlayer(): Piece.m_creator is populated in Piece.Awake, Unity
            // doesn't guarantee component Awake order, and a built one read too early looks wild.
            // This catches a built piece arriving over the network, where the creator is already
            // on the ZDO. It does NOT catch one the local player just placed - see the second
            // check in PinPlacer.DrainQueue for why.
            if (PinCatalog.PlayerBuildable(category) && !IsWild(zdo))
            {
                Tally(ref _skippedHive);
                return;
            }

            if (category == PinCategory.Pickable)
            {
                // Pickable.Awake destroys stale one-shot pickables, so without this we can pin
                // something that is about to delete itself.
                Pickable pickable = go.GetComponent<Pickable>();
                if (pickable != null && !pickable.CanBePicked())
                {
                    Tally(ref _skippedPicked);
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

            Tally(ref _enqueued);
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
            List<Minimap.PinData> pins = MinimapAccess.GetPins(__instance);
            if (pins == null)
                return;

            Crowding.Measure(pins);
            LabelCrowding.Apply(pins);

            foreach (Minimap.PinData pin in pins)
            {
                if (pin?.m_iconElement == null)
                    continue;

                // Size and whether it is drawn at all are PinFade's, which eases both from the
                // frame hook rather than setting them here. Doing it in this pass too would fight
                // it: UpdatePins runs only when the map moves, so a pin would jump on the frame
                // the map moved and glide the rest of the time.
                //
                // Colour stays here, and has to: vanilla rewrites every pin's colour on each pass
                // of this method, so anything set outside it lasts until the next one.
                PinStyles.Style style = PinStyles.For(pin.m_pos);

                // Dimmed rather than hidden: a search that removes pins cannot answer "where is
                // this in relation to everything else", which is usually why you were looking.
                if (PinFilter.Active && !PinFilter.Matches(pin.m_name))
                {
                    Color faded = pin.m_iconElement.color;
                    faded.a = 0.15f;
                    pin.m_iconElement.color = faded;
                    continue;
                }

                if (!pin.m_checked)
                {
                    // A colour set on this pin by hand wins. Only when there is none does the ore
                    // colour apply, so tinting by type never overrides a deliberate choice.
                    Color? colour = PinStyles.ColourFor(style);
                    if (colour.HasValue)
                        pin.m_iconElement.color = colour.Value;
                    else if (PinStyles.TintFor(pin.m_type, out Color tint))
                        pin.m_iconElement.color = tint;
                }

                // An emptied chest is still worth marking - it says the spot has been dealt with -
                // but it is not worth the same weight as one you have not opened. Half opacity
                // rather than a tick: ticking is vanilla's own "done" state and the dungeon sweep
                // already uses it, so a chest reading as ticked would mean two things at once.
                //
                // Applied last, and multiplied into whatever alpha the colour above left, so a pin
                // you have faded by hand stays faded rather than being reset to half.
                Minimap.PinType looted = PinPlacer.LootedChestType();
                if (looted != Minimap.PinType.None && pin.m_type == looted)
                {
                    Color faded = pin.m_iconElement.color;
                    faded.a *= 0.5f;
                    pin.m_iconElement.color = faded;
                }
            }
        }
    }

    /// Remembers which location a vegvisir or a guardian stone asked about, keyed by the pin name
    /// the answer will carry back.
    ///
    /// A reveal is a round trip. Vegvisir.Interact and RuneStone.Interact both call
    /// Game.DiscoverClosestLocation(locationName, ...), which routes an RPC to the server - the
    /// only machine that holds ZoneSystem.m_locationInstances - and the server answers with a
    /// position and the pin name it was handed. By the time Minimap.DiscoverLocation runs, the
    /// location's name is gone, and a client cannot get it back: the altar is thousands of metres
    /// away, so there is no Location component in the scene to ask.
    ///
    /// Keyed by pin name rather than paired with the reply in order, because one vegvisir can ask
    /// about several locations in a single click (Vegvisir.m_locations is a list) and each answer
    /// comes back as its own RPC, in whatever order the server sends them.
    internal static class DiscoveryRequests
    {
        private static readonly Dictionary<string, string> ByPinName = new Dictionary<string, string>();

        public static void Record(string pinName, string locationName)
        {
            if (!string.IsNullOrEmpty(pinName))
                ByPinName[pinName] = locationName;
        }

        /// Which boss this reveal is for, or null when it is not one of ours - a trader, Hildir's
        /// camps, or a discovery some other mod made without going through a runestone.
        ///
        /// Both names are offered to the table because neither covers every boss on its own: the
        /// location name says "GDKing" where the boss prefab says "gd_king", and the Queen has no
        /// altar location at all - she lives in the Infested Citadel, whose name says nothing
        /// about her, so only the pin name identifies her.
        public static string BossFor(string pinName)
        {
            if (pinName == null || !ByPinName.TryGetValue(pinName, out string locationName))
                return null;

            // The location name is the stronger claim, so it is tried first - Match takes its
            // second name ahead of its first.
            string boss = Subtypes.Match(Subtypes.Bosses, pinName, locationName);
            if (boss == null)
                Plugin.Log.LogInfo($"Revealed '{locationName}' as '{pinName}' - no boss matched, left to vanilla.");
            return boss;
        }
    }

    /// Records the location name on its way out. A Prefix rather than a Postfix only so the entry
    /// is in place before any reply can arrive.
    [HarmonyPatch(typeof(Game), nameof(Game.DiscoverClosestLocation))]
    internal static class Patch_Game_DiscoverClosestLocation
    {
        private static void Prefix(string name, string pinName) => DiscoveryRequests.Record(pinName, name);
    }

    /// Puts our own altar pin on a boss revealed by a vegvisir or a guardian stone, instead of
    /// vanilla's generic Boss marker.
    ///
    /// A Prefix returning false, which this mod otherwise avoids: the pin the original adds is
    /// precisely the thing being replaced, so there is nothing for a Postfix to add to. Letting it
    /// run and deleting its pin afterwards was the alternative, and it cannot tell vanilla's pin
    /// from ours whenever a boss is left on the default Boss icon - same position, same name, same
    /// type - so it would delete ours and leave the altar unmarked.
    ///
    /// Everything the original does apart from adding that pin is done here instead, so the toast
    /// and the map behave as before. Only the toast's little icon is dropped: Minimap.GetSprite is
    /// private and the message reads the same without it.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.DiscoverLocation))]
    internal static class Patch_Minimap_DiscoverLocation
    {
        /// Matches Patch_Minimap_UpdateLocationPins: the altar is large and its pin is not placed
        /// to the metre, so "is one of ours already here" is asked generously.
        private const float SamePlace = 12f;

        private static bool Prefix(Minimap __instance, Vector3 pos, string name, bool showMap, ref bool __result)
        {
            if (Player.m_localPlayer == null)
                return true;

            string boss = DiscoveryRequests.BossFor(name);
            if (boss == null)
                return true;

            // Asked before pinning, because afterwards the answer is always yes. This is vanilla's
            // HaveSimilarPin case: the player has read the same stone twice.
            bool already = PinRecord.HasCategoryNear(PinCategory.BossAltar, pos, SamePlace);

            // Switched off, either the whole category or this one boss. Vanilla's pin is better
            // than no pin, so the original is left to place it.
            if (!PinPlacer.PinDiscovered(boss, pos, name))
                return true;

            if (already)
            {
                if (showMap)
                {
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_pin_exist");
                    __instance.ShowPointOnMap(pos);
                }
                __result = false;
                return false;
            }

            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "$msg_pin_added: " + name);
            if (showMap)
                __instance.ShowPointOnMap(pos);
            __result = true;
            return false;
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

        /// Clears a death pin once its grave has been emptied.
    ///
    /// GiveBoost is the exact moment: TombStone.UpdateDespawn calls it inside
    /// "if (!m_container.IsInUse() && m_container.GetInventory().NrOfItems() <= 0)", immediately
    /// before m_nview.Destroy(), and it has no other call site. Patching UpdateDespawn instead
    /// would mean restating that condition here, which is the kind of copy that goes quietly wrong
    /// when the game changes one half of it.
    ///
    /// Only fires on the client that owns the grave, because the branch it sits in is already
    /// inside "if (m_nview.IsOwner())". That is the same client whose map holds the pin in all but
    /// an odd multiplayer case, since the pin is saved locally and the grave is yours.
    [HarmonyPatch(typeof(TombStone), "GiveBoost")]
    internal static class Patch_TombStone_GiveBoost
    {
        /// Harmony throws on a target it cannot find, and CreateAndPatchAll is wrapped now - so a
        /// private method renamed by a game update would take every other patch down with it.
        /// Prepare is how a patch declines instead.
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(TombStone), "GiveBoost") != null)
                return true;

            Plugin.Log.LogWarning("TombStone.GiveBoost not found - death pins will not clear themselves. Everything else still works.");
            return false;
        }

        /// A Prefix, because the grave is destroyed moments later and its position is wanted now.
        private static void Prefix(TombStone __instance)
        {
            if (__instance != null)
                PinPlacer.ClearDeathPin(__instance.transform.position);
        }
    }

    /// Categories where vanilla marks the same place we do. Traders are the second:
        /// ZoneSystem.GetLocationIcons hands back every placed location flagged m_iconPlaced, so
        /// Haldor, Hildir and the Bog Witch each get an unnamed vanilla marker under our named,
        /// saved one - two icons on one spot, and the crowding pass counts them as two things and
        /// shrinks both.
        private static readonly PinCategory[] Replaced =
        {
            PinCategory.BossAltar,
            PinCategory.Trader,
        };

        private static void Postfix(Minimap __instance)
        {
            var pins = LocationPins?.GetValue(__instance) as Dictionary<Vector3, Minimap.PinData>;
            if (pins == null || pins.Count == 0)
                return;

            List<Vector3> drop = null;
            foreach (KeyValuePair<Vector3, Minimap.PinData> kv in pins)
            {
                foreach (PinCategory category in Replaced)
                {
                    Plugin.CategorySettings settings = Plugin.SettingsFor(category);
                    if (settings == null || !settings.Enabled.Value)
                        continue;
                    if (!PinRecord.HasCategoryNear(category, kv.Key, SamePlace))
                        continue;

                    (drop ?? (drop = new List<Vector3>())).Add(kv.Key);
                    break;
                }
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
