using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    /// Shrinks pins that are sitting on top of each other, so a cluster reads as several things
    /// rather than one blob with a fringe.
    ///
    /// Measured in screen pixels, not world metres: UpdatePins has already placed every icon by
    /// the time this runs, so crowding can be read off where the icons actually are. That makes it
    /// zoom-proof for nothing - the same two deposits are crowded when zoomed out and not when
    /// zoomed in, which is exactly what the eye sees.
    ///
    /// Never below half size. Past that a pin stops being readable, and a cluster of unreadable
    /// pins is worse than an overlapping one.
    internal static class Crowding
    {
        /// Fallback only: the cell is normally the icon's own width, read off the first pin seen.
        /// A hardcoded 34 was a guess at pin size that any UI scale would make wrong.
        private const float FallbackCell = 34f;

        private const float Floor = 0.5f;

        private static float _cell = FallbackCell;

        /// Reused rather than allocated per pass: this runs on every pin update, and a dictionary
        /// a frame is the kind of thing that shows up as stutter half an hour later.
        private static readonly Dictionary<long, int> Counts = new Dictionary<long, int>();

        private static long KeyFor(Minimap.PinData pin)
        {
            Vector2 pos = pin.m_iconElement.rectTransform.anchoredPosition;
            return ((long)Mathf.RoundToInt(pos.x / _cell) << 32) ^ (uint)Mathf.RoundToInt(pos.y / _cell);
        }

        /// Counts in steps rather than one for one.
        ///
        /// A pin drifting over a cell boundary flips its neighbour's count between two and three,
        /// and a size that answers every such flip is the shrinking and growing you can see on the
        /// map. Steps mean the common wobble changes nothing: it takes a real change in how
        /// crowded a spot is to move a pin to the next size down.
        private static float ScaleForCount(int count)
        {
            if (count <= 1)
                return 1f;
            if (count == 2)
                return 0.9f;   // a pair is the common case: separate them without shrinking much
            if (count <= 4)
                return 0.8f;
            if (count <= 6)
                return 0.65f;
            return Floor;
        }

        public static void Measure(List<Minimap.PinData> pins)
        {
            Counts.Clear();
            if (!Plugin.ShrinkCrowdedPins.Value)
                return;

            foreach (Minimap.PinData pin in pins)
            {
                // Inactive icons are the ones the map has scrolled off screen; counting them would
                // shrink a pin because of neighbours nobody can see.
                if (pin?.m_iconElement == null || !pin.m_iconElement.gameObject.activeInHierarchy)
                    continue;

                // The icon's own width, once: it is what "on top of each other" is measured in,
                // and it follows the player's UI scale without being told about it.
                float width = pin.m_iconElement.rectTransform.rect.width;
                if (width > 1f)
                    _cell = width;

                long key = KeyFor(pin);
                Counts.TryGetValue(key, out int count);
                Counts[key] = count + 1;
            }
        }

        /// How much to shrink this pin: 1 when it has its cell to itself, less as the cell fills,
        /// never past half.
        ///
        /// Stepped, so the usual wobble in a count changes nothing.
        public static float ScaleFor(Minimap.PinData pin)
        {
            if (Counts.Count == 0 || pin?.m_iconElement == null)
                return 1f;

            if (!Counts.TryGetValue(KeyFor(pin), out int count))
                return 1f;

            return ScaleForCount(count);
        }
    }
}
