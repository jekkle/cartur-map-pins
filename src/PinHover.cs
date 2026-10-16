using System.Collections.Generic;
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

            if (ReferenceEquals(found, Current))
                return;
            Current = found;
            if (found?.m_iconElement != null)
                found.m_iconElement.transform.SetAsLastSibling();
        }
    }
}
