using System.Collections.Generic;

namespace CarturMapPins
{
    /// Tracks the wild loot containers we've seen, so the looted-chest sweep doesn't have to
    /// enumerate every Container in the scene.
    ///
    /// FindObjectsByType&lt;Container&gt; returns the player's entire base as well - every chest,
    /// barrel and cart - which is a lot of objects to walk just to check a handful of world
    /// chests. The spawn hook already sees each container exactly once as its zone loads, and
    /// it has already filtered out player-built ones, so collecting them there is both cheaper
    /// and more precise. Mirrors how the game itself keeps Character.s_characters.
    internal static class ChestRegistry
    {
        private static readonly List<Container> Live = new List<Container>();

        public static int Count => Live.Count;

        public static void Add(Container container)
        {
            if (container == null || Live.Contains(container))
                return;
            Live.Add(container);
        }

        public static void Clear() => Live.Clear();

        /// Live containers, with unloaded ones pruned as we go. Unity nulls a destroyed
        /// component's reference, so a simple null check is enough to drop anything the game has
        /// unloaded since we saw it.
        public static IEnumerable<Container> Alive()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (Live[i] == null)
                {
                    Live.RemoveAt(i);
                    continue;
                }
                yield return Live[i];
            }
        }
    }
}
