using System;
using System.Collections.Generic;
using System.Reflection;

namespace CarturMapPins
{
    /// Minimap.m_pins is private with no public accessor, so reading it needs reflection.
    /// Cached once; every failure path returns null so a game update renaming the field
    /// degrades to "cleanup can't find pins" rather than throwing on every call.
    internal static class MinimapAccess
    {
        private static readonly FieldInfo PinsField =
            typeof(Minimap).GetField("m_pins", BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool _warned;

        /// Root cause (read from Minimap.AddPin): vanilla ends AddPin with "if the new pin's type
        /// is filtered out, ToggleIconFilter(type)", which switches the whole type back on. That is
        /// right for a pin the player drops by hand, and wrong for one this mod places on its own:
        /// right-click a mushroom icon to hide mushrooms and the next mushroom discovered re-shows
        /// every mushroom pin on the map. This puts the filter back the way the player left it.
        public static Minimap.PinData AddPinKeepFilter(Minimap map, UnityEngine.Vector3 pos,
                                                       Minimap.PinType type, string name, bool save)
        {
            bool hidden = !CustomIcons.IsVisible(map, type);
            Minimap.PinData pin = map.AddPin(pos, type, name, save, false);
            if (hidden && CustomIcons.IsVisible(map, type))
                CustomIcons.ToggleFilter(map, type);
            return pin;
        }

        private static readonly FieldInfo PinUpdateRequiredField =
            typeof(Minimap).GetField("m_pinUpdateRequired", BindingFlags.NonPublic | BindingFlags.Instance);

        /// Minimap.Update calls UpdatePins only while m_pinUpdateRequired is set (Minimap.cs:756,
        /// read from the installed DLL), and UpdatePins is where vanilla resets every pin's colour
        /// and where this mod paints the looted-chest half opacity back on. Editing a pin in place
        /// touches neither, so the change waits for the next time the map happens to move.
        public static void RequestPinUpdate(Minimap map) => PinUpdateRequiredField?.SetValue(map, true);

        public static List<Minimap.PinData> GetPins(Minimap map)
        {
            if (map == null)
                return null;

            if (PinsField == null)
            {
                WarnOnce("Minimap.m_pins not found - pin cleanup unavailable.");
                return null;
            }

            try
            {
                return PinsField.GetValue(map) as List<Minimap.PinData>;
            }
            catch (Exception e)
            {
                WarnOnce($"Could not read Minimap.m_pins: {e.Message}");
                return null;
            }
        }

        private static void WarnOnce(string message)
        {
            if (_warned)
                return;
            _warned = true;
            Plugin.Log.LogWarning(message);
        }
    }

    /// Location keeps a private static registry of the locations currently loaded, the same
    /// pattern as Character.s_characters. Reading it is how dungeons/altars/runestones are found
    /// without any spawn hook: a Location only Awakes when its zone loads (~64-96m), so the list
    /// is inherently proximity-gated and small.
    ///
    /// Deliberately NOT using ZoneSystem.m_locationInstances instead - that's populated
    /// server-side only, so it is empty on a client connected to a dedicated server, and it would
    /// be a whole-world reveal anyway.
    internal static class LocationAccess
    {
        private static readonly FieldInfo AllLocationsField =
            typeof(Location).GetField("s_allLocations", BindingFlags.NonPublic | BindingFlags.Static);

        private static bool _warned;

        public static List<Location> GetAll()
        {
            if (AllLocationsField == null)
            {
                WarnOnce("Location.s_allLocations not found - dungeon/altar/runestone pinning disabled.");
                return null;
            }

            try
            {
                return AllLocationsField.GetValue(null) as List<Location>;
            }
            catch (Exception e)
            {
                WarnOnce($"Could not read Location.s_allLocations: {e.Message}");
                return null;
            }
        }

        private static void WarnOnce(string message)
        {
            if (_warned)
                return;
            _warned = true;
            Plugin.Log.LogWarning(message);
        }
    }
}
