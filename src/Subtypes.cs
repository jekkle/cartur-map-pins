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

        /// Fragments are matched against the location's prefab name AND, when it has one, its
        /// DungeonGenerator's name. The DG_* names are confirmed from the game's own asset
        /// manifest, so those matches are exact rather than guesswork:
        ///   DG_ForestCrypt DG_SunkenCrypt DG_Cave DG_GoblinCamp DG_DvergrTown DG_DvergrBoss
        ///   DG_MeadowsVillage DG_MeadowsFarm DG_AshlandRuins DG_FortressRuins
        ///   DG_Hildir_Cave DG_Hildir_ForestCrypt DG_Hildir_PlainsFortress
        /// Location prefab names can't be read offline (they're Unity asset references, not
        /// string literals), so run `carturpins_locations` in game to dump the authoritative
        /// list and extend these tables from it.
        public static readonly Entry[] Dungeons =
        {
            // Hildir variants first - they contain the base names they're variants of.
            E("hildir_forestcrypt", "Hildir Crypt", 22),
            E("hildir_cave", "Hildir Cave", 18),
            E("dg_sunkencrypt", "Sunken Crypt", 28),
            E("sunkencrypt", "Sunken Crypt", 28),
            E("dg_dvergrboss", "Infested Citadel", 67),
            E("dvergrboss", "Infested Citadel", 67),
            E("citadel", "Infested Citadel", 67),
            E("dg_dvergrtown", "Infested Mine", 57),
            E("dvergrtown", "Infested Mine", 57),
            E("infestedmine", "Infested Mine", 57),
            E("mountaincave", "Frost Cave", 18),
            E("frostcave", "Frost Cave", 18),
            E("trollcave", "Troll Cave", 50),
            E("forestcave", "Troll Cave", 50),
            E("morgen", "Putrid Hole", 73),
            E("mausoleum", "Tomb", 71),
            E("tomb", "Tomb", 71),
            E("dg_forestcrypt", "Crypt", 22),
            E("crypt", "Crypt", 22),        // after sunkencrypt, so that wins
            E("dg_cave", "Frost Cave", 18), // last: bare "cave" is the vaguest signal
        };

        public static readonly Entry[] Camps =
        {
            E("hildir_plainsfortress", "Hildir Fortress", 16),
            E("dg_goblincamp", "Fuling Village", 77),
            E("goblincamp", "Fuling Village", 77),
            E("goblinvillage", "Fuling Village", 77),
            E("fuling", "Fuling Village", 77),
            E("dg_fortressruins", "Charred Fortress", 16),
            E("dg_ashlandruins", "Ashlands Ruin", 74),
            E("charred", "Charred Fortress", 16),
            E("fortress", "Charred Fortress", 16),
            E("dg_meadowsvillage", "Abandoned Village", 79),
            E("dg_meadowsfarm", "Abandoned Farm", 41),
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

        /// Returns the subtype name for a location, or null when nothing matches.
        ///
        /// The generator name is checked first where present: those DG_* names are confirmed
        /// from the game's asset manifest, whereas the location prefab names are only partly
        /// known, so the reliable signal gets priority.
        public static string Match(Entry[] table, string prefabName, string generatorName = null)
        {
            string byGenerator = MatchOne(table, generatorName);
            if (byGenerator != null)
                return byGenerator;
            return MatchOne(table, prefabName);
        }

        private static string MatchOne(Entry[] table, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            string needle = name.ToLowerInvariant();
            foreach (Entry e in table)
            {
                if (needle.Contains(e.Fragment))
                    return e.Name;
            }
            return null;
        }
    }
}
