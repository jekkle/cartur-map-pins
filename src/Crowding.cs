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
        /// A cell roughly one pin wide. Pins in the same cell are on top of each other.
        private const float CellSize = 34f;

        private const float Floor = 0.5f;

        /// Reused rather than allocated per pass: this runs on every pin update, and a dictionary
        /// a frame is the kind of thing that shows up as stutter half an hour later.
        private static readonly Dictionary<long, int> Counts = new Dictionary<long, int>();

        private static long KeyFor(Minimap.PinData pin)
        {
            Vector2 pos = pin.m_iconElement.rectTransform.anchoredPosition;
            return ((long)Mathf.RoundToInt(pos.x / CellSize) << 32) ^ (uint)Mathf.RoundToInt(pos.y / CellSize);
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

                long key = KeyFor(pin);
                Counts.TryGetValue(key, out int count);
                Counts[key] = count + 1;
            }
        }

        /// How much to shrink this pin: 1 when it has its cell to itself, less as the cell fills,
        /// never past half.
        ///
        /// One over the square root of the count, so two pins barely shrink and it takes four to
        /// reach the floor - crowding should be corrected, not punished.
        public static float ScaleFor(Minimap.PinData pin)
        {
            if (Counts.Count == 0 || pin?.m_iconElement == null)
                return 1f;

            if (!Counts.TryGetValue(KeyFor(pin), out int count) || count < 2)
                return 1f;

            return Mathf.Max(Floor, 1f / Mathf.Sqrt(count));
        }
    }
}
