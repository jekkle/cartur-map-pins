using System.Collections.Generic;

namespace CarturMapPins
{
    /// Per-kind icons for the categories where one icon for the whole category loses the
    /// information you actually wanted on the map: a Burial Chamber vs a Frost Cave, copper vs
    /// tin, Haldor vs Hildir, a wolf den vs a draugr pile.
    ///
    /// Matching is on fragments of a name, checked in order - so "sunkencrypt" must be tested
    /// before the bare "crypt", and "greydwarf_shaman" before "greydwarf". Anything unmatched
    /// falls back to the category icon and gets logged, so a missing name can be added rather
    /// than silently mislabelled.
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
            E("hildir_crypt", "Hildir Crypt", 30),
            E("hildir_cave", "Hildir Cave", 33),
            E("dg_sunkencrypt", "Sunken Crypt", 32),
            E("sunkencrypt", "Sunken Crypt", 32),
            E("dg_dvergrboss", "Infested Citadel", 133),
            E("dvergrboss", "Infested Citadel", 133),
            E("dg_dvergrtown", "Infested Mine", 34),
            E("dvergrtown", "Infested Mine", 34),
            E("mountaincave", "Frost Cave", 33),
            E("trollcave", "Troll Cave", 31),
            E("morgen", "Putrid Hole", 126),
            E("dg_forestcrypt", "Crypt", 30),
            E("crypt", "Crypt", 30),        // after sunkencrypt, so that wins
            E("dg_cave", "Frost Cave", 33), // last: bare "cave" is the vaguest signal
        };

        public static readonly Entry[] Camps =
        {
            E("hildir_plainsfortress", "Hildir Fortress", 40),
            E("dg_goblincamp", "Fuling Village", 41),
            E("goblincamp", "Fuling Village", 41),
            E("dg_fortressruins", "Charred Fortress", 36),
            E("dg_ashlandruins", "Ashlands Ruin", 40),
            E("charredfortress", "Charred Fortress", 36),
            E("fortress", "Charred Fortress", 36),
            E("dg_meadowsvillage", "Abandoned Village", 42),
            E("dg_meadowsfarm", "Abandoned Farm", 77),
            E("greydwarf", "Greydwarf Camp", 0),
            E("hildir", "Hildir Camp", 61),
        };

        /// Keyed by the ore type PinCatalog.OreTokens already resolves ("Copper", "Tin"), not by
        /// a prefab name - so unlike the tables above these are never run through Match, the
        /// Fragment is just the key repeated. Sulfur has no icon of its own in the sheet and
        /// falls back to the generic ore glyph.
        public static readonly Entry[] Ores =
        {
            E("Copper", "Copper", 110),
            E("Tin", "Tin", 111),
            E("Iron", "Iron", 112),
            E("Silver", "Silver", 113),
            E("Obsidian", "Obsidian", 116),
            E("Flametal", "Flametal", 115),
            E("Sulfur", "Sulfur", 119),
            E("Black Marble", "Black Marble", 147),
            E("Giant Remains", "Giant Remains", 145),
            E("Chitin", "Chitin", 146),
            E("Tar", "Tar", 13),
        };

        /// Matched against OfferingBowl.m_bossPrefab's name, which is the boss's own prefab
        /// ("Eikthyr", "gd_king", "GoblinKing"), falling back to the bowl's m_name.
        public static readonly Entry[] Bosses =
        {
            E("eikthyr", "Eikthyr", 63),
            E("gd_king", "The Elder", 64),
            E("elder", "The Elder", 64),
            E("bonemass", "Bonemass", 65),
            E("dragon", "Moder", 66),
            E("moder", "Moder", 66),
            E("goblinking", "Yagluth", 67),
            E("yagluth", "Yagluth", 67),
            E("seekerqueen", "The Queen", 68),
            E("queen", "The Queen", 68),
            E("fader", "Fader", 69),
        };

        /// Matched against the trader's prefab name. Confirmed: Trader.m_name is empty on the
        /// Bog Witch, so her prefab name is what identifies her - see PinPlacer.ResolveLabel.
        public static readonly Entry[] Traders =
        {
            E("haldor", "Haldor", 60),
            E("hildir", "Hildir", 61),
            E("bogwitch", "Bog Witch", 62),
        };

        /// Matched against the prefab name of the creature the spawner spawns, read off
        /// CreatureSpawner.m_creaturePrefab / SpawnArea rather than off the spawner's own name -
        /// the same "ask the object, don't pattern-match its name" principle the ore catalog
        /// uses, and it answers the cases the spawner name cannot (Spawner_Hole,
        /// Spawner_Location_Elite and EvilHeart_Forest all name a place or a tuning variant).
        ///
        /// Creature prefab names are Unity asset references and can't be read offline, so these
        /// are the standard spellings and unmatched ones fall back to the category icon and get
        /// logged - run the game and read the log to extend this.
        public static readonly Entry[] Spawners =
        {
            E("greydwarf_shaman", "Greydwarf Shaman", 20),   // before the bare "greydwarf"
            E("greydwarf", "Greydwarf", 0),
            E("greyling", "Greydwarf", 0),
            E("skeleton", "Skeleton", 1),
            E("draugr", "Draugr", 2),
            E("goblin", "Fuling", 3),
            E("seeker", "Seeker", 5),
            E("surtling", "Surtling", 6),
            E("charred", "Charred", 7),
            E("hatchling", "Drake", 8),
            E("abomination", "Abomination", 10),
            E("morgen", "Morgen", 11),
            E("volture", "Volture", 12),
            E("blobtar", "Tar Blob", 13),          // before the bare "blob"
            E("growth", "Growth", 16),
            E("boar", "Boar", 138),         // the animal, not the boar spawn-stone glyph
            E("fenring", "Fenring", 17),
            E("fallenvalkyrie", "Fallen Valkyrie", 132),
            E("troll", "Troll", 21),
            E("wolf", "Wolf", 22),
            E("ulv", "Ulv", 22),               // wolf-kin, and there is no ulv glyph
            E("deathsquito", "Deathsquito", 23),
            E("lox", "Lox", 24),
            E("gjall", "Gjall", 25),
            E("tick", "Tick", 26),
            E("wraith", "Wraith", 27),
            E("ghost", "Wraith", 27),
            E("stonegolem", "Stone Golem", 28),
            E("serpent", "Serpent", 29),
        };

        /// Matched against the pickable's prefab name, so a bush gets its own berry rather than
        /// every berry sharing one glyph. Names confirmed from `carturpins_labels Pickable`.
        ///
        /// Order matters where one name contains another: every mushroom variant is listed
        /// before the bare "mushroom", and the ore-bearing pickables use their full prefab
        /// fragment so "tin" cannot catch something else.
        ///
        /// Anything unmatched falls back to its PickableGroup icon, which is why this table only
        /// needs the pickables the sheet actually has art for.
        public static readonly Entry[] Pickables =
        {
            E("raspberrybush", "Raspberry", 90),
            E("blueberrybush", "Blueberry", 91),
            E("cloudberrybush", "Cloudberry", 92),
            E("lingonberrybush", "Lingonberry", 140),
            E("vinegreen", "Vineberry", 141),
            E("vineash", "Vineberry", 141),

            E("mushroom_jotunpuffs", "Jotun Puffs", 98),   // before the bare "mushroom"
            E("mushroom_magecap", "Magecap", 97),
            E("mushroom_yellow", "Yellow Mushroom", 96),
            E("smokepuff", "Smoke Puff", 142),
            E("mushroom", "Mushroom", 95),

            E("dandelion", "Dandelion", 93),
            E("thistle", "Thistle", 94),
            E("fiddlehead", "Fiddlehead", 99),
            E("carrot", "Carrot", 100),                    // also catches SeedCarrot
            E("turnip", "Turnip", 101),
            E("onion", "Onion", 102),
            E("barley", "Barley", 103),
            E("flax", "Flax", 104),

            E("pickable_flint", "Flint", 108),
            E("pickable_branch", "Branch", 107),
            E("surtlingcorestand", "Surtling Core", 118),
            E("moltencorestand", "Molten Core", 143),
            E("blackcorestand", "Black Core", 144),
            E("mountaincavecrystal", "Cave Crystal", 117),
            E("royaljelly", "Royal Jelly", 105),
            E("pickable_obsidian", "Obsidian", 116),
            E("pickable_tin", "Tin Nugget", 111),
            E("bogironore", "Bog Iron", 112),
            E("pickable_tar", "Tar", 13),
            E("meteorite", "Meteorite", 115),
            E("dragonegg", "Dragon Egg", 8),
            E("voltureegg", "Volture Egg", 12),
        };

        /// Surface landmarks: locations with no interior, no dungeon generator and no runestone,
        /// which the sweep previously identified only to discard. Wells, docks, shipwrecks,
        /// dolmens, stone circles, swamp huts, abandoned houses.
        ///
        /// Unlike the dungeon and camp tables these fragments are NOT guesses - every one is a
        /// real prefab name from a live `carturpins_locations` dump of all 232 ZoneLocations.
        ///
        /// Locations already covered by another category are deliberately absent: the boss altars,
        /// the eleven Runestone_* lore stones, Vendor_BlackForest (Haldor, handled as a Trader),
        /// and the nest/spawner locations, all of which would otherwise pin twice.
        public static readonly Entry[] Landmarks =
        {
            E("shipwreck", "Shipwreck", 47),
            E("frozenship", "Shipwreck", 47),
            E("shipsetting", "Ship Setting", 56),
            E("swamphut", "Swamp Hut", 44),          // before the bare "hut"
            E("swampwell", "Well", 48),
            E("mountainwell", "Well", 48),
            E("dolmen", "Dolmen", 46),
            E("stonecircle", "Stone Circle", 45),
            E("stonehenge", "Stone Circle", 45),
            E("stonehouse", "Stone House", 43),
            E("woodhouse", "Abandoned House", 43),
            E("abandonedlogcabin", "Log Cabin", 43),
            E("dn_hut", "Deep North Hut", 43),
            E("firehole", "Fire Geyser", 49),
            E("tarpit", "Tar Pit", 13),
            E("sulfurarch", "Sulfur Arch", 129),
            E("placeofmystery", "Place of Mystery", 127),
            E("mistlands_viaduct", "Viaduct", 134),
            E("mistlands_harbour", "Harbour", 55),
            E("mistlands_swords", "Giant Sword", 52),
            E("mistlands_giant", "Giant Skull", 51),  // "Giant Remains" is taken by the ore table
            E("mistlands_excavation", "Dvergr Excavation", 38),
            E("mistlands_lighthouse", "Lighthouse", 40),
            E("gammeltroll", "Petrified Troll", 125),
            E("leviathanlava", "Lava Leviathan", 128),
            E("starttemple", "Sacrificial Stones", 59),
            E("ancientupgradestation", "Ancient Upgrade Station", 75),
            E("infestedtree", "Infested Tree", 14),
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
