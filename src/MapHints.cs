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
            _hideKey = KeyCap(_hide.transform.Find("keyboard_hint").GetComponent<Image>(), KeyName(Plugin.HidePinsKey.Value.MainKey), 32f);

            // Change Icon: label, the Shift key, then the mouse button the game uses for Add pin.
            _change = Object.Instantiate(add.gameObject, row);
            _change.name = "CarturChangeIcon";
            _change.transform.Find("Label").GetComponent<TextMeshProUGUI>().text = "Change Icon";
            Image mouse = _change.transform.Find("keyboard_hint").GetComponent<Image>();
            GameObject shift = Object.Instantiate(mouse.gameObject, _change.transform);
            shift.name = "keyboard_hint_shift";
            shift.transform.SetSiblingIndex(mouse.transform.GetSiblingIndex());   // before the mouse
            KeyCap(shift.GetComponent<Image>(), "Shift", 58f);
        }

        /// Turns a key image into a drawn cap: dark face, the picker's frame, the key name.
        private static TextMeshProUGUI KeyCap(Image face, string text, float width)
        {
            face.sprite = null;
            face.color = new Color(0.10f, 0.09f, 0.08f, 0.95f);
            var layout = face.GetComponent<LayoutElement>() ?? face.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 26f;
            layout.minWidth = width;
            layout.minHeight = 26f;

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frame.transform.SetParent(face.transform, false);
            var frt = (RectTransform)frame.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            Image border = frame.GetComponent<Image>();
            border.sprite = IconGrid.Border;
            border.type = Image.Type.Sliced;
            border.color = IconGrid.Bronze;
            border.raycastTarget = false;

            var letter = new GameObject("Key", typeof(RectTransform));
            letter.transform.SetParent(face.transform, false);
            var lrt = (RectTransform)letter.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            TextMeshProUGUI label = Fonts.AddLabel(letter);
            label.text = text;
            label.fontSize = 16f;
            label.color = new Color(0.95f, 0.92f, 0.82f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }
    }
}
