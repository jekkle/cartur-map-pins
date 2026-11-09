using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// Four entries on the large map's bottom hint bar, in the game's own style: the pin cycle (H),
    /// the names switch (Shift+H), Setup (P) and "[Shift] + click, Change Icon" (owner, 2026-10-27).
    ///
    /// The bar is Minimap.m_largeRoot/KeyHints/keyboard_hints, a HorizontalLayoutGroup whose entries
    /// are each a label and a 32 px key image (read with the pilot's maptree). The entries are
    /// cloned from the game's own Ping and Add pin ones, so spacing, font and fitting are the
    /// game's. The key image becomes a drawn key cap, since there is no sprite for a letter.
    ///
    /// The H and Shift+H entries name what the map is showing NOW (owner, 2026-10-30; the H one used
    /// to name the next press, which read as the current state to totya).
    internal static class MapHints
    {
        private static GameObject _hide, _labels, _setup, _change;
        private static TextMeshProUGUI _hideLabel, _hideKey, _labelsLabel, _labelsKey, _setupLabel, _setupKey;
        private static KeyboardShortcut _labelsShown, _setupShown;
        private static Minimap _built;

        public static void Tick(Minimap map)
        {
            if (map == null || map.m_mode != Minimap.MapMode.Large || map.m_largeRoot == null)
                return;
            if (_built != map || _hide == null)
            {
                _built = map;
                Build(map);
            }
            if (_hideLabel == null)
                return;
            Spread(map);
            string text = CurrentState();
            if (_hideLabel.text != text)
                _hideLabel.text = text;
            string key = KeyName(Plugin.HidePinsKey.Value.MainKey);
            if (_hideKey != null && _hideKey.text != key)
                _hideKey.text = key;

            string names = PinHiding.LabelsHidden ? "Labels Off" : "Labels On";
            if (_labelsLabel != null && _labelsLabel.text != names)
                _labelsLabel.text = names;
            // The key caps follow a rebinding made while playing; the text is only built when it changed.
            if (_labelsKey != null && !_labelsShown.Equals(Plugin.HideLabelsKey.Value))
                SetCap(_labelsKey, KeyName(_labelsShown = Plugin.HideLabelsKey.Value));
            if (_setupKey != null && !_setupShown.Equals(Plugin.SetupKey.Value))
                SetCap(_setupKey, KeyName(_setupShown = Plugin.SetupKey.Value));
        }

        private static string CurrentState()
        {
            switch (PinHiding.Cycle)
            {
                case PinHiding.Shown.Yours: return "Mine Only";
                case PinHiding.Shown.Auto: return "Auto Only";
                case PinHiding.Shown.None: return "None";
                default: return "All Pins";
            }
        }

        private static string KeyName(KeyCode key)
        {
            string name = key == KeyCode.None ? "-" : key.ToString().Replace("Alpha", "");
            return name.EndsWith("Shift") ? "Shift" : name.EndsWith("Control") ? "Ctrl" : name.EndsWith("Alt") ? "Alt" : name;
        }

        /// "Shift+H": modifiers first, the way the config file writes them.
        private static string KeyName(KeyboardShortcut shortcut) =>
            string.Join("+", shortcut.Modifiers.Select(KeyName).Concat(new[] { KeyName(shortcut.MainKey) }));

        /// A key cap as wide as its words, 30 at least (the width of a single letter).
        private static void SetCap(TextMeshProUGUI key, string text)
        {
            key.text = text;
            var layout = key.transform.parent.GetComponent<LayoutElement>();
            layout.preferredWidth = layout.minWidth = Mathf.Max(30f, key.GetPreferredValues(text).x + 14f);
        }

        /// One entry cloned from the game's own Ping one: its label, then a drawn key cap.
        private static GameObject Entry(Transform row, Transform template, string name, string text, string cap,
                                        out TextMeshProUGUI label, out TextMeshProUGUI key)
        {
            GameObject entry = Object.Instantiate(template.gameObject, row);
            entry.name = name;
            label = entry.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            label.text = text;
            AddGap(entry);
            key = KeyCap(entry.transform.Find("keyboard_hint").GetComponent<Image>(), cap, 30f, true);
            SetCap(key, cap);
            return entry;
        }

        private static void Build(Minimap map)
        {
            Transform row = map.m_largeRoot.transform.Find("KeyHints/keyboard_hints");
            Transform ping = row != null ? row.Find("PingPanel") : null;
            Transform add = row != null ? row.Find("AddPin") : null;
            if (row == null || ping == null || add == null)
            {
                Plugin.Log.LogWarning("Map hint bar not found - the Hide Pins, Labels, Setup and Change Icon hints are off.");
                return;
            }

            if (_hide != null) Object.Destroy(_hide);
            if (_labels != null) Object.Destroy(_labels);
            if (_setup != null) Object.Destroy(_setup);
            if (_change != null) Object.Destroy(_change);

            // Hide Pins, then the names switch beside it, then Setup: label, then the key.
            _hide = Entry(row, ping, "CarturHidePins", CurrentState(), KeyName(Plugin.HidePinsKey.Value.MainKey), out _hideLabel, out _hideKey);
            _labelsShown = Plugin.HideLabelsKey.Value;
            _labels = Entry(row, ping, "CarturHideLabels", PinHiding.LabelsHidden ? "Labels Off" : "Labels On", KeyName(_labelsShown), out _labelsLabel, out _labelsKey);
            _setupShown = Plugin.SetupKey.Value;
            _setup = Entry(row, ping, "CarturSetup", "Setup", KeyName(_setupShown), out _setupLabel, out _setupKey);

            // Change Icon: label, then the Shift key with the mouse button laid on it, just right of
            // the "t" (owner, 2026-10-27), so it reads as one gesture: shift + click.
            _change = Object.Instantiate(add.gameObject, row);
            _change.name = "CarturChangeIcon";
            _change.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = "Change Icon";
            AddGap(_change);
            Image mouse = _change.transform.Find("keyboard_hint").GetComponent<Image>();

            var wrap = new GameObject("ShiftClick", typeof(RectTransform), typeof(LayoutElement));
            wrap.transform.SetParent(_change.transform, false);
            wrap.transform.SetSiblingIndex(mouse.transform.GetSiblingIndex());
            var cap = new GameObject("Cap", typeof(RectTransform), typeof(Image));
            cap.transform.SetParent(wrap.transform, false);
            var crt = (RectTransform)cap.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0f, 0.5f);
            TextMeshProUGUI shift = KeyCap(cap.GetComponent<Image>(), "Shift", 0f, false);
            // Left-aligned, so the key's wider body leaves room on the right for the mouse.
            shift.alignment = TextAlignmentOptions.MidlineLeft;
            shift.rectTransform.offsetMin = new Vector2(7f, 0f);
            float textWidth = shift.GetPreferredValues("Shift").x;
            const float MouseSize = 30f;
            // The key runs under the whole mouse icon, so the icon sits ON it.
            float capWidth = 7f + textWidth + 2f + MouseSize + 4f;
            crt.sizeDelta = new Vector2(capWidth, CapHeight);
            crt.anchoredPosition = Vector2.zero;

            mouse.transform.SetParent(wrap.transform, false);
            var mrt = (RectTransform)mouse.transform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 0.5f);
            mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.sizeDelta = new Vector2(MouseSize, MouseSize);
            mrt.anchoredPosition = new Vector2(7f + textWidth + 2f + MouseSize * 0.5f, 0f);
            mouse.transform.SetAsLastSibling();   // on top of the key
            mouse.raycastTarget = false;
            var layout = wrap.GetComponent<LayoutElement>();
            layout.preferredWidth = capWidth;
            layout.preferredHeight = 32f;

            // The game's own entries run together the same way (Ping's mouse sat as close to "All
            // Pins" as to its own label), so every entry on the bar gets its own space.
            foreach (Transform entry in row)
            {
                HorizontalLayoutGroup group = entry.GetComponent<HorizontalLayoutGroup>();
                if (group != null)
                    group.padding.right = 0;   // the room between entries is the row's spacing now (Spread)
            }
            CentreBar(row);
        }

        /// Layout of the whole bar, vanilla entries and ours together (totya, 2.1.3 to 2.2.0, and measured
        /// 2026-11-05: at 1280x720, 1920x1080 and 2560x1440 the game's row is 1225 units wide and the eight
        /// entries need more, so the layout squeezed them: "Megjelolés" ran into the next entry and the gaps
        /// were uneven, while a 2560x1080 screen, whose row is 1865 wide, was fine).
        ///
        /// So the bar is no longer left at the width the game gave it. KeyHints is stretched to the free room,
        /// the map frame's left edge to the icon picker's left edge when the picker is on the bar's band (the
        /// whole frame otherwise) less a margin, and the room is shared out evenly: every entry keeps its own label and key together at the
        /// game's own spacing, and the same gap goes between entries, never under MinGap and never over MaxGap
        /// (the rest becomes an equal margin each side, since the row centres). If even MinGap does not fit, the
        /// words shrink, to a floor of MinFont of their size, rather than overlap. Read from the live rects
        /// every half second, since the words change width ("All Pins" to "Mine Only"), the picker opens and
        /// closes, and the window changes size.
        private static RectTransform _hints, _row;
        private static float _spreadAt = -10f;
        private static readonly Vector3[] Corners = new Vector3[4];
        private static readonly System.Collections.Generic.Dictionary<TextMeshProUGUI, float> BaseFont =
            new System.Collections.Generic.Dictionary<TextMeshProUGUI, float>();

        private const float EdgeMargin = 14f;
        private const float MinGap = 26f;
        private const float MaxGap = 60f;
        private const float MinFont = 0.7f;

        private static void Spread(Minimap map)
        {
            if (_row == null || _hints == null || map.m_largeRoot == null || Time.unscaledTime - _spreadAt < 0.5f)
                return;
            _spreadAt = Time.unscaledTime;
            float scale = _hints.lossyScale.x;
            if (scale <= 0f)
                return;

            ((RectTransform)map.m_largeRoot.transform).GetWorldCorners(Corners);
            float left = Corners[0].x, right = Corners[2].x;
            // The bar stops at the picker only where the picker is actually on the bar's band of the screen. A
            // picker moved up out of it takes nothing from the bar (totya, 2.4.0: the bar's width followed
            // the picker's left edge wherever it was put, so it was narrow on his screen and fine on mine).
            RectTransform picker = MapIconPicker.PanelRect;
            if (picker != null && picker.gameObject.activeInHierarchy)
            {
                _hints.GetWorldCorners(Corners);
                float barBottom = Corners[0].y, barTop = Corners[2].y;
                picker.GetWorldCorners(Corners);
                if (Corners[0].y < barTop && Corners[2].y > barBottom)
                    right = Corners[0].x;
            }
            left += EdgeMargin * scale;
            right -= EdgeMargin * scale;

            // KeyHints onto the free room. Moves each edge by what it is off by, in the parent's units.
            _hints.GetWorldCorners(Corners);
            float dl = (left - Corners[0].x) / scale, dr = (right - Corners[2].x) / scale;
            if (Mathf.Abs(dl) > 0.5f || Mathf.Abs(dr) > 0.5f)
            {
                _hints.offsetMin += new Vector2(dl, 0f);
                _hints.offsetMax += new Vector2(dr, 0f);
            }
            float room = (right - left) / scale;

            var entries = new System.Collections.Generic.List<RectTransform>();
            foreach (Transform entry in _row)
            {
                if (entry.gameObject.activeSelf && entry.GetComponent<HorizontalLayoutGroup>() != null)
                    entries.Add((RectTransform)entry);
            }
            if (entries.Count < 2)
                return;

            HorizontalLayoutGroup bar = _row.GetComponent<HorizontalLayoutGroup>();
            if (bar == null)
                return;

            // Words at full size first, so the measurement does not depend on the last pass.
            SetFont(entries, 1f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_row);
            float words = 0f, used = 0f;
            foreach (RectTransform entry in entries)
            {
                used += LayoutUtility.GetPreferredWidth(entry);
                TextMeshProUGUI label = LabelOf(entry);
                if (label != null)
                    words += label.preferredWidth;
            }

            float fit = (room - MinGap * (entries.Count - 1) - (used - words)) / Mathf.Max(1f, words);
            float font = Mathf.Clamp(fit, MinFont, 1f);
            if (font < 1f)
            {
                SetFont(entries, font);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_row);
                used = 0f;
                foreach (RectTransform entry in entries)
                    used += LayoutUtility.GetPreferredWidth(entry);
            }

            float gap = Mathf.Clamp((room - used) / (entries.Count - 1), MinGap, MaxGap);
            if (!Mathf.Approximately(bar.spacing, gap))
            {
                bar.spacing = gap;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_row);
            }
        }

        private static TextMeshProUGUI LabelOf(Transform entry) => entry.Find("Label")?.GetComponent<TextMeshProUGUI>();

        private static void SetFont(System.Collections.Generic.List<RectTransform> entries, float factor)
        {
            foreach (RectTransform entry in entries)
            {
                TextMeshProUGUI label = LabelOf(entry);
                if (label == null)
                    continue;
                if (!BaseFont.TryGetValue(label, out float size))
                    BaseFont[label] = size = label.fontSize;
                if (!Mathf.Approximately(label.fontSize, size * factor))
                    label.fontSize = size * factor;
            }
        }

        private static void CentreBar(Transform row)
        {
            _row = row as RectTransform;
            _hints = row.parent as RectTransform;
            HorizontalLayoutGroup bar = row.GetComponent<HorizontalLayoutGroup>();
            if (bar == null)
                return;
            TextAnchor a = bar.childAlignment;
            bar.childAlignment = a <= TextAnchor.UpperRight ? TextAnchor.UpperCenter
                               : a <= TextAnchor.MiddleRight ? TextAnchor.MiddleCenter : TextAnchor.LowerCenter;
        }

        /// A key sits close to its own words and far from the next entry's (totya, 2026-11-02: with
        /// an even gap either side, H read as belonging to "Labels Off" and Shift+H to "Setup", and
        /// he thought the keys were broken). The label-to-key gap is the layout's spacing plus
        /// SpritePadding, and the gap between entries is Spread's, never under MinGap, so the next one is clearly apart.
        private const float SpritePadding = 2f;
        private const float CapHeight = 30f;   // the mouse icon's visible body

        private static void AddGap(GameObject entry)
        {
            HorizontalLayoutGroup group = entry.GetComponent<HorizontalLayoutGroup>();
            if (group == null)
                return;
            group.spacing += SpritePadding;
        }

        /// Turns a key image into a drawn cap: dark face, the picker's frame, the key name.
        private static TextMeshProUGUI KeyCap(Image face, string text, float width, bool inLayout)
        {
            face.sprite = null;
            face.color = new Color(0.10f, 0.09f, 0.08f, 0.95f);
            if (inLayout)
            {
                var layout = face.GetComponent<LayoutElement>() ?? face.gameObject.AddComponent<LayoutElement>();
                layout.preferredWidth = width;
                layout.preferredHeight = CapHeight;
                layout.minWidth = width;
                layout.minHeight = CapHeight;
            }

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(face.transform, false);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            Image border = frame.GetComponent<Image>();
            border.sprite = IconGrid.Border;
            border.type = Image.Type.Sliced;
            border.color = Color.white;   // the game's own hint white
            border.raycastTarget = false;

            var letter = new GameObject("Key", typeof(RectTransform));
            letter.transform.SetParent(face.transform, false);
            var lrt = (RectTransform)letter.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            TextMeshProUGUI label = Fonts.AddLabel(letter);
            label.text = text;
            label.fontSize = 18f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }
    }
}
