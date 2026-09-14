using System;

namespace CarturMapPins
{
    /// What the map is being searched for.
    ///
    /// Valheim has no way to find a pin, so a map with two hundred of them is read by eye. Typing
    /// "copper" dims everything that is not copper rather than hiding it: a map that removes pins
    /// while you search cannot be used to judge where things are in relation to each other, which
    /// is the reason you were looking.
    internal static class PinFilter
    {
        private static string _text = string.Empty;

        public static string Text
        {
            get => _text;
            set => _text = value ?? string.Empty;
        }

        public static bool Active => _text.Length > 0;

        /// Matched against the pin's name, which is what the player sees and therefore what they
        /// will type. Case-insensitive and a substring, so "cop" finds Copper and "spawn" finds
        /// every spawner.
        public static bool Matches(string name) =>
            !Active || (!string.IsNullOrEmpty(name) &&
                        name.IndexOf(_text, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
