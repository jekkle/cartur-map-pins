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
            Recentre(map);
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
                    PadAfter(group);
            }
            CentreBar(row);
        }

        /// The entries add up to more than the game's bar is wide once they have their room, and the
        /// overflow ran off the left of the map (owner, 2026-11-02). KeyHints is stretched across the
        /// map and shifted 177.5 left of its centre (read with the pilot's maptree). The room that is
        /// free is the map frame's left edge to the icon picker's left edge; the bar is moved so its
        /// middle is the middle of that, and its layout centres the entries, which it does evenly on
        /// both sides when they overflow. Read from the live rects each time, not a fixed shift:
        /// 2.1.2 used -134, which was right at 1920x1080 only (frame 150..1770, picker from 1503;
        /// totya's report on 2.1.2 named it). No picker, or one that is hidden, leaves the whole frame.
        private static float _centredOn = float.NaN;
        private static RectTransform _hints, _row;
        private static float _spreadAt = -10f;
        private static readonly Vector3[] Corners = new Vector3[4];

        private static void Recentre(Minimap map)
        {
            if (_hints == null || map.m_largeRoot == null)
                return;
            ((RectTransform)map.m_largeRoot.transform).GetWorldCorners(Corners);
            float left = Corners[0].x, right = Corners[2].x;
            RectTransform picker = MapIconPicker.PanelRect;
            if (picker != null && picker.gameObject.activeInHierarchy)
            {
                picker.GetWorldCorners(Corners);
                right = Corners[0].x;
            }
            float target = (left + right) * 0.5f;
            if (Mathf.Approximately(target, _centredOn))
                return;

            _hints.GetWorldCorners(Corners);
            float scale = _hints.lossyScale.x;
            if (scale <= 0f)
                return;
            float move = (target - (Corners[0].x + Corners[2].x) * 0.5f) / scale;
            _hints.offsetMin += new Vector2(move, 0f);
            _hints.offsetMax += new Vector2(move, 0f);
            _centredOn = target;
        }

        /// The room after each entry grows into the width the bar has to spare, up to MaxPairGap (totya,
        /// 2.1.3: "Change Icon" too close to P, "Labels On" too close to H, a lot of unused room on the
        /// right). The words stay as close to their own key as before; only the space between entries
        /// moves, never below PairGap. Twice a second, since the words change width ("All Pins" to
        /// "Mine Only") and the picker opening changes how much room there is.
        private const float MaxPairGap = 56f;

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
            RectTransform picker = MapIconPicker.PanelRect;
            if (picker != null && picker.gameObject.activeInHierarchy)
            {
                picker.GetWorldCorners(Corners);
                right = Corners[0].x;
            }

            var groups = new System.Collections.Generic.List<HorizontalLayoutGroup>();
            foreach (Transform entry in _row)
            {
                HorizontalLayoutGroup group = entry.GetComponent<HorizontalLayoutGroup>();
                if (group != null && entry.gameObject.activeSelf)
                    groups.Add(group);
            }
            if (groups.Count == 0)
                return;

            float room = (right - left) / scale;
            float used = LayoutUtility.GetPreferredWidth(_row);
            float gap = Mathf.Clamp(groups[0].padding.right + (room - used) * 0.9f / groups.Count, PairGap, MaxPairGap);
            int pixels = Mathf.RoundToInt(gap);
            if (groups[0].padding.right == pixels)
                return;
            foreach (HorizontalLayoutGroup group in groups)
                group.padding.right = pixels;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_row);
            _centredOn = float.NaN;   // the bar changed width, so it is centred again
        }

        private static void CentreBar(Transform row)
        {
            _row = row as RectTransform;
            _hints = row.parent as RectTransform;
            _centredOn = float.NaN;
            if (_built != null)
                Recentre(_built);
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
        /// SpritePadding, and PairGap is empty room after the entry, so the next one is clearly apart.
        private const float SpritePadding = 2f;
        private const float PairGap = 18f;
        private const float CapHeight = 30f;   // the mouse icon's visible body

        private static void AddGap(GameObject entry)
        {
            HorizontalLayoutGroup group = entry.GetComponent<HorizontalLayoutGroup>();
            if (group == null)
                return;
            group.spacing += SpritePadding;
            PadAfter(group);
        }

        /// Room after the entry's last item. A floor, not an addition, so it is safe to run twice.
        private static void PadAfter(HorizontalLayoutGroup group)
        {
            if (group.padding.right < PairGap)
                group.padding.right = (int)PairGap;
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
