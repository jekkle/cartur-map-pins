using System;
using System.Collections.Generic;
using System.Text;

namespace CarturMapPins
{
    /// Turns internal prefab names into short pin labels: "rock4_copper" -> "Copper",
    /// "TrollCave02" -> "Troll Cave", "Crypt2" -> "Crypt".
    ///
    /// Ore names come from the item the node drops, not the prefab name. The catalog records which
    /// drop qualified each prefab as ore, so "CopperOre" becomes "Copper" with no per-prefab table.
    internal static class Labels
    {
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

        /// Resolves the "$token" strings that component m_name fields hold.
        ///
        /// The game also localizes a pin name on its way to the map -
        /// PinNameData.SetTextAndGameObject writes "PinNameText.text =
        /// Localization.instance.Localize(ParentPin.m_name)" (verified against the installed DLL).
        /// It is still resolved here because m_name is what the rename box, the search bar and the
        /// pin list show, and "$guardianstone_name" is not a name to any of them.
        ///
        /// Localize substitutes tokens anywhere in a string, so a label that mixes a prefab-derived
        /// word with a token - "Eikthyr $guardianstone_name" - comes out right in every language.
        public static string Localize(string text) =>
            string.IsNullOrEmpty(text) || Localization.instance == null
                ? text
                : Localization.instance.Localize(text);

        /// The one place a source label becomes the text on a pin: resolve its tokens, then take
        /// the colour markup off. Everything that writes a pin name goes through here, and so does
        /// PinRecord.Relabel, so a language change produces the same text the pin would have been
        /// given had it been placed in that language. Two paths that agree by coincidence would
        /// leave Relabel unable to recognise its own handiwork.
        public static string ForPin(string label) => StripRichText(Localize(label)) ?? string.Empty;

        /// Valheim colours miniboss names in its own UI, so their Character.m_name arrives as
        /// "<color=orange>Brenna</color>". That markup would be written into the save and drawn on
        /// the pin, so the tags come off and the name stays. Only the four minibosses (Brenna,
        /// Geirrhafa, Zil & Thungr, Lord Reto) need it today.
        public static string StripRichText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
                return text;

            var sb = new StringBuilder(text.Length);
            int depth = 0;
            foreach (char c in text)
            {
                if (c == '<') depth++;
                else if (c == '>') { if (depth > 0) depth--; }
                else if (depth == 0) sb.Append(c);
            }

            string stripped = sb.ToString().Trim();
            return stripped.Length > 0 ? stripped : text;
        }

        public static string ForLocation(string prefabName) => Prettify(prefabName);

        /// "Runestone_Boars" -> "Boars Runestone", "Runestone_BlackForest" -> "Black Forest
        /// Runestone". Qualifier first, like a spawner label, instead of Prettify's "Runestone
        /// Boars", which reads backwards.
        public static string ForLoreStone(string prefabName)
        {
            const string prefix = "Runestone_";
            string rest = prefabName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? prefabName.Substring(prefix.Length)
                : prefabName;

            string pretty = Prettify(rest);
            return string.IsNullOrEmpty(pretty) ? Prettify(prefabName) : pretty + " Runestone";
        }

        /// Words that describe how a spawner is tuned, or which biome's copy of it this is, rather
        /// than what comes out of it. Taken from the 103 spawner prefabs the catalog registers: the
        /// skeleton line alone ships eleven variants (_hildir, _Meadows, _Mountains, _Swamp,
        /// _poison, _rise, _respawn_30, _night_noarcher...), all the same pin on a map.
        ///
        /// Creature variants are deliberately NOT in here - Elite, Brute, Shaman, Archer and Mage
        /// are different things to meet and the label should keep saying so.
        private static readonly HashSet<string> SpawnerNoise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "respawn", "night", "noarcher", "noise", "rise", "sleeping", "wakeup",
            "event", "bossroom", "stared", "surprise", "location", "random", "hildir",
            "meadows", "mountains", "mountain", "swamp", "forest", "ashlands", "deep", "north", "cave",
        };

        /// Names that already say the pin is a place, so appending "Spawner" would stutter.
        private static readonly HashSet<string> PlaceWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nest", "pile", "heart", "core", "spawner", "totem", "stand", "hole",
        };

        /// Spawners whose prefab never names the creature that comes out of them. Left to the
        /// rules above, Spawner_Hole reads "Hole" and Spawner_Location_Elite reads "Elite".
        ///
        /// Hole is the greydwarf spawn hole. Elite and Shaman sit beside Spawner_Location_Greydwarf
        /// in the catalog and are inferred to be the elite and shaman forms placed in the same
        /// camps - an inference from the sibling prefabs, not something the name states.
        private static readonly Dictionary<string, string> SpawnerAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Hole", "Greydwarf Spawner" },
            { "Elite", "Greydwarf Elite Spawner" },
            { "Shaman", "Greydwarf Shaman Spawner" },
        };

        /// "Spawner_Skeleton" -> "Skeleton Spawner", "Spawner_Skeleton_night_noarcher" -> the same,
        /// "Spawner_GreydwarfNest" -> "Greydwarf Nest" (left alone, it already reads as a place).
        ///
        /// The "Spawner" suffix is the only word saying this is a spawn point; without it the map
        /// reads "Skeleton" and looks like a skeleton is standing there.
        public static string ForSpawner(string prefabName)
        {
            // Prettify first so underscores and camelCase both split into words, and
            // "Spawner_Skeleton_Meadows" and "Spawner_DvergerDeepNorth" filter the same way.
            return CleanSpawnerLabel(Prettify(StripSpawnerPrefix(prefabName)));
        }

        /// The half of ForSpawner that works on words, so a label already in somebody's save can be
        /// repaired without the prefab, which is gone by the time a pin is loaded back.
        /// "Skeleton Night Noarcher" and "Skeleton" both leave as "Skeleton Spawner".
        public static string CleanSpawnerLabel(string prettified)
        {
            if (string.IsNullOrEmpty(prettified))
                return prettified;

            string[] words = prettified.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var kept = new List<string>(words.Length);
            foreach (string w in words)
            {
                if (!SpawnerNoise.Contains(w))
                    kept.Add(w);
            }

            // Everything filtered out: keep the original rather than an empty label.
            if (kept.Count == 0)
                return prettified;

            string name = string.Join(" ", kept.ToArray());
            if (SpawnerAliases.TryGetValue(name, out string alias))
                return alias;

            return PlaceWords.Contains(kept[kept.Count - 1]) ? name : name + " Spawner";
        }

        private static string StripSpawnerPrefix(string name)
        {
            foreach (string prefix in new[] { "Spawner_", "Spawn_" })
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(prefix.Length);
            }
            return name;
        }

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

            foreach (string noise in new[] { "Mistlands ", "Ashlands ", "Hildir ", "Dvergr " })
            {
                if (s.StartsWith(noise, System.StringComparison.OrdinalIgnoreCase) && s.Length > noise.Length)
                    s = s.Substring(noise.Length);
            }

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
