using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    /// Hides a pin's name when an identical name is already drawn on top of it.
    ///
    /// Two tin deposits a few metres apart print "TIN TIN", and a crypt beside a copper deposit
    /// prints one word over the other. The second "TIN" says nothing the first did not, so it goes;
    /// the crypt and the copper both say something, so both stay even though they collide.
    ///
    /// Only when the labels actually overlap. Two pins in the same neighbourhood whose names sit
    /// clear of each other are not a problem, and hiding one of those would lose information for
    /// nothing.
    internal static class LabelCrowding
    {
        /// Kept between passes so a label hidden here can be shown again when the map moves.
        private static readonly List<Minimap.PinNameData> Hidden = new List<Minimap.PinNameData>();

        private static readonly List<Minimap.PinNameData> Shown = new List<Minimap.PinNameData>();

        public static void Apply(List<Minimap.PinData> pins)
        {
            foreach (Minimap.PinNameData name in Hidden)
            {
                if (name?.PinNameGameObject != null)
                    name.PinNameGameObject.SetActive(true);
            }
            Hidden.Clear();
            Shown.Clear();

            if (!Plugin.HideDuplicateLabels.Value)
                return;

            foreach (Minimap.PinData pin in pins)
            {
                Minimap.PinNameData name = pin?.m_NamePinData;
                if (name?.PinNameRectTransform == null || name.PinNameGameObject == null)
                    continue;
                if (!name.PinNameGameObject.activeInHierarchy || string.IsNullOrEmpty(pin.m_name))
                    continue;

                if (!Overlaps(pin, name))
                {
                    Shown.Add(name);
                    continue;
                }

                name.PinNameGameObject.SetActive(false);
                Hidden.Add(name);
            }
        }

        /// True when an already-drawn label says the same thing and sits on top of this one.
        ///
        /// Rectangle against rectangle, both read off the labels themselves, so it answers the
        /// question the eye is asking - do these two collide right now, at this zoom - rather than
        /// guessing from how far apart the pins are in the world.
        private static bool Overlaps(Minimap.PinData pin, Minimap.PinNameData name)
        {
            Rect mine = RectOf(name.PinNameRectTransform);

            foreach (Minimap.PinNameData other in Shown)
            {
                if (other.ParentPin == null || other.ParentPin.m_name != pin.m_name)
                    continue;
                if (RectOf(other.PinNameRectTransform).Overlaps(mine))
                    return true;
            }

            return false;
        }

        private static Rect RectOf(RectTransform rt)
        {
            Vector2 pos = rt.anchoredPosition;
            Vector2 size = rt.rect.size;
            return new Rect(pos.x - size.x * 0.5f, pos.y - size.y * 0.5f, size.x, size.y);
        }
    }
}
