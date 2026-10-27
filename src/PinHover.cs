using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CarturMapPins
{
    /// The pin under the cursor on the large map grows a little and comes to the front.
    ///
    /// Size goes through PinFade, which already eases every pin toward a target size each frame:
    /// the hovered pin just gets a bigger target, so it grows and shrinks back smoothly with no
    /// animation of its own. Bringing it to the front is a sibling move - pins share one parent
    /// and later siblings draw on top - so an enlarged pin is not covered by its neighbours.
    ///
    /// Only a drawn pin counts (PinFade switches the Image off for faded ones), and only when the
    /// cursor is over the icon itself, measured from the icon's own size and current scale.
    internal static class PinHover
    {
        private const float Grow = 1.3f;

        public static Minimap.PinData Current { get; private set; }

        public static float ScaleFor(Minimap.PinData pin) => ReferenceEquals(pin, Current) ? Grow : 1f;

        public static void Find(Minimap map, List<Minimap.PinData> pins)
        {
            Minimap.PinData found = null;
            // Not while the pointer is over our own editor, picker or search bar: the pin behind a
            // panel is not the one being pointed at. Mouse only; the panels' rects are tested
            // against the mouse position, which a gamepad does not move.
            if (map.m_mode == Minimap.MapMode.Large && pins != null &&
                !(ZInput.IsMouseActive() && (PinEditor.PointerOverPanel() || MapIconPicker.PointerOverUi())))
            {
                Vector2 cursor = new Vector2(float.MaxValue, float.MaxValue);
                float best = float.MaxValue;
                foreach (Minimap.PinData pin in pins)
                {
                    UnityEngine.UI.Image icon = pin?.m_iconElement;
                    // Area pins (the raid circle) are sized from a world radius and would answer
                    // anywhere inside the circle.
                    if (icon == null || pin.m_worldSize > 0f || !icon.enabled || !icon.gameObject.activeInHierarchy)
                        continue;
                    if (cursor.x == float.MaxValue)
                        cursor = LabelCrowding.CursorIn(icon);

                    RectTransform rect = icon.rectTransform;
                    float reach = rect.rect.width * 0.5f * rect.localScale.x;
                    float distance = Vector2.Distance((Vector2)rect.localPosition, cursor);
                    if (distance < reach && distance < best)
                    {
                        best = distance;
                        found = pin;
                    }
                }
            }

            if (!ReferenceEquals(found, Current))
            {
                Current = found;
                if (found?.m_iconElement != null)
                    found.m_iconElement.transform.SetAsLastSibling();
                SetTag(found);
            }
            PlaceTag(found);
        }

        // ---- "Auto pin" / "Your pin" above the hovered pin ------------------------------------
        //
        // Asked on Nexus with the delete fixes (2026-10-23): a pin the mod placed and one placed
        // by hand look the same, so there was no telling which one a right-click would cost.
        // Off the right of the cursor, like a tooltip (owner, 2026-10-24), so it never sits on the
        // pin's own name or on a neighbouring pin.

        private static TextMeshProUGUI _tag;

        private static void SetTag(Minimap.PinData pin)
        {
            if (pin?.m_iconElement == null)
            {
                if (_tag != null)
                    _tag.gameObject.SetActive(false);
                return;
            }
            if (_tag == null)
            {
                var go = new GameObject("CarturPins_HoverOrigin", typeof(RectTransform));
                _tag = Fonts.AddLabel(go);
                _tag.alignment = TextAlignmentOptions.Left;
                _tag.textWrappingMode = TextWrappingModes.NoWrap;
                _tag.raycastTarget = false;
                _tag.color = new Color(0.9f, 0.86f, 0.72f, 1f);
                _tag.rectTransform.sizeDelta = new Vector2(160f, 24f);
                _tag.rectTransform.pivot = new Vector2(0f, 0.5f);
            }
            Transform root = pin.m_iconElement.transform.parent;
            if (_tag.transform.parent != root)
                _tag.transform.SetParent(root, false);
            TMP_Text name = pin.m_NamePinData?.PinNameText;
            _tag.fontSize = name != null ? name.fontSize * 0.8f : 14f;
            _tag.text = pin.m_ownerID != 0L ? "Shared pin" : PinRecord.IsOurs(pin) ? "Auto pin" : "Your pin";
            _tag.gameObject.SetActive(true);
            _tag.transform.SetAsLastSibling();
        }

        /// Every frame, following the mouse while it stays on the pin. CursorIn answers in the pin
        /// root's own units, which is where the tag lives.
        private static void PlaceTag(Minimap.PinData pin)
        {
            if (_tag == null || pin?.m_iconElement == null || !_tag.gameObject.activeSelf)
                return;
            Vector2 cursor = LabelCrowding.CursorIn(pin.m_iconElement);
            _tag.rectTransform.localPosition = new Vector3(cursor.x + CursorGap, cursor.y, 0f);
        }

        /// Clear of the pointer arrow, which hangs down and right from the hot spot.
        private const float CursorGap = 20f;
    }
}
