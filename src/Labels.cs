using System.Text;

namespace CarturMapPins
{
    /// Turns internal prefab names into short, generalised pin labels: "rock4_copper" -> "Copper",
    /// "TrollCave02" -> "Troll Cave", "Crypt2" -> "Crypt".
    ///
    /// Ore names are derived from the item the node drops rather than from its prefab name, which
    /// is both cleaner and already known - the catalog records which drop qualified each prefab
    /// as ore, so "CopperOre" becomes "Copper" with no per-prefab table to maintain.
    internal static class Labels
    {
        /// Suffixes/prefixes that are noise in an item or prefab name.
        private static readonly string[] DropSuffixes = { "ore", "scrap", "new", "old" };

        public static string ForOre(string prefabName)
        {
            if (PinCatalog.OreQualifiedBy.TryGetValue(prefabName, out string via) && !string.IsNullOrEmpty(via))
            {
                // "CopperOre (via MineRock_Copper)" -> "CopperOre"
                int paren = via.IndexOf(" (via ");
                string itemName = paren > 0 ? via.Substring(0, paren) : via;
                string cleaned = StripSuffix(itemName);
                if (!string.IsNullOrEmpty(cleaned))
                    return Prettify(cleaned);
            }

            return Prettify(StripOrePrefabNoise(prefabName));
        }

        public static string ForLocation(string prefabName) => Prettify(prefabName);

        /// Removes a trailing word like "Ore"/"Scrap" so "CopperOre" -> "Copper" and
        /// "IronScrap" -> "Iron", while leaving names like "Obsidian" or "Sulfur" alone.
        private static string StripSuffix(string name)
        {
            string lower = name.ToLowerInvariant();
            foreach (string suffix in DropSuffixes)
            {
                if (lower.Length > suffix.Length && lower.EndsWith(suffix))
                    return name.Substring(0, name.Length - suffix.Length);
            }
            return name;
        }

        /// Deposit prefabs carry mesh-variant noise ("rock4_copper", "MineRock_Copper",
        /// "silvervein") that shouldn't reach the map.
        private static string StripOrePrefabNoise(string name)
        {
            string s = name;
            foreach (string prefix in new[] { "MineRock_", "rock1_", "rock2_", "rock3_", "rock4_" })
            {
                if (s.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(prefix.Length);
                    break;
                }
            }
            return s;
        }

        /// "TrollCave02" -> "Troll Cave", "Mistlands_DvergrTown" -> "Dvergr Town", "Crypt2" -> "Crypt".
        public static string Prettify(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw;

            string s = raw.Replace('_', ' ');

            // Drop biome/era qualifiers that add nothing to a map label.
            foreach (string noise in new[] { "Mistlands ", "Ashlands ", "Hildir ", "Dvergr " })
            {
                if (s.StartsWith(noise, System.StringComparison.OrdinalIgnoreCase) && s.Length > noise.Length)
                    s = s.Substring(noise.Length);
            }

            // Split camelCase into words.
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]) && s[i - 1] != ' ')
                    sb.Append(' ');
                sb.Append(c);
            }
            s = sb.ToString();

            // Trailing variant digits: "Crypt 2", "Troll Cave 02" -> drop the number.
            s = s.TrimEnd();
            int end = s.Length;
            while (end > 0 && (char.IsDigit(s[end - 1]) || s[end - 1] == ' '))
                end--;
            if (end > 0)
                s = s.Substring(0, end);

            // Collapse runs of spaces, then title-case for map readability.
            string[] words = s.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var outSb = new StringBuilder();
            foreach (string w in words)
            {
                if (outSb.Length > 0)
                    outSb.Append(' ');
                outSb.Append(char.ToUpperInvariant(w[0]));
                if (w.Length > 1)
                    outSb.Append(w.Substring(1));
            }

            return outSb.Length > 0 ? outSb.ToString() : raw;
        }
    }
}
