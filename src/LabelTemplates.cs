using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;

namespace CarturMapPins
{
    /// Short labels for automatic pins, written by the player: "B7" instead of "Raspberries",
    /// "SC" instead of "Sunken Crypt". Requested for a shared-map naming convention.
    ///
    /// Display only. The pin's saved name is never touched, for two reasons. {qty} is the merged
    /// count, which depends on zoom (a merge patch is one icon wide in metres), so no single
    /// value of it could be saved. And search, the rename box and the pin list keep reading the
    /// full name.
    ///
    /// The text has to be written on every pass. Vanilla sets a label once, when its marker is
    /// built (PinNameData.SetTextAndGameObject, read from the installed DLL), and rebuilds markers
    /// as pins scroll on screen. TMP_Text.set_text returns early on an identical string (also
    /// read from the DLL), so a pass that changes nothing costs a string compare per pin.
    ///
    /// A pin renamed by hand keeps its name: PinRecord.AutoLabelKey only answers for a pin still
    /// carrying the text this mod last wrote.
    internal static class LabelTemplates
    {
        private const string FileName = "carturpins_labels.txt";

        /// Record key ("Ore:Copper") -> template. Null until read for this world.
        private static Dictionary<string, string> _templates;

        /// Pins given a template on the last pass; the hover check walks only these.
        private static readonly List<Minimap.PinData> Templated = new List<Minimap.PinData>();

        /// The templated pin under the cursor, which shows its full name instead.
        private static Minimap.PinData _hovered;

        public static void Reset()
        {
            _templates = null;
            _hovered = null;
            Templated.Clear();
        }

        /// Runs in the UpdatePins postfix, after Crowding.Measure (which the count comes from) and
        /// before LabelCrowding, so collisions are judged on the text actually drawn.
        public static void Apply(List<Minimap.PinData> pins)
        {
            Templated.Clear();
            Dictionary<string, string> templates = Load();
            if (templates == null || templates.Count == 0)
                return;

            PinRecord.IndexByPosition();
            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameGameObject == null || name.PinNameText == null || Crowding.OutOfSight(pin))
                    continue;

                string key = PinRecord.AutoLabelKey(pin);
                if (key == null || !templates.TryGetValue(key, out string template))
                    continue;

                Templated.Add(pin);
                string full = Labels.Localize(pin.m_name);
                name.PinNameText.text = ReferenceEquals(pin, _hovered)
                    ? full
                    : Render(template, full, Crowding.MergedCount(pin));
            }
        }

        /// Every frame, because UpdatePins does not run on a mouse move - only when
        /// m_pinUpdateRequired is set (Minimap.Update, read from the installed DLL). When the pin
        /// under the cursor changes, one pass is requested, and that pass swaps the text and lets
        /// LabelCrowding reveal the name if a collision had hidden it.
        public static void Hover(Minimap map)
        {
            Minimap.PinData hovered = null;
            if (map.m_mode == Minimap.MapMode.Large && Templated.Count > 0)
            {
                Vector2 cursor = LabelCrowding.CursorIn(Templated[0].m_iconElement);
                float best = LabelCrowding.RevealRadius;
                foreach (Minimap.PinData pin in Templated)
                {
                    if (pin.m_iconElement == null)
                        continue;
                    float distance = Vector2.Distance((Vector2)pin.m_iconElement.rectTransform.localPosition, cursor);
                    if (distance < best)
                    {
                        best = distance;
                        hovered = pin;
                    }
                }
            }

            if (ReferenceEquals(hovered, _hovered))
                return;
            _hovered = hovered;
            MinimapAccess.RequestPinUpdate(map);
        }

        /// {qty} is left out at one, so "B{qty}" reads "B" for a lone bush, as asked.
        private static string Render(string template, string full, int qty) =>
            template.Replace("{name}", full)
                    .Replace("{qty}", qty > 1 ? qty.ToString(CultureInfo.InvariantCulture) : string.Empty)
                    .Trim();

        /// Same "key=text" format as the translation file, for the same reason: it survives
        /// Notepad and a typo costs one line, not the file.
        private static Dictionary<string, string> Load()
        {
            if (_templates != null)
                return _templates;

            string path = Path.Combine(Paths.ConfigPath, FileName);
            if (!File.Exists(path))
            {
                // Not cached until the starter is written: the record that lists the keys may not
                // be loaded on the first pass, and a file written then would list nothing.
                if (!WriteStarter(path))
                    return null;
                _templates = new Dictionary<string, string>();
                return _templates;
            }

            // Case-insensitive, since these keys are typed by hand.
            _templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string text = line.Substring(eq + 1).Trim();
                    if (text.Length > 0)
                        _templates[line.Substring(0, eq).Trim()] = text;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read {path}: {e.Message}. Pin labels stay as they are.");
                return _templates;
            }

            if (_templates.Count > 0)
                Plugin.Log.LogInfo($"Pin label templates: {_templates.Count} loaded from {FileName}.");
            return _templates;
        }

        /// Every line commented out, so writing it changes nothing on the map. Lists the kinds
        /// already pinned in this world, which are real keys rather than ones derived from tables.
        private static bool WriteStarter(string path)
        {
            List<string> keys = PinRecord.Keys();
            if (keys.Count == 0)
                return false;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Cartur's Map Pins - your own labels for automatic pins.");
                sb.AppendLine("# Remove the '#' from a line and write the label after the '='.");
                sb.AppendLine("#   {qty}  how many pins of that kind are merged into the marker (left out when it is one)");
                sb.AppendLine("#   {name} the normal name, in your language");
                sb.AppendLine("# Examples:");
                sb.AppendLine("#   Pickable:Raspberry=R{qty}");
                sb.AppendLine("#   Dungeon:Sunken Crypt=SC");
                sb.AppendLine("#   Ore:Copper={qty} {name}");
                sb.AppendLine("# Pins you renamed by hand are never changed. Hover a pin to see its full name.");
                sb.AppendLine("# Changes apply the next time you load a world.");
                sb.AppendLine("# The keys below are what was pinned in the world this file was written from.");
                sb.AppendLine("# The key of any pin is the first field of its line in the .pins.txt record.");
                sb.AppendLine();
                foreach (string key in keys)
                    sb.AppendLine("#" + key + "=");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Plugin.Log.LogInfo($"Wrote {path} - every line commented out, edit it to shorten pin labels.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write {path}: {e.Message}");
            }
            return true;
        }
    }
}
