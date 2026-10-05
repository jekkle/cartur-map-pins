using System.Collections.Generic;

namespace CarturMapPins
{
    /// Per-kind icons for the categories where one icon for the whole category loses the
    /// information you actually wanted on the map: a Burial Chamber vs a Frost Cave, copper vs
    /// tin, Haldor vs Hildir, a wolf den vs a draugr pile.
    ///
    /// Matching is on fragments of a name, checked in order, so "sunkencrypt" must be tested
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

            /// Which pickable group this belongs to, for the Pickables table only. Nullable
            /// because the first value of PickableGroup is HighValue, so a forgotten group would
            /// quietly mean "rare and worth remembering" for every dungeon and spawner.
            public PickableGroup? Group;
        }

        private static Entry E(string fragment, string name, int icon) =>
            new Entry { Fragment = fragment, Name = name, DefaultIcon = icon };

        /// A pickable, which carries its group as well.
        private static Entry EP(string fragment, string name, int icon, PickableGroup group) =>
            new Entry { Fragment = fragment, Name = name, DefaultIcon = icon, Group = group };

        /// Fragments are matched against the location's prefab name AND, when it has one, its
        /// DungeonGenerator's name. The DG_* names are confirmed from the game's own asset
        /// manifest, so those matches are exact rather than guesswork:
        ///   DG_ForestCrypt DG_SunkenCrypt DG_Cave DG_GoblinCamp DG_DvergrTown DG_DvergrBoss
        ///   DG_MeadowsVillage DG_MeadowsFarm DG_AshlandRuins DG_FortressRuins
        ///   DG_Hildir_Cave DG_Hildir_ForestCrypt DG_Hildir_PlainsFortress
        /// Location prefab names cannot be read offline (Unity asset references, not string
        /// literals), so run `carturpins_locations` in game to dump the authoritative list and
        /// extend these tables from it.
        public static readonly Entry[] Dungeons =
        {
            // Hildir variants first, they contain the base names they vary.
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
            // Deep North. The icon sheet has glyphs for these; without entries all three pin on
            // the generic stairs-down. The generator name is the sure signal for the first: DG_MorkHalla is
            // what the morkhalla icon is named after, and the Deep North progression is built on
            // two dungeon types, the gates of Morkhalla and the Winding Tunnels below.
            E("dg_morkhalla", "Gates of Morkhalla", 122),
            E("morkborg", "Gates of Morkhalla", 122),
            // The Deep North's other dungeon type. Both are built from HoleRock_root pieces
            // (root-choked rock); TheDarkestHole is the unique one, a single instance against
            // forty of the ordinary kind, so it keeps its own name.
            //
            // After "morgen" above, which matches the Ashlands MorgenHole prefabs: those contain
            // "hole" too and are a different dungeon entirely.
            E("darkesthole", "The Darkest Hole", 123),
            E("thehole", "Winding Tunnels", 123),
            // Bear Cave has no generator and nothing above would catch it; it is a Black Forest
            // cave mouth, which is what icon 35 draws.
            E("bearcave", "Bear Cave", 35),
            // The Ashlands oddity. Its two surface siblings already pin as Place of Mystery via
            // the landmark table on icon 127; the one with an interior matches them.
            E("placeofmystery", "Place of Mystery", 127),
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
            // The Meadows villages and farm are WoodVillage1/2 and WoodFarm1 in the location list;
            // the DG_ names above them are generators nothing in this build uses.
            E("woodvillage", "Abandoned Village", 42),
            E("woodfarm", "Abandoned Farm", 77),
            E("northvillage", "Deep North Village", 124),
            // "greydwarf_camp", not "greydwarf": the camp table is matched by name before the lore
            // stones, and a bare fragment would turn Runestone_Greydwarfs into a Greydwarf Camp.
            E("greydwarf_camp", "Greydwarf Camp", 0),
            E("hildir", "Hildir Camp", 61),
        };

        /// World objects worth a pin that carry no component saying so.
        ///
        /// Everything else in the catalog is found by asking the object what it is: a Container
        /// with loot, a Pickable, a spawner. A maypole is a Piece with a WearNTear, exactly like
        /// a wooden wall, so only its name identifies it. This table is therefore a deliberate
        /// allowlist, not a rule.
        ///
        /// These all exist in player-built form too, so the spawn hook checks the ZDO creator and
        /// skips anything somebody built, the same gate beehives and chests go through.
        public static readonly Entry[] Props =
        {
            E("maypole", "Maypole", 149),
        };

        /// Ruins worth recognising but not worth a pin of their own.
        ///
        /// There are four kinds of stone tower ruin at quantity 80 apiece and six Mistlands guard
        /// tower variants at 50-80: pinning the locations themselves would put five hundred icons
        /// of broken wall on the map. But each holds a chest that is already pinned, so the chest
        /// pin wears the ruin's icon and name instead of the generic chest glyph.
        ///
        /// Matched against the name of the location a chest is standing inside, so these never
        /// touch a chest out in the open.
        ///
        /// Order matters: "stonetowerruins" sits before "stonetower" or every ruin would come out
        /// as an intact tower.
        public static readonly Entry[] ChestSites =
        {
            E("stonetowerruins", "Stone Tower Ruin", 40),
            E("stonetower", "Stone Tower", 40),
            E("mistlands_guardtower", "Dvergr Tower", 37),
        };

        /// Chests the game buries: TreasureChest_meadows_buried and TreasureChest_memorial_buried,
        /// read from the ZNetScene dump (docs/prefab-reference.txt). Matched against the chest's
        /// own prefab name, not a location - a buried chest sits out in the open. Its own kind, so
        /// it has its own switch and its own icon (the sheet's UtilBuriedX), which also makes it
        /// hideable on its own with a right-click (DeadByte42, crpgnut).
        public static readonly Entry[] BuriedChests =
        {
            E("_buried", "Buried Chest", 73),
        };

        /// Keyed by the ore type PinCatalog.OreTokens already resolves ("Copper", "Tin"), not by
        /// a prefab name, so unlike the tables above these never go through Match and Fragment is
        /// just the key repeated. Sulfur has no icon of its own in the sheet and falls back to
        /// the generic ore glyph.
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

        /// What each ore is, in colour.
        ///
        /// The map is dark, so these are lifted towards the lightest honest reading of the
        /// material: obsidian, tar and black marble are all but black in the game and would be a
        /// hole in the map at pin size. Copper stays copper.
        ///
        /// Keyed by the same names the ore table uses, which the catalog resolves from what a
        /// deposit drops, so an ore added by a game update has no colour until one is written
        /// here rather than taking somebody else's.
        public static readonly Dictionary<string, string> OreColours = new Dictionary<string, string>
        {
            { "Copper", "C87A33" },
            { "Tin", "D6DCE4" },
            { "Iron", "9A9A9A" },
            { "Silver", "E4EDF5" },
            { "Obsidian", "6E6A86" },       // lifted: obsidian is near black
            { "Flametal", "E8562A" },
            { "Sulfur", "E8D44D" },
            { "Black Marble", "8A7BA8" },   // lifted
            { "Giant Remains", "E0D2AC" },
            { "Chitin", "CDB79A" },
            { "Tar", "6B6459" },            // lifted
        };

        /// Matched against OfferingBowl.m_bossPrefab's name, which is the boss's own prefab
        /// ("Eikthyr", "gd_king", "GoblinKing"), falling back to the bowl's m_name.
        ///
        /// A vegvisir reveal has no bowl to ask, so the location's own name and the pin name the
        /// stone carries are matched here too: "Eikthyrnir", "GDKing", "Dragonqueen". Order
        /// matters for those: Moder's location is called Dragonqueen, so "dragon" must be tried
        /// before "queen" or Moder's altar would pin as the Queen's.
        public static readonly Entry[] Bosses =
        {
            E("eikthyr", "Eikthyr", 63),
            E("gd_king", "The Elder", 64),
            // The one boss whose location name is spelled differently from its prefab: the altar
            // says "gd_king", the ZoneLocation says "GDKing".
            E("gdking", "The Elder", 64),
            E("elder", "The Elder", 64),
            E("bonemass", "Bonemass", 65),
            E("dragon", "Moder", 66),
            E("moder", "Moder", 66),
            E("goblinking", "Yagluth", 67),
            E("yagluth", "Yagluth", 67),
            E("seekerqueen", "The Queen", 68),
            E("queen", "The Queen", 68),
            E("fader", "Fader", 69),
            // Deep North. The altar names its boss "FrozenKing"; the sheet calls the icon
            // boss_kall, and the label the altar itself hands us confirms it is Kall Fimbulbringer.
            E("frozenking", "Kall Fimbulbringer", 120),
            // Not a boss, but it arrives here because NorthMemorialPlace holds an offering bowl,
            // which is what identifies a boss altar. Icon 59 is three standing stones, which is
            // what the place is.
            E("memorialsite", "Memorial Site", 59),
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
        /// CreatureSpawner.m_creaturePrefab / SpawnArea rather than the spawner's own name: the
        /// same "ask the object" principle as the ore catalog, and it answers the cases the
        /// spawner name cannot (Spawner_Hole, Spawner_Location_Elite and EvilHeart_Forest all
        /// name a place or a tuning variant).
        ///
        /// Creature prefab names are Unity asset references and cannot be read offline, so these
        /// are the standard spellings; unmatched ones fall back to the category icon and get
        /// logged, so read the log to extend this.
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
            // Before the bare "charred", which his prefab name also contains. Lord Reto is the
            // two-star miniboss guarding a Dyrnwyn fragment, not a rank-and-file Charred.
            E("dyrnwyn", "Lord Reto", 130),
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

        /// The named, hand-placed minibosses, matched on the creature their spawner spawns.
        ///
        /// These are not the Spawner category. A greydwarf nest is scenery and there are 103
        /// spawner prefabs, which is why that category is off by default, but Lord Reto and
        /// Hildir's three are single, named, fought-once creatures and should still appear.
        ///
        /// Matched before Subtypes.Spawners, which would otherwise take them: Lord Reto's prefab
        /// contains "charred", Brenna's "skeleton", and Zil and Thungr's "goblin".
        public static readonly Entry[] Minibosses =
        {
            E("charred_melee_dyrnwyn", "Lord Reto", 130),
            E("skeleton_hildir", "Brenna", 18),          // bone pile with a sword
            E("goblinbrute_hildir", "Zil & Thungr", 4),  // fuling banner, for the pair
            E("cultist_hildir", "Geirrhafa", 17),        // she is a fenring cultist
            E("fallenvalkyrie", "Fallen Valkyrie", 132),
        };

        /// Matched against the pickable's prefab name, so a bush gets its own berry rather than
        /// every berry sharing one glyph. Names confirmed from `carturpins_labels Pickable`.
        /// Each entry also carries the pickable's group.
        ///
        /// Order matters where one name contains another: every mushroom variant is listed
        /// before the bare "mushroom", and the ore-bearing pickables use their full prefab
        /// fragment so "tin" cannot catch something else. The wild crops sit above their farmed
        /// selves; both entries share a Name, so DistinctOf gives them one switch and one icon
        /// and only the group differs.
        ///
        /// Anything unmatched falls back to its PickableGroup icon, so this table only needs the
        /// pickables the sheet has art for.
        ///
        /// The group is a table column because substring rules over the prefab name failed at
        /// scale: 44 of the game's 89 pickables fell through to "Unrecognised", and the junk rule
        /// matched the letters "stone", so every Mork Halla gemstone (Bloodstone, Draumyx and the
        /// rest, the rarest things in the game) was filed with sticks and rocks.
        public static readonly Entry[] Pickables =
        {
            EP("raspberrybush", "Raspberry", 90, PickableGroup.Berries),
            EP("blueberrybush", "Blueberry", 91, PickableGroup.Berries),
            EP("cloudberrybush", "Cloudberry", 92, PickableGroup.Berries),
            EP("lingonberrybush", "Lingonberry", 140, PickableGroup.Berries),
            EP("vinegreen", "Vineberry", 141, PickableGroup.Berries),
            EP("vineash", "Vineberry", 141, PickableGroup.Berries),

            EP("mushroom_jotunpuffs", "Jotun Puffs", 98, PickableGroup.Mushrooms),  // before bare "mushroom"
            EP("mushroom_magecap", "Magecap", 97, PickableGroup.Mushrooms),
            EP("mushroom_yellow", "Yellow Mushroom", 96, PickableGroup.Mushrooms),
            EP("smokepuff", "Smoke Puff", 142, PickableGroup.Mushrooms),
            EP("mushroom", "Mushroom", 95, PickableGroup.Mushrooms),

            // Wild before farmed: a patch out in the Plains is worth walking back to, the field
            // behind your house is not. Measured: carturpins_plants reports sapling_barley ->
            // Pickable_Barley, so the plain prefab is what a farm grows and _Wild is world
            // generation only.
            EP("barley_wild", "Barley", 103, PickableGroup.HighValue),
            EP("flax_wild", "Flax", 104, PickableGroup.HighValue),

            EP("dandelion", "Dandelion", 93, PickableGroup.Crops),
            EP("thistle", "Thistle", 94, PickableGroup.Crops),
            EP("fiddlehead", "Fiddlehead", 99, PickableGroup.Crops),
            EP("carrot", "Carrot", 100, PickableGroup.Crops),           // also catches SeedCarrot
            EP("turnip", "Turnip", 101, PickableGroup.Crops),
            EP("onion", "Onion", 102, PickableGroup.Crops),
            EP("barley", "Barley", 103, PickableGroup.Crops),
            EP("flax", "Flax", 104, PickableGroup.Crops),

            EP("pickable_flint", "Flint", 108, PickableGroup.Junk),
            EP("pickable_branch", "Branch", 107, PickableGroup.Junk),

            // Fixed to one spot, finite, and worth a second trip, which is the whole test for this
            // group. The commoner ones (tin, obsidian, bog iron) are here for the same reason and
            // can be switched off one at a time under Pickable Kinds.
            EP("surtlingcorestand", "Surtling Core", 118, PickableGroup.HighValue),
            EP("moltencorestand", "Molten Core", 143, PickableGroup.HighValue),
            EP("blackcorestand", "Black Core", 144, PickableGroup.HighValue),
            EP("mountaincavecrystal", "Cave Crystal", 117, PickableGroup.HighValue),
            EP("royaljelly", "Royal Jelly", 105, PickableGroup.HighValue),
            EP("pickable_obsidian", "Obsidian", 116, PickableGroup.HighValue),
            EP("pickable_tin", "Tin Nugget", 111, PickableGroup.HighValue),
            EP("bogironore", "Bog Iron", 112, PickableGroup.HighValue),
            EP("pickable_tar", "Tar", 13, PickableGroup.HighValue),
            EP("meteorite", "Meteorite", 115, PickableGroup.HighValue),
            EP("dragonegg", "Dragon Egg", 8, PickableGroup.HighValue),
            EP("voltureegg", "Volture Egg", 12, PickableGroup.HighValue),

            // Without entries these fall into Unrecognised, which is off by default, and they are
            // some of the rarest things in the game. Icons are reused from the existing sheet, not
            // new art: a star for the gemstones, the giant sword for the Dyrnwyn pieces, the coin
            // pile for treasure, the crystal for frostcore.
            //
            // One entry each rather than one "Ancient Gemstone" covering all seven, because
            // dedupe is per subtype: a shared one would let the first gemstone found suppress a
            // different gemstone five metres away.
            EP("morkhalla_eye1", "Draumyx", 85, PickableGroup.HighValue),
            EP("morkhalla_eye2", "Grimvarn", 85, PickableGroup.HighValue),
            EP("morkhalla_eye3", "Solryth", 85, PickableGroup.HighValue),
            EP("morkhalla_eye4", "Veydris", 85, PickableGroup.HighValue),
            EP("morkhalla_eye5", "Iolite", 85, PickableGroup.HighValue),
            EP("morkhalla_eye6", "Jade", 85, PickableGroup.HighValue),
            EP("morkhalla_eye7", "Bloodstone", 85, PickableGroup.HighValue),

            EP("swordpiece1", "Dyrnwyn Hilt", 52, PickableGroup.HighValue),
            EP("swordpiece2", "Dyrnwyn Blade", 52, PickableGroup.HighValue),
            EP("swordpiece3", "Dyrnwyn Tip", 52, PickableGroup.HighValue),

            EP("frostcore", "Frostcore", 117, PickableGroup.HighValue),
            EP("dolmentreasure", "Dolmen Treasure", 88, PickableGroup.HighValue),
            EP("dvergrminetreasure", "Coin Pile", 88, PickableGroup.HighValue),

            // Farm food belongs in Crops with its seeds. "seedkale" first, or "kale" would
            // swallow it.
            EP("seedkale", "Kale Seeds", 99, PickableGroup.Crops),
            EP("kale", "Kale", 99, PickableGroup.Crops),
            EP("poteitr", "Poteitr", 101, PickableGroup.Crops),
        };

        /// Surface landmarks: locations with no interior, no dungeon generator and no runestone.
        /// Wells, docks, shipwrecks, dolmens, stone circles, swamp huts, abandoned houses.
        ///
        /// Unlike the dungeon and camp tables these fragments are NOT guesses - every one is a
        /// real prefab name from a live `carturpins_locations` dump of all 232 ZoneLocations.
        ///
        /// Locations already covered by another category are deliberately absent: the boss altars,
        /// the eleven Runestone_* lore stones, Vendor_BlackForest (Haldor, handled as a Trader),
        /// and the nest/spawner locations, all of which would otherwise pin twice.
        ///
        /// StartTemple is absent for the same reason even though nothing of ours covers it: it is
        /// the one location flagged iconAlways, so vanilla already marks the spawn temple and a
        /// landmark pin would be a second icon on the same spot.
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
            // Intact guard towers have no chest (docs/location-reference.txt), so ChestSites never
            // reaches them and only the ruined ones were pinned (GitHub #3, #8). Full names, because
            // "mistlands_guardtower1_new" is not inside "mistlands_guardtower1_ruined_new" and the
            // ruined ones must not pin a second time on top of their chest.
            E("mistlands_guardtower1_new", "Dvergr Tower", 37),
            E("mistlands_guardtower2_new", "Dvergr Tower", 37),
            E("mistlands_guardtower3_new", "Dvergr Tower", 37),
            E("gammeltroll", "Petrified Troll", 125),
            E("leviathanlava", "Lava Leviathan", 128),
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

        /// Every table, so Translations can collect the names without a second list that would
        /// drift the first time a table is added. A table missing from here is a name that stays
        /// English.
        public static readonly Entry[][] AllTables =
        {
            Dungeons, Camps, Props, ChestSites, BuriedChests, Ores, Bosses, Traders, Spawners, Minibosses,
            Pickables, Landmarks,
        };

        /// Returns the subtype name for a location, or null when nothing matches.
        ///
        /// The generator name is checked first where present: the DG_* names are confirmed from
        /// the game's asset manifest, so the reliable signal gets priority.
        public static string Match(Entry[] table, string prefabName, string generatorName = null)
        {
            string byGenerator = MatchOne(table, generatorName);
            if (byGenerator != null)
                return byGenerator;
            return MatchOne(table, prefabName);
        }

        /// The group the Pickables table gives this prefab, or null when nothing matches: modded
        /// content, or something a game update added that nobody has classified yet.
        public static PickableGroup? GroupFor(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return null;

            string needle = prefabName.ToLowerInvariant();
            foreach (Entry e in Pickables)
            {
                if (e.Group.HasValue && needle.Contains(e.Fragment))
                    return e.Group;
            }
            return null;
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
