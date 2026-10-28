using UnityEngine;

namespace CarturMapPins
{
    /// Zoomed far out, the pins worth keeping are the ones worth crossing a map for (Release 9,
    /// "priority LOD"), so the cheap-to-find things drop out first and the rest stays: bosses,
    /// dungeons, camps, portals, traders and home always; ore, chests and stones until the map is
    /// three quarters out; landmarks, props, nests and hives until just over half; pickables
    /// until a third. Only pins this mod placed are ever dropped (your own pins, shared pins,
    /// markers and pings are never touched), they fade like any hidden pin, and each step has a
    /// margin so a zoom resting on a line does not flip them. Behind General / PriorityZoomOut,
    /// off by default.
    internal static class PriorityZoom
    {
        private const float Margin = 3f;
        // percent zoomed out beyond which each tier is dropped; tier 0 is never dropped
        private static readonly float[] DropBeyond = { float.MaxValue, 75f, 55f, 35f };
        private static readonly bool[] Dropped = new bool[4];

        public static bool Active => Plugin.PriorityZoomOut != null && Plugin.PriorityZoomOut.Value;

        /// Once per pass, before pins are asked.
        public static void Update(float zoomOutPercent)
        {
            for (int t = 1; t < Dropped.Length; t++)
                Dropped[t] = Active && (Dropped[t] ? zoomOutPercent > DropBeyond[t] - Margin : zoomOutPercent > DropBeyond[t] + Margin);
        }

        public static bool Hides(Minimap.PinData pin)
        {
            if (!Active || pin.m_ownerID != 0L)
                return false;
            string key = PinRecord.KeyOfIndexed(pin);
            if (key == null)
                return false;
            int colon = key.IndexOf(':');
            if (colon <= 0 || !System.Enum.TryParse(key.Substring(0, colon), out PinCategory category))
                return false;
            return Dropped[TierOf(category)];
        }

        private static int TierOf(PinCategory category)
        {
            switch (category)
            {
                case PinCategory.Ore:
                case PinCategory.Chest:
                case PinCategory.Runestone:
                case PinCategory.LoreStone:
                case PinCategory.Leviathan:
                case PinCategory.Wisp:
                    return 1;
                case PinCategory.Landmark:
                case PinCategory.Prop:
                case PinCategory.Spawner:
                case PinCategory.Beehive:
                    return 2;
                case PinCategory.Pickable:
                    return 3;
                default:
                    return 0;   // bosses, minibosses, dungeons, camps, traders, home, portals
            }
        }
    }
}
