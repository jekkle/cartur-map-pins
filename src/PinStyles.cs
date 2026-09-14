using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CarturMapPins
{
    /// Per-pin colour, size and opacity, kept in their own file beside the pin record.
    ///
    /// Not in the pin record, because that file answers "did we pin this already" and covers only
    /// pins the mod placed - while a style can be put on any pin, including one placed by hand,
    /// which is most of the point. Not in the map save either: PinData has no field to spare and
    /// adding one would mean rewriting the game's own save format.
    ///
    /// Keyed by position rounded to the metre. A pin does not move, so its position is its
    /// identity; the rounding absorbs the float drift between a saved position and a reloaded one.
    internal static class PinStyles
    {
        /// White means "leave it alone", which is why it is first and is the default.
        public static readonly Color[] Palette =
        {
            Color.white,
            new Color(0.85f, 0.20f, 0.18f),   // red
            new Color(0.94f, 0.55f, 0.16f),   // orange
            new Color(0.94f, 0.84f, 0.28f),   // yellow
            new Color(0.36f, 0.74f, 0.36f),   // green
            new Color(0.26f, 0.48f, 0.92f),   // blue
            new Color(0.62f, 0.31f, 0.85f),   // purple
            new Color(0.18f, 0.18f, 0.20f),   // black, lifted enough to read on a dark map
        };

        public static readonly string[] PaletteNames =
            { "Original", "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Black" };

        public struct Style
        {
            public int Colour;
            public float Size;
            public float Alpha;

            public bool IsDefault => Colour <= 0 && Mathf.Approximately(Size, 1f) && Mathf.Approximately(Alpha, 1f);
        }

        public static readonly Style Default = new Style { Colour = 0, Size = 1f, Alpha = 1f };

        private static readonly Dictionary<long, Style> Styles = new Dictionary<long, Style>();
        private static string _path;
        private static bool _dirty;

        /// Metre resolution in both axes, packed into one long. Two pins closer than a metre would
        /// share a style, which cannot happen in practice - the map refuses to place a pin within
        /// a metre of another.
        private static long KeyFor(Vector3 pos) =>
            ((long)Mathf.RoundToInt(pos.x) << 32) ^ (uint)Mathf.RoundToInt(pos.z);

        public static Style For(Vector3 pos) =>
            Styles.TryGetValue(KeyFor(pos), out Style style) ? style : Default;

        public static bool Any => Styles.Count > 0 || OreTints.Count > 0;

        public static void Set(Vector3 pos, Style style)
        {
            long key = KeyFor(pos);
            if (style.IsDefault)
            {
                if (!Styles.Remove(key))
                    return;
            }
            else
            {
                Styles[key] = style;
            }

            _dirty = true;
            Save();
        }

        public static void Load(string configDir)
        {
            _path = Path.Combine(configDir, "com.jekkle.valheim.carturmappins.styles.txt");
            Styles.Clear();

            if (!File.Exists(_path))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(_path))
                {
                    // x|z|colour|size|alpha
                    string[] parts = line.Split('|');
                    if (parts.Length != 5)
                        continue;
                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                        !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) ||
                        !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int colour) ||
                        !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float size) ||
                        !float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float alpha))
                        continue;

                    Styles[((long)x << 32) ^ (uint)z] = new Style { Colour = colour, Size = size, Alpha = alpha };
                }
                Plugin.Log.LogInfo($"Loaded {Styles.Count} pin style(s).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read pin styles, starting empty: {e.Message}");
            }
        }

        private static void Save()
        {
            if (!_dirty || string.IsNullOrEmpty(_path))
                return;

            try
            {
                var lines = new List<string>(Styles.Count);
                foreach (KeyValuePair<long, Style> kv in Styles)
                {
                    int x = (int)(kv.Key >> 32);
                    int z = (int)(uint)kv.Key;
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}",
                        x, z, kv.Value.Colour, kv.Value.Size, kv.Value.Alpha));
                }
                File.WriteAllLines(_path, lines.ToArray());
                _dirty = false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write pin styles: {e.Message}");
            }
        }

        /// Ore colours, keyed by the pin type that carries each ore's icon.
        ///
        /// By type rather than by position, which is the whole reason this is cheap: an ore type
        /// already has an icon of its own, so it already has a pin type of its own, and painting
        /// becomes a dictionary hit on a number the pin is already carrying. No per-pin record,
        /// nothing to keep in step as pins come and go.
        ///
        /// Rebuilt whenever the map is built, so changing an ore's icon in the settings takes
        /// effect on the next map rather than the next launch.
        ///
        /// The catch, stated rather than hidden: two things sharing an icon share the colour. Tar
        /// deposits and tar blob spawners both use the tar glyph, so a tar blob spawner comes out
        /// tar-coloured too. That reads as correct more often than not.
        private static readonly Dictionary<Minimap.PinType, Color> OreTints =
            new Dictionary<Minimap.PinType, Color>();

        public static void RebuildOreTints()
        {
            OreTints.Clear();
            if (!Plugin.TintOreByType.Value)
                return;

            Plugin.CategorySettings ore = Plugin.SettingsFor(PinCategory.Ore);
            if (ore == null)
                return;

            foreach (KeyValuePair<string, string> entry in Subtypes.OreColours)
            {
                if (!ColorUtility.TryParseHtmlString("#" + entry.Value, out Color colour))
                    continue;

                Minimap.PinType type = Plugin.SubtypeIconFor(entry.Key, ore);
                if (CustomIcons.IsCustom(type))
                    OreTints[type] = colour;
            }
        }

        public static bool TintFor(Minimap.PinType type, out Color colour) =>
            OreTints.TryGetValue(type, out colour);

        /// The colour to draw a pin in, or null to leave vanilla's own alone.
        public static Color? ColourFor(Style style)
        {
            if (style.IsDefault)
                return null;

            Color colour = style.Colour > 0 && style.Colour < Palette.Length
                ? Palette[style.Colour]
                : Color.white;
            colour.a = Mathf.Clamp01(style.Alpha);
            return colour;
        }
    }
}
