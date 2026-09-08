using System.Collections.Generic;

namespace CarturMapPins
{
    /// Dungeons and camps get an icon per kind rather than one for the whole category, so a
    /// Burial Chamber, a Frost Cave and an Infested Mine are distinguishable at a glance.
    ///
    /// Matching is on fragments of the location's prefab name, checked in order - so
    /// "sunkencrypt" must be tested before the bare "crypt", and "dvergrboss" before
    /// "dvergrtown". Several fragments map to the same subtype because only a couple of real
    /// prefab names are known for certain from logs ("Crypt2", "TrollCave02"); the rest cover the
    /// plausible spellings. Anything unmatched falls back to the category icon and gets logged,
    /// so a missing name can be added rather than silently mislabelled.
    internal static class Subtypes
    {
        public struct Entry
        {
            public string Fragment;
            public string Name;
            public int DefaultIcon;
        }

        private static Entry E(string fragment, string name, int icon) =>
            new Entry { Fragment = fragment, Name = name, DefaultIcon = icon };

        public static readonly Entry[] Dungeons =
        {
            E("sunkencrypt", "Sunken Crypt", 28),
            E("mountaincave", "Frost Cave", 18),
            E("frostcave", "Frost Cave", 18),
            E("trollcave", "Troll Cave", 50),
            E("forestcave", "Troll Cave", 50),
            E("dvergrboss", "Infested Citadel", 67),
            E("citadel", "Infested Citadel", 67),
            E("dvergrtown", "Infested Mine", 57),
            E("infestedmine", "Infested Mine", 57),
            E("morgen", "Putrid Hole", 73),
            E("mausoleum", "Tomb", 71),
            E("tomb", "Tomb", 71),
            E("crypt", "Crypt", 22),        // last: ForestCrypt/Crypt2, after sunkencrypt
        };

        public static readonly Entry[] Camps =
        {
            E("goblincamp", "Fuling Village", 77),
            E("goblinvillage", "Fuling Village", 77),
            E("fuling", "Fuling Village", 77),
            E("charred", "Charred Fortress", 16),
            E("fortress", "Charred Fortress", 16),
            E("greydwarf", "Greydwarf Camp", 13),
            E("dvergrtower", "Dvergr Tower", 67),
            E("sealedtower", "Dvergr Tower", 67),
            E("hildir", "Hildir Camp", 34),
        };

        /// Distinct subtype names in declaration order, for binding one config entry each.
        public static IEnumerable<Entry> DistinctOf(Entry[] table)
        {
            var seen = new HashSet<string>();
            foreach (Entry e in table)
            {
                if (seen.Add(e.Name))
                    yield return e;
            }
        }

        /// Returns the subtype name for a prefab, or null when nothing matches.
        public static string Match(Entry[] table, string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return null;

            string needle = prefabName.ToLowerInvariant();
            foreach (Entry e in table)
            {
                if (needle.Contains(e.Fragment))
                    return e.Name;
            }
            return null;
        }
    }
}
