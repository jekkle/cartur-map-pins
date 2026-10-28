using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CarturMapPins
{
    /// Two entries on the large map's bottom hint bar, in the game's own style: "[H] Hide Pins" and
    /// "[Shift] + click, Change Icon" (owner, 2026-10-27).
    ///
    /// The bar is Minimap.m_largeRoot/KeyHints/keyboard_hints, a HorizontalLayoutGroup whose entries
    /// are each a label and a 32 px key image (read with the pilot's maptree). The entries are
    /// cloned from the game's own Ping and Add pin ones, so spacing, font and fitting are the
    /// game's. The key image becomes a drawn key cap, since there is no sprite for a letter.
    ///
    /// The H entry names what the NEXT press does, so it is always true: all pins showing, "Hide
    /// Pins"; all hidden, "My Pins Only"; only my pins showing, "Show All Pins".
    internal static class MapHints
    {
        private static GameObject _hide, _change;
        private static TextMeshProUGUI _hideLabel, _hideKey;
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
            string text = NextAction();
            if (_hideLabel.text != text)
                _hideLabel.text = text;
            string key = KeyName(Plugin.HidePinsKey.Value.MainKey);
            if (_hideKey != null && _hideKey.text != key)
                _hideKey.text = key;
        }

        private static string NextAction()
        {
            switch (PinHiding.Cycle)
            {
                case PinHiding.Shown.None: return "My Pins Only";
                case PinHiding.Shown.Yours: return "Show All Pins";
                default: return "Hide Pins";
            }
        }

        private static string KeyName(KeyCode key) => key == KeyCode.None ? "-" : key.ToString().Replace("Alpha", "");

        private static void Build(Minimap map)
        {
            Transform row = map.m_largeRoot.transform.Find("KeyHints/keyboard_hints");
            Transform ping = row != null ? row.Find("PingPanel") : null;
            Transform add = row != null ? row.Find("AddPin") : null;
            if (row == null || ping == null || add == null)
            {
                Plugin.Log.LogWarning("Map hint bar not found - the Hide Pins and Change Icon hints are off.");
                return;
            }

            if (_hide != null) Object.Destroy(_hide);
            if (_change != null) Object.Destroy(_change);

            // Hide Pins: label, then the key.
            _hide = Object.Instantiate(ping.gameObject, row);
            _hide.name = "CarturHidePins";
            _hideLabel = _hide.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            _hideLabel.text = NextAction();
            AddGap(_hide);
            _hideKey = KeyCap(_hide.transform.Find("keyboard_hint").GetComponent<Image>(), KeyName(Plugin.HidePinsKey.Value.MainKey), 30f, true);

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
        }

        /// The game's mouse icons carry empty sprite on each side, so the text-to-icon gap they
        /// show is their layout spacing (5) plus that. Measured from the owner's screenshot
        /// (2026-10-27): the game's entries show about 27 screen px between text and icon, ours
        /// showed 8 with the same 5 of spacing, i.e. a scale of 1.6, so the padding is about 12.
        /// A drawn cap has no such margin, so it is added to the entry's spacing and the gaps match.
        /// Re-measured on a screenshot after the first try (pixels on the hint bar): Add pin 31,
        /// Cross off 29, Remove 31, Ping 31, ours 27, so 12 became 14.5. The icons are as empty
        /// on their right as on their left (entry-to-entry gap 48-50 against our 29), so the same
        /// amount is added after the key.
        private const float SpritePadding = 14.5f;
        private const float CapHeight = 30f;   // the mouse icon's visible body

        private static void AddGap(GameObject entry)
        {
            HorizontalLayoutGroup group = entry.GetComponent<HorizontalLayoutGroup>();
            if (group == null)
                return;
            group.spacing += SpritePadding;
            group.padding = new RectOffset(group.padding.left, group.padding.right + Mathf.RoundToInt(SpritePadding), group.padding.top, group.padding.bottom);
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
