using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Splatform;
using UnityEngine;
using UnityEngine.UI;
using PinData = Minimap.PinData;
using PinType = Minimap.PinType;

namespace CarturMapPins
{
    /// <summary>
    /// Tracks every physical cart separately. A cart is identified by its persistent ZDOID.
    /// When the local player detaches, that cart's existing saved map pin is moved to the
    /// cart's new position. If no pin exists yet, one is created.
    ///
    /// The small sidecar stores the relationship between cart ZDOID and the last known
    /// map-pin position/type/name plus any carried custom colour. The actual pin remains a
    /// normal saved Valheim map pin.
    /// </summary>
    internal static class CartTracker
    {
        private const string DefaultLabelFormat = "{player}'s Cart";
        private const string LegacyDefaultLabel = "Cart";
        private const int DefaultPinType = 3; // vanilla point-of-interest icon
        private const string PluginGuid = "com.jekkle.valheim.carturmappins";
        private const string ConfigSection = "Cart Tracking";
        private const float ExactRecoveryRadius = 0.15f;
        private const float NormalRecoveryRadius = 1.0f;
        private const string FileName = "carturmappins_carts_v1.tsv";

        private sealed class Record
        {
            public long WorldUid;
            public long PlayerId;
            public long CartUserId;
            public uint CartId;
            public Vector3 Position;
            public int PinType;
            public bool Checked;
            public string Label;
            public bool HasColour;
            public Color Colour;

            public string Key
            {
                get { return BuildKey(WorldUid, PlayerId, CartUserId, CartId); }
            }
        }

        private static readonly Dictionary<string, Record> Records =
            new Dictionary<string, Record>(StringComparer.Ordinal);

        // Runtime references avoid having to rediscover a pin after every detach in one session.
        private static readonly Dictionary<string, PinData> LivePins =
            new Dictionary<string, PinData>(StringComparer.Ordinal);
        private static readonly HashSet<string> HiddenPins =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool _loaded;
        private static bool _configBound;
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<PinIcon> _icon;
        private static ConfigEntry<bool> _syncIconToExisting;
        private static ConfigEntry<string> _labelFormat;
        private static ConfigEntry<bool> _removePinOnDestroy;
        private static ConfigEntry<bool> _hideWhileAttached;

        private static readonly System.Reflection.MethodInfo MinimapGetSprite =
            AccessTools.Method(typeof(Minimap), "GetSprite", new Type[] { typeof(PinType) });

        private static string StoragePath
        {
            get { return Path.Combine(Paths.ConfigPath, FileName); }
        }

        internal static bool IsEnabled
        {
            get
            {
                EnsureConfig();
                return _enabled == null || _enabled.Value;
            }
        }

        internal static void BindConfig(ConfigFile config)
        {
            if (_configBound || config == null)
            {
                return;
            }

            _enabled = config.Bind(
                ConfigSection,
                "Enabled",
                true,
                "Track each physical cart separately and move its saved map pin whenever you release it.");

            _icon = config.Bind(
                ConfigSection,
                "Icon",
                PinIcon.Default,
                new ConfigDescription(
                    "Cart icon. With SyncIconToExistingCarts enabled, tracked carts are kept on this icon too.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = IconDrawer.Draw
                    }));

            _syncIconToExisting = config.Bind(
                ConfigSection,
                "SyncIconToExistingCarts",
                true,
                "Keep existing tracked carts on the configured Icon. Turn this off if you want to choose a different icon for individual carts in the map editor.");

            _labelFormat = config.Bind(
                ConfigSection,
                "LabelFormat",
                DefaultLabelFormat,
                "Name used when a cart is first tracked. {player} is replaced with the current character name. Additional carts are numbered automatically.");

            _removePinOnDestroy = config.Bind(
                ConfigSection,
                "RemovePinOnDestroy",
                true,
                "Remove a tracked cart's map pin when that cart is actually destroyed. Zone unloading does not count as destruction.");

            _hideWhileAttached = config.Bind(
                ConfigSection,
                "HideWhileAttached",
                true,
                "Hide a tracked cart's map pin while the local player is pulling it, then restore it at the new position when released.");

            _configBound = true;
        }

        internal static void EnsureConfig()
        {
            if (_configBound)
            {
                return;
            }

            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info) ||
                info.Instance == null)
            {
                return;
            }

            BindConfig(info.Instance.Config);
        }

        internal static bool HideWhileAttached
        {
            get
            {
                EnsureConfig();
                return _hideWhileAttached == null || _hideWhileAttached.Value;
            }
        }

        private static bool SyncIconToExisting
        {
            get
            {
                EnsureConfig();
                return _syncIconToExisting == null || _syncIconToExisting.Value;
            }
        }

        internal static bool TryGetCartId(Vagon cart, out ZDOID id)
        {
            id = ZDOID.None;
            if ((UnityEngine.Object)cart == null)
            {
                return false;
            }

            ZNetView nview = ((Component)cart).GetComponent<ZNetView>();
            if ((UnityEngine.Object)nview == null || !nview.IsValid())
            {
                return false;
            }

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
            {
                return false;
            }

            id = zdo.m_uid;
            return id.UserID != 0L || id.ID != 0U;
        }

        internal static void Update(Vagon cart, ZDOID cartId)
        {
            if (!IsEnabled)
            {
                return;
            }

            if ((UnityEngine.Object)cart == null || (UnityEngine.Object)Minimap.instance == null)
            {
                return;
            }

            long worldUid;
            long playerId;
            if (!TryContext(out worldUid, out playerId))
            {
                return;
            }

            EnsureLoaded();

            string key = BuildKey(worldUid, playerId, cartId.UserID, cartId.ID);
            Vector3 position = ((Component)cart).transform.position;
            bool wasHidden = HiddenPins.Remove(key);

            Record record;
            Records.TryGetValue(key, out record);

            PinData pin = FindLivePin(key);
            if (pin == null && record != null && !wasHidden)
            {
                pin = RecoverSavedPin(Minimap.instance, record);
            }

            bool migrateLegacyLabel = record != null &&
                string.Equals(record.Label, LegacyDefaultLabel, StringComparison.Ordinal);

            PinType type = (record != null)
                ? (PinType)record.PinType
                : ConfiguredPinType();
            string label = (record != null && !string.IsNullOrEmpty(record.Label) && !migrateLegacyLabel)
                ? record.Label
                : BuildInitialLabel(worldUid, playerId);
            bool isChecked = record != null && record.Checked;
            bool hasColour = record != null && record.HasColour;
            Color colour = record != null ? record.Colour : Color.white;

            if (pin != null)
            {
                if (record == null)
                {
                    record = new Record
                    {
                        WorldUid = worldUid,
                        PlayerId = playerId,
                        CartUserId = cartId.UserID,
                        CartId = cartId.ID,
                        Position = pin.m_pos
                    };
                }

                CapturePinState(record, pin, false);

                if (!SyncIconToExisting)
                {
                    type = pin.m_type;
                }
                else
                {
                    type = ConfiguredPinType();
                }

                if (!string.IsNullOrEmpty(pin.m_name))
                {
                    label = (migrateLegacyLabel &&
                        string.Equals(pin.m_name, LegacyDefaultLabel, StringComparison.Ordinal))
                        ? BuildInitialLabel(worldUid, playerId)
                        : pin.m_name;
                }
                isChecked = pin.m_checked;
                hasColour = record.HasColour;
                colour = record.Colour;

                // Re-add rather than only changing m_pos: this keeps Minimap's runtime UI/cache
                // consistent and guarantees the moved coordinates are part of normal map saving.
                Minimap.instance.RemovePin(pin);
            }
            else if (SyncIconToExisting)
            {
                type = ConfiguredPinType();
            }

            PinData moved = Minimap.instance.AddPin(
                position,
                type,
                label,
                true,
                isChecked,
                0L,
                default(PlatformUserID));

            if (moved == null)
            {
                Plugin.Log.LogWarning((object)"Cart tracking: Minimap.AddPin returned null.");
                return;
            }

            LivePins[key] = moved;
            Records[key] = new Record
            {
                WorldUid = worldUid,
                PlayerId = playerId,
                CartUserId = cartId.UserID,
                CartId = cartId.ID,
                Position = position,
                PinType = (int)moved.m_type,
                Checked = moved.m_checked,
                Label = moved.m_name ?? BuildInitialLabel(worldUid, playerId),
                HasColour = hasColour,
                Colour = colour
            };

            ApplyStoredColour(moved, Records[key]);
            Save();
            Plugin.Log.LogInfo((object)("Cart tracking: updated cart " +
                cartId.UserID.ToString(CultureInfo.InvariantCulture) + ":" +
                cartId.ID.ToString(CultureInfo.InvariantCulture) +
                " at " + FormatPosition(position) + "."));
        }

        internal static void Hide(Vagon cart, ZDOID cartId)
        {
            if (!IsEnabled || !HideWhileAttached || (UnityEngine.Object)Minimap.instance == null)
            {
                return;
            }

            long worldUid;
            long playerId;
            if (!TryContext(out worldUid, out playerId))
            {
                return;
            }

            EnsureLoaded();
            string key = BuildKey(worldUid, playerId, cartId.UserID, cartId.ID);

            Record record;
            if (!Records.TryGetValue(key, out record))
            {
                // A brand-new cart has no stale pin to hide yet. It will be created on release.
                return;
            }

            PinData pin = FindLivePin(key);
            if (pin == null)
            {
                pin = RecoverSavedPin(Minimap.instance, record);
            }

            if (pin != null)
            {
                CapturePinState(record, pin, false);
                record.Position = pin.m_pos;
                Minimap.instance.RemovePin(pin);
            }

            Records[key] = record;
            LivePins.Remove(key);
            HiddenPins.Add(key);
            Save();

            Plugin.Log.LogInfo((object)("Cart tracking: hid attached cart " +
                cartId.UserID.ToString(CultureInfo.InvariantCulture) + ":" +
                cartId.ID.ToString(CultureInfo.InvariantCulture) + "."));
        }

        internal static void Remove(ZDOID cartId)
        {
            EnsureConfig();
            if (_removePinOnDestroy != null && !_removePinOnDestroy.Value)
            {
                return;
            }

            long worldUid;
            long playerId;
            if (!TryContext(out worldUid, out playerId))
            {
                return;
            }

            EnsureLoaded();

            string key = BuildKey(worldUid, playerId, cartId.UserID, cartId.ID);
            Record record;
            Records.TryGetValue(key, out record);

            PinData pin = FindLivePin(key);
            if (pin == null && record != null && (UnityEngine.Object)Minimap.instance != null)
            {
                pin = RecoverSavedPin(Minimap.instance, record);
            }

            if (pin != null && (UnityEngine.Object)Minimap.instance != null)
            {
                Minimap.instance.RemovePin(pin);
            }

            LivePins.Remove(key);
            HiddenPins.Remove(key);
            if (Records.Remove(key))
            {
                Save();
                Plugin.Log.LogInfo((object)("Cart tracking: removed destroyed cart " +
                    cartId.UserID.ToString(CultureInfo.InvariantCulture) + ":" +
                    cartId.ID.ToString(CultureInfo.InvariantCulture) + "."));
            }
        }

        internal static void ClearRuntime()
        {
            LivePins.Clear();
            HiddenPins.Clear();
        }

        /// <summary>
        /// Number of tracked carts belonging to the currently loaded world and character.
        /// This deliberately counts carts whose pin is temporarily hidden while attached: the
        /// cart is still on record even though there is no parked marker to draw.
        /// </summary>
        internal static int CountCurrentContext()
        {
            long worldUid;
            long playerId;
            if (!TryContext(out worldUid, out playerId))
            {
                return 0;
            }

            EnsureLoaded();
            int count = 0;
            foreach (Record record in Records.Values)
            {
                if (record.WorldUid == worldUid && record.PlayerId == playerId)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Removes all tracked-cart pins and records for the current world/character. Called by
        /// PinRecord.RemoveAll so Cartur's existing ResetPins and carturpins_clear include carts.
        /// </summary>
        internal static int ClearCurrentContext()
        {
            long worldUid;
            long playerId;
            if (!TryContext(out worldUid, out playerId))
            {
                return 0;
            }

            EnsureLoaded();
            Minimap map = Minimap.instance;
            var removeKeys = new List<string>();

            foreach (KeyValuePair<string, Record> pair in Records)
            {
                Record record = pair.Value;
                if (record.WorldUid != worldUid || record.PlayerId != playerId)
                {
                    continue;
                }

                removeKeys.Add(pair.Key);

                if ((UnityEngine.Object)map == null || HiddenPins.Contains(pair.Key))
                {
                    continue;
                }

                PinData pin = FindLivePin(pair.Key);
                if (pin == null)
                {
                    pin = RecoverSavedPin(map, record);
                }
                if (pin != null)
                {
                    map.RemovePin(pin);
                }
            }

            foreach (string key in removeKeys)
            {
                Records.Remove(key);
                LivePins.Remove(key);
                HiddenPins.Remove(key);
            }

            if (removeKeys.Count > 0)
            {
                Save();
                if ((UnityEngine.Object)map != null)
                {
                    map.SaveMapData();
                }
            }

            return removeKeys.Count;
        }

        /// <summary>
        /// Forgets current-context cart records whose parked marker is no longer on the map.
        /// Intentionally hidden attached carts are skipped. This mirrors PinRecord.ForgetMissing.
        /// </summary>
        internal static int ForgetMissingCurrentContext()
        {
            long worldUid;
            long playerId;
            Minimap map = Minimap.instance;
            if ((UnityEngine.Object)map == null || !TryContext(out worldUid, out playerId))
            {
                return 0;
            }

            EnsureLoaded();
            var removeKeys = new List<string>();

            foreach (KeyValuePair<string, Record> pair in Records)
            {
                Record record = pair.Value;
                if (record.WorldUid != worldUid || record.PlayerId != playerId ||
                    HiddenPins.Contains(pair.Key))
                {
                    continue;
                }

                PinData pin = FindLivePin(pair.Key);
                if (pin == null)
                {
                    pin = RecoverSavedPin(map, record);
                }
                if (pin == null)
                {
                    removeKeys.Add(pair.Key);
                }
            }

            foreach (string key in removeKeys)
            {
                Records.Remove(key);
                LivePins.Remove(key);
                HiddenPins.Remove(key);
            }

            if (removeKeys.Count > 0)
            {
                Save();
            }
            return removeKeys.Count;
        }

        /// <summary>
        /// Repoints every visible tracked cart to the configured cart icon. This is deliberately
        /// stronger than SyncIconToExistingCarts: carturpins_reicon is an explicit request to put
        /// every Cartur-owned pin back on its configured icon.
        /// </summary>
        internal static int ReiconCurrentContext()
        {
            long worldUid;
            long playerId;
            Minimap map = Minimap.instance;
            if ((UnityEngine.Object)map == null || !TryContext(out worldUid, out playerId))
            {
                return 0;
            }

            EnsureLoaded();
            PinType wanted = ConfiguredPinType();
            int changed = 0;
            bool dirty = false;

            foreach (KeyValuePair<string, Record> pair in Records)
            {
                Record record = pair.Value;
                if (record.WorldUid != worldUid || record.PlayerId != playerId ||
                    HiddenPins.Contains(pair.Key))
                {
                    continue;
                }

                PinData pin = FindLivePin(pair.Key);
                if (pin == null)
                {
                    pin = RecoverSavedPin(map, record);
                    if (pin != null)
                    {
                        LivePins[pair.Key] = pin;
                    }
                }

                if (pin == null || pin.m_type == wanted)
                {
                    continue;
                }

                if (PinRecord.Repoint(map, pin, wanted))
                {
                    record.PinType = (int)wanted;
                    changed++;
                    dirty = true;
                }
            }

            if (dirty)
            {
                Save();
                map.SaveMapData();
            }
            return changed;
        }

        /// <summary>
        /// True when a CartTracker record owns the supplied parked-pin position in the current
        /// world/character. PinRecord.RecordedAt consults this so Cartur's existing audit/dedupe
        /// code recognises cart markers as owned. Hidden attached carts deliberately do not claim
        /// their old position.
        /// </summary>
        internal static bool OwnsVisiblePinAt(Vector3 position)
        {
            long worldUid;
            long playerId;
            if (!TryContext(out worldUid, out playerId))
            {
                return false;
            }

            EnsureLoaded();
            foreach (KeyValuePair<string, Record> pair in Records)
            {
                Record record = pair.Value;
                if (record.WorldUid != worldUid || record.PlayerId != playerId ||
                    HiddenPins.Contains(pair.Key))
                {
                    continue;
                }

                Vector3 d = record.Position - position;
                if (d.x * d.x + d.z * d.z < 0.25f)
                {
                    return true;
                }
            }
            return false;
        }

        internal static void ApplyVisualOverrides(Minimap map)
        {
            if ((UnityEngine.Object)map == null || !IsEnabled)
            {
                return;
            }

            long worldUid;
            long playerId;
            if (!TryContext(out worldUid, out playerId))
            {
                return;
            }

            EnsureLoaded();
            bool dirty = false;

            // Re-associate visible saved pins after a restart so config icon changes can
            // affect already-tracked carts without waiting for each cart to be touched.
            foreach (KeyValuePair<string, Record> pair in Records)
            {
                Record record = pair.Value;
                if (record.WorldUid != worldUid || record.PlayerId != playerId || HiddenPins.Contains(pair.Key))
                {
                    continue;
                }

                PinData pin = FindLivePin(pair.Key);
                if (pin == null)
                {
                    pin = RecoverSavedPin(map, record);
                    if (pin != null)
                    {
                        LivePins[pair.Key] = pin;
                    }
                }

                if (pin == null)
                {
                    continue;
                }

                if (SyncIconToExisting)
                {
                    PinType desired = ConfiguredPinType();
                    if (pin.m_type != desired)
                    {
                        pin.m_type = desired;
                        record.PinType = (int)desired;
                        RefreshPinSprite(map, pin);
                        dirty = true;
                    }
                }

                Color? currentStyleColour = CustomColourAt(pin.m_pos);
                if (currentStyleColour.HasValue)
                {
                    record.HasColour = true;
                    record.Colour = currentStyleColour.Value;
                }
                else
                {
                    ApplyStoredColour(pin, record);
                }
            }

            if (dirty)
            {
                Save();
            }
        }

        private static void CapturePinState(Record record, PinData pin, bool clearColourWhenMissing)
        {
            if (record == null || pin == null)
            {
                return;
            }

            record.PinType = (int)pin.m_type;
            record.Checked = pin.m_checked;
            record.Label = pin.m_name ?? record.Label;

            Color? colour = CustomColourAt(pin.m_pos);
            if (colour.HasValue)
            {
                record.HasColour = true;
                record.Colour = colour.Value;
            }
            else if (clearColourWhenMissing)
            {
                record.HasColour = false;
                record.Colour = Color.white;
            }
        }

        private static Color? CustomColourAt(Vector3 position)
        {
            try
            {
                var style = PinStyles.For(position);
                return PinStyles.ColourFor(style);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyStoredColour(PinData pin, Record record)
        {
            if (pin == null || record == null || !record.HasColour || pin.m_checked ||
                (UnityEngine.Object)pin.m_iconElement == null)
            {
                return;
            }

            ((Graphic)pin.m_iconElement).color = record.Colour;
        }

        private static void RefreshPinSprite(Minimap map, PinData pin)
        {
            if ((UnityEngine.Object)map == null || pin == null ||
                (UnityEngine.Object)pin.m_iconElement == null || MinimapGetSprite == null)
            {
                return;
            }

            try
            {
                Sprite sprite = MinimapGetSprite.Invoke(map, new object[] { pin.m_type }) as Sprite;
                if ((UnityEngine.Object)sprite != null)
                {
                    pin.m_iconElement.sprite = sprite;
                }
            }
            catch
            {
                // The pin type is still updated; the normal map refresh will repair the sprite.
            }
        }

        private static PinType ConfiguredPinType()
        {
            EnsureConfig();
            PinIcon configured = (_icon != null) ? _icon.Value : PinIcon.Default;
            return CustomIcons.Resolve((int)configured, (PinType)DefaultPinType);
        }

        private static string BuildInitialLabel(long worldUid, long playerId)
        {
            EnsureConfig();

            string playerName = "Player";
            Player local = Player.m_localPlayer;
            if ((UnityEngine.Object)local != null)
            {
                try
                {
                    string currentName = local.GetPlayerName();
                    if (!string.IsNullOrWhiteSpace(currentName))
                    {
                        playerName = currentName.Trim();
                    }
                }
                catch
                {
                    // Keep the safe fallback name.
                }
            }

            string format = (_labelFormat != null) ? _labelFormat.Value : DefaultLabelFormat;
            if (string.IsNullOrWhiteSpace(format))
            {
                format = DefaultLabelFormat;
            }

            string baseLabel = format.Replace("{player}", playerName).Trim();
            if (baseLabel.Length == 0)
            {
                baseLabel = playerName + "'s Cart";
            }

            if (!LabelInUse(worldUid, playerId, baseLabel))
            {
                return baseLabel;
            }

            for (int number = 2; number < 10000; number++)
            {
                string candidate = baseLabel + " " + number.ToString(CultureInfo.InvariantCulture);
                if (!LabelInUse(worldUid, playerId, candidate))
                {
                    return candidate;
                }
            }

            // This is practically unreachable, but still produce a unique-looking label.
            return baseLabel + " " + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private static bool LabelInUse(long worldUid, long playerId, string label)
        {
            foreach (KeyValuePair<string, Record> pair in Records)
            {
                Record record = pair.Value;
                if (record.WorldUid == worldUid &&
                    record.PlayerId == playerId &&
                    string.Equals(record.Label ?? string.Empty, label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryContext(out long worldUid, out long playerId)
        {
            worldUid = 0L;
            playerId = 0L;

            if ((UnityEngine.Object)ZNet.instance == null ||
                (UnityEngine.Object)Player.m_localPlayer == null)
            {
                return false;
            }

            // Current Valheim builds expose GetWorldUID(), but the underlying world field
            // is not part of the public API in every build. During world load/unload,
            // GetWorldUID() can also briefly hit an uninitialised world, so guard it.
            try
            {
                worldUid = ZNet.instance.GetWorldUID();
            }
            catch (NullReferenceException)
            {
                return false;
            }

            playerId = Player.m_localPlayer.GetPlayerID();
            return worldUid != 0L && playerId != 0L;
        }

        private static PinData FindLivePin(string key)
        {
            PinData pin;
            if (!LivePins.TryGetValue(key, out pin) || pin == null)
            {
                return null;
            }

            if ((UnityEngine.Object)Minimap.instance == null)
            {
                return null;
            }

            List<PinData> pins = MinimapAccess.GetPins(Minimap.instance);
            if (pins == null || !pins.Contains(pin))
            {
                LivePins.Remove(key);
                return null;
            }

            return pin;
        }

        private static PinData RecoverSavedPin(Minimap map, Record record)
        {
            List<PinData> pins = MinimapAccess.GetPins(map);
            if (pins == null)
            {
                return null;
            }

            // First use all metadata we persisted. This is the normal recovery path after restart.
            PinData best = null;
            float bestDistance = NormalRecoveryRadius * NormalRecoveryRadius;
            for (int i = 0; i < pins.Count; i++)
            {
                PinData candidate = pins[i];
                if (candidate == null || !candidate.m_save ||
                    (int)candidate.m_type != record.PinType ||
                    !string.Equals(candidate.m_name ?? string.Empty,
                        record.Label ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                float distance = DistanceXZSquared(candidate.m_pos, record.Position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best != null)
            {
                return best;
            }

            // If the user changed the marker's icon/name after the final detach, recover only from
            // an almost-exact old position. Keeping this radius tiny avoids stealing unrelated pins.
            bestDistance = ExactRecoveryRadius * ExactRecoveryRadius;
            for (int i = 0; i < pins.Count; i++)
            {
                PinData candidate = pins[i];
                if (candidate == null || !candidate.m_save)
                {
                    continue;
                }

                float distance = DistanceXZSquared(candidate.m_pos, record.Position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static float DistanceXZSquared(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static string BuildKey(long worldUid, long playerId, long cartUserId, uint cartId)
        {
            return worldUid.ToString(CultureInfo.InvariantCulture) + ":" +
                   playerId.ToString(CultureInfo.InvariantCulture) + ":" +
                   cartUserId.ToString(CultureInfo.InvariantCulture) + ":" +
                   cartId.ToString(CultureInfo.InvariantCulture);
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            Records.Clear();

            string path = StoragePath;
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                    {
                        continue;
                    }

                    Record record;
                    if (TryParse(line, out record))
                    {
                        Records[record.Key] = record;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning((object)("Cart tracking: could not load " + path + ": " + ex.Message));
            }
        }

        private static bool TryParse(string line, out Record record)
        {
            record = null;
            string[] p = line.Split('\t');
            if (p.Length != 10 && p.Length != 15)
            {
                return false;
            }

            long worldUid;
            long playerId;
            long cartUserId;
            uint cartId;
            float x;
            float y;
            float z;
            int pinType;
            bool isChecked;

            if (!long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out worldUid) ||
                !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out playerId) ||
                !long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out cartUserId) ||
                !uint.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out cartId) ||
                !float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out z) ||
                !int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out pinType) ||
                !bool.TryParse(p[8], out isChecked))
            {
                return false;
            }

            string label;
            try
            {
                label = Encoding.UTF8.GetString(Convert.FromBase64String(p[9]));
            }
            catch
            {
                return false;
            }

            bool hasColour = false;
            Color colour = Color.white;
            if (p.Length == 15)
            {
                bool parsedHasColour;
                float r;
                float g;
                float b;
                float a;
                if (!bool.TryParse(p[10], out parsedHasColour) ||
                    !float.TryParse(p[11], NumberStyles.Float, CultureInfo.InvariantCulture, out r) ||
                    !float.TryParse(p[12], NumberStyles.Float, CultureInfo.InvariantCulture, out g) ||
                    !float.TryParse(p[13], NumberStyles.Float, CultureInfo.InvariantCulture, out b) ||
                    !float.TryParse(p[14], NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                {
                    return false;
                }

                hasColour = parsedHasColour;
                colour = new Color(r, g, b, a);
            }

            record = new Record
            {
                WorldUid = worldUid,
                PlayerId = playerId,
                CartUserId = cartUserId,
                CartId = cartId,
                Position = new Vector3(x, y, z),
                PinType = pinType,
                Checked = isChecked,
                Label = label,
                HasColour = hasColour,
                Colour = colour
            };
            return true;
        }

        private static void Save()
        {
            string path = StoragePath;
            string temp = path + ".tmp";

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Cartur Map Pins - tracked carts v2");
                sb.AppendLine("# worldUid\tplayerId\tzdoUserId\tzdoId\tx\ty\tz\tpinType\tchecked\tlabelBase64\thasColour\tr\tg\tb\ta");

                foreach (KeyValuePair<string, Record> pair in Records)
                {
                    Record r = pair.Value;
                    string label = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(r.Label ?? "Cart"));

                    sb.Append(r.WorldUid.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.PlayerId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.CartUserId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.CartId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Position.x.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Position.y.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Position.z.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.PinType.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Checked.ToString()).Append('\t')
                      .Append(label).Append('\t')
                      .Append(r.HasColour.ToString()).Append('\t')
                      .Append(r.Colour.r.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Colour.g.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Colour.b.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Colour.a.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                }

                File.WriteAllText(temp, sb.ToString());
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                    catch (IOException)
                    {
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning((object)("Cart tracking: could not save " + path + ": " + ex.Message));
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                    // Best effort only.
                }
            }
        }

        private static string FormatPosition(Vector3 p)
        {
            return p.x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                   p.z.ToString("F1", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Hide an already-tracked cart's parked marker as soon as the local player starts pulling it.
    /// The record itself is retained so release can restore the same cart at its new position.
    /// </summary>
    [HarmonyPatch(typeof(Vagon), "AttachTo")]
    internal static class Patch_Vagon_AttachTo_CartTracking
    {
        private static void Postfix(Vagon __instance)
        {
            if (!CartTracker.IsEnabled || !CartTracker.HideWhileAttached)
            {
                return;
            }

            Player local = Player.m_localPlayer;
            if ((UnityEngine.Object)local == null ||
                (UnityEngine.Object)__instance == null ||
                !__instance.IsAttached(local))
            {
                return;
            }

            ZDOID id;
            if (CartTracker.TryGetCartId(__instance, out id))
            {
                CartTracker.Hide(__instance, id);
            }
        }
    }

    /// <summary>
    /// Capture the local-player relationship before Vagon.Detach clears it, then move/create
    /// that physical cart's map marker after detachment completes.
    /// </summary>
    [HarmonyPatch(typeof(Vagon), "Detach")]
    internal static class Patch_Vagon_Detach_CartTracking
    {
        private sealed class State
        {
            public readonly ZDOID CartId;

            public State(ZDOID cartId)
            {
                CartId = cartId;
            }
        }

        private static void Prefix(Vagon __instance, out State __state)
        {
            __state = null;

            if (!CartTracker.IsEnabled)
            {
                return;
            }

            Player local = Player.m_localPlayer;
            if ((UnityEngine.Object)local == null ||
                (UnityEngine.Object)__instance == null ||
                !__instance.IsAttached(local))
            {
                return;
            }

            ZDOID id;
            if (!CartTracker.TryGetCartId(__instance, out id))
            {
                return;
            }

            // Assign the complete state object once. This avoids Harmony analyzer warnings
            // about mutating fields on a non-ref __state value.
            __state = new State(id);
        }

        private static void Postfix(Vagon __instance, State __state)
        {
            if (__state != null)
            {
                CartTracker.Update(__instance, __state.CartId);
            }
        }
    }

    /// <summary>
    /// Container.OnDestroyed is the actual destruction callback, unlike Unity OnDestroy which
    /// also fires for ordinary zone unloading. Remove only markers belonging to cart containers.
    /// </summary>
    [HarmonyPatch(typeof(Container), "OnDestroyed")]
    internal static class Patch_Container_OnDestroyed_CartTracking
    {
        private static void Prefix(Container __instance)
        {
            if ((UnityEngine.Object)__instance == null)
            {
                return;
            }

            Vagon cart = ((Component)__instance).GetComponentInParent<Vagon>();
            if ((UnityEngine.Object)cart == null)
            {
                return;
            }

            ZDOID id;
            if (CartTracker.TryGetCartId(cart, out id))
            {
                CartTracker.Remove(id);
            }
        }
    }

    /// <summary>
    /// Cartur stores custom pin colours by position. A tracked cart moves, so its old style key no
    /// longer follows it. Run after normal pin styling and carry the stored cart colour forward.
    /// This pass also keeps existing cart icons synced to the configured cart icon when requested.
    /// </summary>
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Patch_Minimap_UpdatePins_CartTracking
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Minimap __instance)
        {
            CartTracker.ApplyVisualOverrides(__instance);
        }
    }

    /// <summary>
    /// Drop runtime PinData references when a world scene is rebuilt. The persisted sidecar and
    /// Valheim's saved pins remain intact and will be reconciled on the next cart detach.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Patch_ZNetScene_Awake_CartTracking
    {
        private static void Prefix()
        {
            CartTracker.EnsureConfig();
            CartTracker.ClearRuntime();
        }
    }
}
