using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// Keeps this mod's pins off the cartography table when the player asks for that.
    ///
    /// Minimap.GetSharedMapData is the single funnel - MapTable.GetMapData is its only caller in
    /// the whole game - and it already skips any pin whose m_save is false, and any pin of type
    /// Death. So there is no need to rewrite the package or reimplement the format: clearing
    /// m_save for the duration of the call makes vanilla's own filter do the work.
    ///
    /// The restore is a Finalizer rather than a Postfix because a Finalizer runs even when the
    /// original throws. A pin left with m_save false would be dropped from the player's own save
    /// the next time the game wrote it - losing pins is far worse than sharing them, so the
    /// restore must not be skippable.
    ///
    /// Reading other players' pins off a table is untouched; that is AddSharedMapData.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.GetSharedMapData))]
    internal static class Patch_Minimap_GetSharedMapData
    {
        private static readonly List<Minimap.PinData> Withheld = new List<Minimap.PinData>();

        private static void Prefix(Minimap __instance)
        {
            Withheld.Clear();

            Plugin.PinSharingMode mode = Plugin.PinSharing.Value;
            if (mode == Plugin.PinSharingMode.Everything)
                return;

            List<Minimap.PinData> pins = MinimapAccess.GetPins(__instance);
            if (pins == null)
                return;

            // Only resolved for the mode that needs it - OwnPins walks the record against the map.
            HashSet<Minimap.PinData> mine = mode == Plugin.PinSharingMode.HandPlacedOnly
                ? PinRecord.OwnPins(__instance)
                : null;

            foreach (Minimap.PinData pin in pins)
            {
                if (pin == null || !pin.m_save)
                    continue;                       // already not shared; nothing to restore later
                if (mine != null && !mine.Contains(pin))
                    continue;                       // hand-placed, and this mode shares those

                pin.m_save = false;
                Withheld.Add(pin);
            }
        }

        private static void Finalizer()
        {
            foreach (Minimap.PinData pin in Withheld)
            {
                if (pin != null)
                    pin.m_save = true;
            }
            Withheld.Clear();
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Patch_ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance)
        {
            PinCatalog.Build(__instance);
            PinPlacer.Clear();
            // Containers from the previous world would otherwise linger as stale references.
            ChestRegistry.Clear();
            // Records are per world per character, so the previous world's must be dropped before
            // anything asks this world whether a thing is already pinned.
            PinRecord.Unload();
            IconRepoint.Reset();
            // Same reason, plus the tracked pins belong to a Minimap that is being replaced.
            Trackers.Unload();
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
        // Diagnostics: counters plus a periodic summary, to tell "the hook is not firing" from
        // "every object is being filtered out".
        //
        // Counted through Tally because ReportIfDue is the only reader and it is not compiled into
        // a release, so the increments would be work done for nobody in the one method that runs
        // for every arrow, dropped item and creature. [Conditional] removes the call at each site
        // and leaves the method reading identically in both builds; bracketing six increments in
        // #if would not. The fields stay because the calls still have to bind.
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
            {
                // A second int lookup, only for things the first one rejected. It covers the one case
                // the sweep cannot see: a deposit that shatters destroys itself and leaves its
                // _frac pieces behind, which are not pinnable but ARE the deposit. Without it the
                // copper pin goes the moment the rock breaks.
                if (PinCatalog.TryGetOreFragment(hash, out string fragmentOre))
                    OreRegistry.Add(nview.gameObject, fragmentOre);
                return;   // the fast path for the overwhelming majority of calls
            }

            Tally(ref _matched);

            // Boss altars sit inside location prefabs, where the Location sweep can classify and
            // label them from the location's own data. Everything else is handled here - runestones
            // included, since many are placed as standalone world objects rather than locations.
            if (category == PinCategory.BossAltar)
                return;

            // Resolved once and carried into the queue. The switch is read again when the pin is
            // placed, because a queued pickable waits for the player to walk up to it and the
            // switch can be turned off in between.
            PickableGroup group = category == PinCategory.Pickable
                ? PinCatalog.GroupOf(hash)
                : PickableGroup.Other;

            if (category == PinCategory.Pickable && !Plugin.PickableGroupEnabled(group))
            {
                Tally(ref _skippedGroup);
                return;
            }

            GameObject go = nview.gameObject;

            // Beehives and loot chests both exist in player-built form, and pinning someone's base
            // would be noise. Read the creator straight off the ZDO rather than via
            // Piece.IsPlacedByPlayer(): Piece.m_creator is populated in Piece.Awake, Unity does not
            // guarantee component Awake order, and a built one read too early looks wild.
            // This catches a built piece arriving over the network, where the creator is already
            // on the ZDO. It does NOT catch one the local player just placed - see the second
            // check in PinPlacer.DrainQueue.
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
            // mined-out deposit needs to know a node is still standing, and a node pinned in an
            // earlier session is seen again here rather than at pin time.
            if (category == PinCategory.Ore)
                OreRegistry.Add(go, PinCatalog.OreTypeOf(hash));

            Tally(ref _enqueued);
            // The subtype gives the pin its own icon and dedupes it only against its own kind -
            // copper against copper, a wolf den against other wolf dens.
            PinPlacer.Enqueue(category, zdo.GetPosition(), go, PinPlacer.SubtypeFor(category, hash, go), group);
        }

        private static bool IsWild(ZDO zdo) => zdo.GetLong(ZDOVars.s_creator, 0L) == 0L;
    }

    /// Drops the pin on a pickable that has been picked and will never come back.
    ///
    /// Berries, mushrooms and crops regrow, so their pins are right to keep. A surtling core
    /// stand, a Dyrnwyn fragment or a treasure pile does not: once taken, the pin marks an empty
    /// patch of ground forever.
    ///
    /// Read from Pickable.SetPicked: both branches that write the respawn time are gated on
    /// m_respawnTimeMinutes being greater than zero, so zero or less means it never comes back.
    ///
    /// SetPicked rather than RPC_Pick or Interact: it is public, it is the single place m_picked
    /// is written, and RPC_SetPicked is a four-instruction forwarder to it - so this one hook
    /// covers the player who picked it and everyone who hears about it. Awake does NOT call it,
    /// which keeps a world load from running this hundreds of times; the cost is that pins left
    /// by an older version are not swept up, and carturpins_forget_missing exists for those.
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.SetPicked))]
    internal static class Patch_Pickable_SetPicked
    {
        /// Deliberately tight. The record keeps the pin's position, which can sit up to a dedupe
        /// radius (five metres) from the thing that was scanned, and several core stands can share
        /// a chamber. Matching only what is underfoot means a cluster keeps its pin until the one
        /// the pin sits on is taken. That leaves a pin standing a little too long; the other way
        /// round removes a pin that still marks something, and nothing re-places it until the
        /// zone reloads.
        private const float Underfoot = 2f;

        private static void Postfix(Pickable __instance, bool picked)
        {
            if (!picked || __instance == null || __instance.m_respawnTimeMinutes > 0f)
                return;

            if (Minimap.instance == null || PinRecord.Count == 0)
                return;

            GameObject go = __instance.gameObject;
            Vector3 pos = go.transform.position;
            int hash = Utils.GetPrefabName(go).GetStableHashCode();

            // Its own kind only, so a core stand cannot take the pin off the chest beside it.
            string subtype = PinPlacer.SubtypeFor(PinCategory.Pickable, hash, go);

            if (PinRecord.Forget(PinCategory.Pickable, pos, Underfoot, subtype))
                Plugin.Log.LogInfo($"{subtype ?? "Pickable"} pin at {pos.x:F0},{pos.z:F0} removed - picked, and it does not respawn.");
        }
    }

    /// Drops the pin on a loose item once somebody picks it up.
    ///
    /// Coins, amber, pearls, rubies and the rest of the dungeon floor loot are PickableItem, not
    /// Pickable - a separate MonoBehaviour that shares no base class with it, so the patch above
    /// never sees them.
    ///
    /// No respawn test here, on purpose. Read off the DLL, PickableItem has no respawn field at
    /// all and RPC_Pick ends with m_nview.Destroy(). One pick and the object is gone for good, so
    /// the pin is always wrong afterwards.
    ///
    /// Interact rather than RPC_Pick: RPC_Pick returns immediately unless m_nview.IsOwner(), so on
    /// a client picking something the server owns it would never run locally and the pin would
    /// stay on the map of the one person who took the item. Interact runs on whoever pressed the
    /// key, which is whose map needs correcting. It returns false only when the ZNetView is
    /// invalid, and true once the pick has been sent.
    [HarmonyPatch(typeof(PickableItem), nameof(PickableItem.Interact))]
    internal static class Patch_PickableItem_Interact
    {
        private static void Postfix(PickableItem __instance, bool __result)
        {
            if (!__result || __instance == null)
                return;

            if (Minimap.instance == null || PinRecord.Count == 0)
                return;

            GameObject go = __instance.gameObject;
            Vector3 pos = go.transform.position;
            int hash = Utils.GetPrefabName(go).GetStableHashCode();
            string subtype = PinPlacer.SubtypeFor(PinCategory.Pickable, hash, go);

            // Same tight radius and reason as the Pickable patch above: the record holds the pin's
            // position, which can sit a dedupe radius from the thing that was scanned, and a pile
            // of loot is several objects close together.
            if (PinRecord.Forget(PinCategory.Pickable, pos, 2f, subtype))
                Plugin.Log.LogInfo($"{subtype ?? "Item"} pin at {pos.x:F0},{pos.z:F0} removed - picked up.");
        }
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
    /// so a SetOwner hook places nothing for a bed claimed before the mod was installed, or
    /// claimed and then re-selected later. The player is left with vanilla's own m_spawnPointPin,
    /// which Minimap.UpdateProfilePins re-derives from the profile with name "" and save: false,
    /// so it has no label and cannot be renamed or edited. With ReplaceBedMarker on it even wears
    /// our house icon, so it reads as a Home pin that has gone wrong.
    ///
    /// A Postfix on Interact covers both branches, and asks the profile rather than the bed which
    /// branch ran: after the call, this bed is home exactly when the profile's custom spawn point
    /// is this bed's spawn point. Bed.IsMine and IsCurrent say the same thing but are private;
    /// GetCustomSpawnPoint and GetSpawnPoint are public. Somebody else's bed never matches, so a
    /// shared server puts nothing on your map.
    ///
    /// Sleeping in the bed that is already your spawn also matches, which is correct, and dedupe
    /// makes the repeat a no-op.
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
            // you, and the position vanilla's own marker uses, so the two land on the same spot.
            Vector3 spawn = __instance.GetSpawnPoint();
            if ((profile.GetCustomSpawnPoint() - spawn).sqrMagnitude > 0.01f)
                return;

            PinPlacer.PinHome(spawn, "Home");
        }
    }

    /// Paints the colour and opacity a pin has been given.
    ///
    /// A Postfix on UpdatePins, because that is what undoes it: vanilla rewrites every pin's icon
    /// colour on each pass - white normally, grey when ticked - so a colour set once would last
    /// until the next frame. Scale is PinFade's, see below.
    ///
    /// Ticked pins are left to vanilla. The grey is how a ticked pin reads as done, and the
    /// dungeon tick depends on it.
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

                // Size and whether it is drawn at all are PinFade's, which eases both from the frame
                // hook. Setting them in this pass too would fight it: UpdatePins runs only when
                // the map moves, so a pin would jump on the frame the map moved and glide the rest
                // of the time. Colour has to stay here, since vanilla rewrites it on each pass.
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
                // but not at the weight of one you have not opened. Half opacity rather than a
                // tick: ticking is vanilla's own "done" state and the dungeon sweep already uses
                // it, so a ticked chest would mean two things at once.
                //
                // Applied last and multiplied into whatever alpha the colour above left, so a pin
                // you have faded by hand stays faded.
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
        /// Both names are offered to the table because neither covers every boss alone: the
        /// location name says "GDKing" where the boss prefab says "gd_king", and the Queen has no
        /// altar location - she lives in the Infested Citadel, whose name says nothing about her,
        /// so only the pin name identifies her.
        public static string BossFor(string pinName)
        {
            if (pinName == null || !ByPinName.TryGetValue(pinName, out string locationName))
                return null;

            // The location name is the stronger claim, so it goes second: Match tries its second
            // name ahead of its first.
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
    /// run and deleting its pin afterwards cannot tell vanilla's pin from ours whenever a boss is
    /// left on the default Boss icon - same position, same name, same type - so it would delete
    /// ours and leave the altar unmarked.
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
    /// each refill rather than once. The Postfix gates itself to the refill frame, see below.
    ///
    /// Deliberately NOT a Prefix returning false: that would suppress every location marker,
    /// including Haldor's and the Ashlands upgrade station, which we do not replace. Only pins
    /// that coincide with one of our own records in a Replaced category are dropped, and until an
    /// altar is discovered and pinned by us, vanilla's marker is left alone.
    [HarmonyPatch(typeof(Minimap), "UpdateLocationPins")]
    internal static class Patch_Minimap_UpdateLocationPins
    {
        private static readonly FieldInfo LocationPins = AccessTools.Field(typeof(Minimap), "m_locationPins");

        /// The countdown UpdateLocationPins throttles itself with. Read to tell a refill frame from
        /// the hundreds of frames in between - see the Postfix. A FieldRef rather than
        /// FieldInfo.GetValue, which boxes a float every frame. Null when the field is gone, so
        /// the gate drops out as the Postfix says it will.
        private static readonly AccessTools.FieldRef<Minimap, float> LocationsTimer =
            AccessTools.Field(typeof(Minimap), "m_updateLocationsTimer") != null
                ? AccessTools.FieldRefAccess<Minimap, float>("m_updateLocationsTimer")
                : null;

        private static float _lastTimer;

        /// Altars are large; vanilla's marker sits at the location centre while ours lands on the
        /// offering bowl, so this is generous rather than exact.
        private const float SamePlace = 12f;

        /// Categories where vanilla marks the same place we do: boss altars (see above) and traders.
        /// ZoneSystem.GetLocationIcons hands back every placed location flagged m_iconPlaced, so
        /// Haldor, Hildir and the Bog Witch each get an unnamed vanilla marker under our named,
        /// saved one - two icons on one spot, which the crowding pass counts as two things and
        /// shrinks both.
        private static readonly PinCategory[] Replaced =
        {
            PinCategory.BossAltar,
            PinCategory.Trader,
        };

        private static void Postfix(Minimap __instance)
        {
            // This Postfix runs every frame, not every 5s: the 5s throttle is the method's own first
            // instructions - "m_updateLocationsTimer -= dt; if (> 0) return;" - while the game
            // calls the method every frame (Minimap.Update -> UpdateDynamicPins), and a Harmony
            // postfix fires on the early return like any other. Without a gate, everything below
            // would walk the dictionary and ask PinRecord about every entry at frame rate.
            //
            // The timer only goes up where the refill resets it to 5; every other frame it counts
            // down, so a rise is a refill and nothing else. Reading the rise rather than testing
            // for 5 keeps this working if a game update changes the interval. If the field is
            // renamed the gate drops out and this runs per frame again - correct, just not cheap.
            if (LocationsTimer != null)
            {
                float timer = LocationsTimer(__instance);
                bool refilled = timer > _lastTimer;
                _lastTimer = timer;
                if (!refilled)
                    return;
            }

            var pins = LocationPins?.GetValue(__instance) as Dictionary<Vector3, Minimap.PinData>;
            if (pins == null || pins.Count == 0)
                return;

            // The pins the map is actually drawing. An entry dropped on an earlier refill keeps its
            // key here on purpose (see the removal loop below), so it comes round again every 5s,
            // and RemovePin sets m_pinUpdateRequired, which costs a full UpdatePins rebuild. Its
            // PinData is out of m_pins, which is how an already-dropped one is told apart from a
            // freshly added one.
            List<Minimap.PinData> live = MinimapAccess.GetPins(__instance);

            List<Vector3> drop = null;
            foreach (KeyValuePair<Vector3, Minimap.PinData> kv in pins)
            {
                if (kv.Value == null || (live != null && !live.Contains(kv.Value)))
                    continue;

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

            // RemovePin only - the key stays in m_locationPins deliberately. Vanilla's refill is
            // "if (!m_locationPins.ContainsKey(key)) { AddPin; m_locationPins.Add; ZLog.Log }", so
            // taking the key out would tell it this location is new again and it would re-add the
            // marker on every tick, with a log line and an m_pinUpdateRequired rebuild each time.
            // Vanilla's own first loop drops the key when the location stops being reported, so
            // nothing is leaked.
            foreach (Vector3 key in drop)
                __instance.RemovePin(pins[key]);
        }
    }

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
        /// Harmony throws on a target it cannot find, which would take every other patch down with
        /// it if a game update renamed this private method. Prepare is how a patch declines
        /// instead.
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
}
