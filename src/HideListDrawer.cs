using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace CarturMapPins
{
    /// Draws the hide list as a text box with a list of what is actually on your map underneath.
    ///
    /// ConfigurationManager's own string drawer is a bare text field, which means typing a pin's
    /// name exactly and from memory - and the names are localized, so half of them are not what
    /// the wiki calls them. Offering the names that are really there, with counts, turns it into
    /// picking from a list. Same mechanism as the icon dropdown next door: a CustomDrawer, drawn
    /// inline rather than floating, because the manager's scroll view moves under an overlay.
    ///
    /// The count is the part that matters. "core" legitimately takes Surtling, Molten and Black
    /// Core at once, and the honest way to allow that is to say so before it happens rather than
    /// to forbid it.
    internal static class HideListDrawer
    {
        /// How tall the scrolling list is, in pixels. Scrolled rather than cut off at a row
        /// count: the first build showed the fourteen commonest names and hid everything else
        /// behind a text box, so finding a name meant already knowing how it is spelled - which
        /// is the one thing the list exists to save you from.
        private const int ListHeight = 260;

        private static readonly HashSet<string> Expanded = new HashSet<string>();
        private static string _search = string.Empty;
        private static Vector2 _scroll;

        /// Suggestions() walks the whole record and the live pin list, and OnGUI runs several
        /// times a frame. Built when the drawer opens or the list text changes, not per event.
        private static List<KeyValuePair<string, int>> _suggestions;
        private static string _suggestionsFor;

        public static void Draw(ConfigEntryBase entry)
        {
            string key = entry.Definition.Section + "/" + entry.Definition.Key;
            string current = entry.BoxedValue as string ?? string.Empty;

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            string edited = GUILayout.TextField(current, GUILayout.MinWidth(200));
            if (edited != current)
                entry.BoxedValue = edited;

            bool open = Expanded.Contains(key);
            if (GUILayout.Button(open ? "Close  ▲" : "Pins…  ▼", GUILayout.Width(90)))
            {
                if (open) Expanded.Remove(key); else Expanded.Add(key);
                _suggestions = null;
            }
            GUILayout.EndHorizontal();

            if (!Expanded.Contains(key))
            {
                GUILayout.EndVertical();
                return;
            }

            if (_suggestions == null || _suggestionsFor != current)
            {
                _suggestions = PinHiding.Suggestions();
                _suggestionsFor = current;
            }
            List<KeyValuePair<string, int>> all = _suggestions;
            if (all.Count == 0)
            {
                GUILayout.Label("No pins on the map yet - open this once you are in a world.");
                GUILayout.EndVertical();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{all.Count} on your map.", GUILayout.Width(110));
            GUILayout.Label("Find:", GUILayout.Width(34));
            _search = GUILayout.TextField(_search ?? string.Empty, GUILayout.MinWidth(100));
            if (GUILayout.Button("Clear find", GUILayout.Width(74)))
                _search = string.Empty;
            if (GUILayout.Button("Show all", GUILayout.Width(70)))
                entry.BoxedValue = string.Empty;
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(ListHeight));

            int shown = 0;
            foreach (KeyValuePair<string, int> option in all)
            {
                if (_search.Length > 0 &&
                    option.Key.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                shown++;
                bool listed = Contains(entry.BoxedValue as string, option.Key);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(listed ? "Show" : "Hide", GUILayout.Width(52)))
                {
                    entry.BoxedValue = listed
                        ? Remove(entry.BoxedValue as string, option.Key)
                        : Add(entry.BoxedValue as string, option.Key);
                }
                GUILayout.Label($"{option.Key}   ({option.Value})");
                GUILayout.EndHorizontal();
            }

            if (shown == 0)
                GUILayout.Label($"Nothing on your map matches \"{_search}\".");

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// Whether this exact name is already one of the terms. Deliberately an exact, whole-term
        /// test and not the matching rule the hide itself uses: the button says whether clicking
        /// it again will take this name back out, and a term like "core" that happens to cover
        /// this pin is not something the Show button could undo.
        private static bool Contains(string raw, string name)
        {
            name = NoCommas(name);
            if (string.IsNullOrEmpty(raw))
                return false;

            foreach (string piece in raw.Split(','))
            {
                if (piece.Trim().Equals(name.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// The list is comma-separated, so a name with a comma in it would be read back as two
        /// terms. Dropped from the name rather than changing the separator, which would break
        /// every existing config. The words still all appear in the pin's identity.
        private static string NoCommas(string name) => name.Replace(",", string.Empty);

        private static string Add(string raw, string name)
        {
            name = NoCommas(name).Trim();
            if (string.IsNullOrEmpty(raw))
                return name;
            return raw.TrimEnd().TrimEnd(',') + ", " + name;
        }

        private static string Remove(string raw, string name)
        {
            name = NoCommas(name);
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            var kept = new List<string>();
            foreach (string piece in raw.Split(','))
            {
                string term = piece.Trim();
                if (term.Length == 0 || term.Equals(name.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    continue;
                kept.Add(term);
            }

            return string.Join(", ", kept.ToArray());
        }
    }
}
