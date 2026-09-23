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
            // Nothing is put back here, deliberately. UpdatePins writes every label visibility
            // every pass - LargeZoom below m_showNamesZoom for the large map, off for everything
            // else - so a label this pass wants shown is already shown by the time we run.
            //
            // Switching the previously-hidden ones back on was the bug behind labels crowding a
            // zoomed-out map: this runs in a Postfix, so it overrode the decision vanilla had just
            // made, and a label hidden once for a collision came back lit at every zoom, and on
            // the small minimap too. Hiding is ours to do; showing is the game to do.
            Shown.Clear();
            Ordered.Clear();
            NameCounts.Clear();

            if (!Plugin.ShowPinLabels.Value)
            {
                HideAll(pins);
                return;
            }

            // Vanilla has this rule and it is dead on the live component: it hides names while
            // "LargeZoom < m_showNamesZoom", and m_showNamesZoom is 2.0 where the highest zoom the
            // map allows is 1.0 - so the test is true at every zoom and no name is ever hidden by
            // it. Measured, not assumed; the 0.5 in the assembly is only the field initializer.
            // So the mod does it, or nobody does.
            float limit = Plugin.HideLabelsFromZoom.Value;
            if (limit < 100f && Crowding.ZoomOutPercent() > limit)
            {
                HideAll(pins);
                return;
            }

            // Before the early return below: a pin that is not being drawn must not leave its
            // name floating over the map, and that is true whatever the collision setting says.
            HideOutOfSight(pins);

            if (!Plugin.HideCollidingLabels.Value && !PinFilter.Active)
                return;

            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameRectTransform == null || name.PinNameGameObject == null)
                    continue;
                if (!name.PinNameGameObject.activeInHierarchy || string.IsNullOrEmpty(pin.m_name))
                    continue;

                // A name that does not match the search is noise while searching, and the pin it
                // belongs to is still on the map, dimmed.
                if (!PinFilter.Matches(pin.m_name))
                {
                    name.PinNameGameObject.SetActive(false);
                    continue;
                }

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
            }
        }

        private static void HideOutOfSight(List<Minimap.PinData> pins)
        {
            foreach (Minimap.PinData pin in pins)
            {
                if (!Crowding.OutOfSight(pin))
                    continue;

                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameGameObject == null || !name.PinNameGameObject.activeInHierarchy)
                    continue;

                name.PinNameGameObject.SetActive(false);
            }
        }

        /// Every label off, bar the one under the cursor. The exception is not a compromise on
        /// the setting: a map of unlabelled icons cannot be read at all without some way to ask
        /// what one of them is, and this is the same reveal the collision hiding already uses.
        private static void HideAll(List<Minimap.PinData> pins)
        {
            Vector2 cursor = CursorNear(pins);

            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameGameObject == null || !name.PinNameGameObject.activeInHierarchy)
                    continue;

                if (!Crowding.OutOfSight(pin) && pin.m_iconElement != null &&
                    Vector2.Distance(pin.m_iconElement.rectTransform.anchoredPosition, cursor) < RevealRadius)
                    continue;

                name.PinNameGameObject.SetActive(false);
            }
        }

        /// The cursor needs any one pin's icon to convert against, since they all share a parent.
        /// A pin list with no drawn icons in it means the map is not showing anything, and the
        /// off-screen answer hides everything, which is what was asked for anyway.
        private static Vector2 CursorNear(List<Minimap.PinData> pins)
        {
            foreach (Minimap.PinData pin in pins)
            {
                if (pin?.m_iconElement != null)
                    return CursorIn(pin.m_iconElement);
            }
            return new Vector2(float.MaxValue, float.MaxValue);
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
