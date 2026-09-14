using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    /// Keeps the map readable where labels pile up, and gives every hidden one back under the
    /// cursor.
    ///
    /// At the zoom you get from pressing M, a well-explored map is a wall of overlapping words -
    /// COPPER over CRYPT, TROLL CAVE over CHEST. Two names on top of each other carry less than
    /// either one alone, so the collision is resolved rather than drawn.
    ///
    /// Which one survives is decided by how common the name is on your map. CHEST appears forty
    /// times and says little; CRYPT appears twice and says a lot. The rare name claims the space
    /// and the common one steps aside - and since the icons are untouched, the chest is still
    /// there to see.
    ///
    /// Nothing is ever permanently unreadable: a pin under the cursor always shows its name, so
    /// sweeping the mouse across a cluster reads it out.
    internal static class LabelCrowding
    {
        /// How close the cursor has to be to a pin before its name is forced back on.
        private const float RevealRadius = 60f;

        private static readonly List<Minimap.PinNameData> Hidden = new List<Minimap.PinNameData>();
        private static readonly List<Minimap.PinNameData> Shown = new List<Minimap.PinNameData>();
        private static readonly List<Minimap.PinData> Ordered = new List<Minimap.PinData>();
        private static readonly Dictionary<string, int> NameCounts = new Dictionary<string, int>();

        /// Cached, because sorting with a lambda allocates a comparer every pass otherwise.
        private static readonly Comparison<Minimap.PinData> ByRarity = (a, b) =>
        {
            NameCounts.TryGetValue(a.m_name ?? string.Empty, out int countA);
            NameCounts.TryGetValue(b.m_name ?? string.Empty, out int countB);
            return countA.CompareTo(countB);
        };

        public static void Apply(List<Minimap.PinData> pins)
        {
            // Everything hidden last pass comes back first, so a label hidden at one zoom or
            // cursor position is not stuck hidden at the next.
            foreach (Minimap.PinNameData name in Hidden)
            {
                if (name?.PinNameGameObject != null)
                    name.PinNameGameObject.SetActive(true);
            }
            Hidden.Clear();
            Shown.Clear();
            Ordered.Clear();
            NameCounts.Clear();

            if (!Plugin.HideCollidingLabels.Value)
                return;

            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameRectTransform == null || name.PinNameGameObject == null)
                    continue;
                if (!name.PinNameGameObject.activeInHierarchy || string.IsNullOrEmpty(pin.m_name))
                    continue;

                NameCounts.TryGetValue(pin.m_name, out int count);
                NameCounts[pin.m_name] = count + 1;
                Ordered.Add(pin);
            }

            if (Ordered.Count == 0)
                return;

            // Rarest first, so the name that says the most claims its space before the ones that
            // repeat all over the map get a chance to sit on it.
            Ordered.Sort(ByRarity);

            Vector2 cursor = CursorIn(Ordered[0].m_iconElement);

            foreach (Minimap.PinData pin in Ordered)
            {
                Minimap.PinNameData name = pin.m_NamePinData;
                Rect mine = RectOf(name.PinNameRectTransform);

                // Under the cursor wins outright, including over an earlier label: pointing at a
                // pin is asking what it is.
                bool revealed = pin.m_iconElement != null &&
                                Vector2.Distance(pin.m_iconElement.rectTransform.anchoredPosition, cursor) < RevealRadius;

                if (revealed || !Collides(mine))
                {
                    Shown.Add(name);
                    continue;
                }

                name.PinNameGameObject.SetActive(false);
                Hidden.Add(name);
            }
        }

        private static bool Collides(Rect mine)
        {
            foreach (Minimap.PinNameData other in Shown)
            {
                if (other.PinNameRectTransform != null && RectOf(other.PinNameRectTransform).Overlaps(mine))
                    return true;
            }
            return false;
        }

        /// The pointer, in the same space the labels are positioned in. Off-screen when there is
        /// no canvas to convert against, which simply means nothing is revealed.
        private static Vector2 CursorIn(UnityEngine.UI.Image icon)
        {
            var parent = icon != null ? icon.rectTransform.parent as RectTransform : null;
            if (parent == null)
                return new Vector2(float.MaxValue, float.MaxValue);

            Canvas canvas = parent.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                       parent, ZInput.pointerPosition, camera, out Vector2 local)
                ? local
                : new Vector2(float.MaxValue, float.MaxValue);
        }

        private static Rect RectOf(RectTransform rt)
        {
            Vector2 pos = rt.anchoredPosition;
            Vector2 size = rt.rect.size;
            return new Rect(pos.x - size.x * 0.5f, pos.y - size.y * 0.5f, size.x, size.y);
        }
    }
}
