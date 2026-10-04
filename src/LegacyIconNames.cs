using System.Collections.Generic;
using BepInEx.Configuration;

namespace CarturMapPins
{
    /// Reads the icon names 1.2.x wrote into people's config files.
    ///
    /// 1.2.x bound every "Icon" setting to an enum generated from the 83-icon sheet, with names
    /// like MineFace, CobwebArch, Shrine and Bee. 1.3.0 replaced that sheet and regenerated the
    /// enum, so none of those names exist any more. BepInEx answers an unparseable value by
    /// logging a warning and using the setting's default - and, verified in
    /// ConfigEntryBase.SetSerializedValue, it discards the old string rather than keeping it
    /// anywhere - so the next time the file is written, the choice is gone for good.
    ///
    /// Everyone upgrading hit this, because the four categories with a non-vanilla default (Ore,
    /// Dungeon, Camp, Beehive) carried one of those names whether or not it was changed by hand.
    /// For them the outcome was right by accident: the 1.3 default is the same icon redrawn.
    /// Anyone who had picked something else lost it silently, which is the part worth undoing.
    ///
    /// Two outcomes per name. Where the new sheet redrew the same thing, the value becomes that
    /// icon, so the pin keeps its meaning in the current artwork. Where it did not - a chicken, a
    /// chalice, a needle and thread - the value points at the old sheet instead, which is still
    /// shipped and still registered on pin types 100-182. Nobody's choice is discarded either way.
    internal static class LegacyIconNames
    {
        /// Values at or above this mean "index into 1.2.x's sheet" rather than the current one.
        /// Above every real PinIcon and far enough clear to read as deliberate in a config file.
        public const int LegacyBase = 1000;

        /// Where 1.2.x's names landed.
        ///
        /// The nine that were 1.2.x category defaults are pinned to the matching 1.3 default rather
        /// than to the closest drawing, even where the drawing would be a better match - Nest to
        /// the summoning circle, Ember to the star. Almost everyone carrying one of those names
        /// never chose it, and has been looking at the 1.3 default since upgrading, so sending
        /// them anywhere else now would be a second unrequested change. The rule costs the few
        /// who picked one of those names deliberately, which is the right way round.
        ///
        /// The rest are matched on what the icon shows. Where two old names mean one new icon -
        /// Pig and Boar, Tombstone and Gravestone - both are mapped.
        private static readonly Dictionary<string, int> Mapped =
            new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            // The 1.2.x category defaults, pinned to their 1.3 counterparts.
            { "MineFace", 119 },        // Ore     -> generic ore chunk
            { "CobwebArch", 39 },       // Dungeon -> stairs down
            { "Shrine", 42 },           // Camp    -> village
            { "Bee", 78 },              // Beehive -> beehive
            { "TreasureChest", 72 },    // Chest   -> chest
            { "Nest", 9 },              // Spawner -> summoning circle
            { "Coins", 88 },            // Trader  -> coins
            { "SerpentHead", 50 },      // Leviathan -> leviathan
            { "Ember", 85 },            // Wisp    -> star

            // Redrawn in the new sheet.
            { "Bed", 70 },
            { "Boar", 138 },
            { "Pig", 138 },
            { "SkullAndBones", 80 },
            { "Skull", 80 },
            { "TrollSkull", 21 },
            { "Crystal", 117 },
            { "Campfire", 74 },
            { "Castle", 40 },
            { "Fort", 40 },
            { "TowerDoor", 40 },
            { "LockedTower", 40 },
            { "FortressAssault", 36 },
            { "Golem", 28 },
            { "DragonEgg", 8 },
            { "DrakeHead", 8 },
            { "Pickaxe", 119 },
            { "OreCluster", 119 },
            { "Boulder", 119 },
            { "Longship", 150 },
            { "SailingBoat", 151 },
            { "DvergrHead", 37 },
            { "StandingStone", 57 },
            { "Berries", 90 },
            { "House", 43 },
            { "LogCabin", 43 },
            { "Village", 42 },
            { "FulingFace", 3 },
            { "TotemPole", 3 },
            { "Banner", 4 },
            { "OpenChest", 152 },
            { "MysteryBox", 84 },
            { "Tombstone", 2 },
            { "Gravestone", 2 },
            { "WizardHat", 62 },
            { "SkeletalHand", 1 },
            { "SwordAndSpear", 52 },
            { "SwordAndShovel", 52 },
            { "SinkingShip", 47 },
            { "Tick", 26 },
            { "GiantSkull", 51 },
            { "MineTower", 34 },
            { "Antlers", 137 },
            { "SeekerHead", 5 },
            { "Door", 39 },
            { "StoneArch", 134 },
            { "BrickWall", 134 },
            { "CrossedSticks", 0 },
            { "Blob", 16 },
            { "Talon", 12 },
            { "Arrowhead", 108 },
            { "Swimmer", 136 },
            { "DiveArrow", 86 },
            { "Tree", 53 },
            { "MushroomTree", 95 },
            { "Leaf", 107 },
            { "Sprout", 109 },
            { "Pillars", 45 },
            { "Imp", 27 },
            { "Fish", 139 },
            { "FishBones", 139 },
            { "Hide", 145 },
            { "Rubble", 127 },
            { "Island", 55 },
        };

        /// 1.2.x's sheet, by name and index, for the ones the new sheet has no answer for. Kept
        /// as their own icon rather than nudged onto something that merely looks nearby: a
        /// chicken is not a deer, and a wrong pin is worse than an old-looking one.
        private static readonly Dictionary<string, int> KeptOld =
            new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "Dot", 0 },
            { "Bell", 14 },
            { "Tent", 13 },
            { "Chicken", 19 },
            { "Signpost", 26 },
            { "SignBoard", 82 },
            { "Chalice", 44 },
            { "NeedleAndThread", 60 },
            { "Armour", 78 },
            { "Canoe", 81 },
        };

        /// Reverse of KeptOld, so a value written back to the config file reads as the name it came
        /// in as instead of a bare number. Mapped values are ordinary PinIcons and write themselves.
        private static readonly Dictionary<int, string> LegacyNames = BuildLegacyNames();

        private static Dictionary<int, string> BuildLegacyNames()
        {
            var byValue = new Dictionary<int, string>();
            foreach (KeyValuePair<string, int> kv in KeptOld)
                byValue[LegacyBase + kv.Value] = kv.Key;
            return byValue;
        }

        /// The PinIcon a 1.2.x name becomes, or null when the name is not one of 1.2.x's.
        public static PinIcon? Translate(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (Mapped.TryGetValue(name, out int current))
                return (PinIcon)current;
            if (KeptOld.TryGetValue(name, out int legacy))
                return (PinIcon)(LegacyBase + legacy);
            return null;
        }

        /// The name to write for a value, or null when it is an ordinary PinIcon that can write
        /// itself.
        public static string NameFor(PinIcon icon) =>
            LegacyNames.TryGetValue((int)icon, out string name) ? name : null;

        /// What the config file said before anything was bound, keyed "Section/Key".
        private static readonly Dictionary<string, string> Raw =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        /// Reads the config file as text, before the first Bind.
        ///
        /// A TomlTypeConverter would be the obvious hook and it does not work: GetConverter
        /// answers every enum with the one converter registered for System.Enum and never looks
        /// up the specific type, so a converter added for PinIcon is never consulted - and
        /// AddConverter refuses it anyway, because CanConvert already answers true. Reading the
        /// file is the way left that uses only public API.
        ///
        /// Must run before the first Bind: ConfigFile.Bind writes the file back, replacing an
        /// unparseable value with the default. After that this would see 1.3's defaults and
        /// translate nothing.
        public static void Capture(string configPath)
        {
            Raw.Clear();
            if (string.IsNullOrEmpty(configPath) || !System.IO.File.Exists(configPath))
                return;

            try
            {
                string section = string.Empty;
                foreach (string line in System.IO.File.ReadAllLines(configPath))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text[0] == '#')
                        continue;
                    if (text[0] == '[' && text[text.Length - 1] == ']')
                    {
                        section = text.Substring(1, text.Length - 2);
                        continue;
                    }

                    int eq = text.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    Raw[section + "/" + text.Substring(0, eq).Trim()] = text.Substring(eq + 1).Trim();
                }
            }
            catch (System.Exception e)
            {
                // Not fatal: the old names just fall back to defaults, as in 1.3.0 through 1.3.2.
                Plugin.Log.LogWarning($"Could not read the config file to carry 1.2.2's icon names over: {e.Message}");
            }
        }

        /// Puts a 1.2.x name back into a setting BepInEx has just reset to its default.
        ///
        /// Called straight after each icon is bound. The entry holds the default by then, so the
        /// value captured from the file is the only record of what was chosen.
        public static void Adopt(ConfigEntry<PinIcon> entry)
        {
            if (entry == null)
                return;
            string key = entry.Definition.Section + "/" + entry.Definition.Key;
            if (!Raw.TryGetValue(key, out string raw))
                return;

            PinIcon? translated = Translate(raw);
            if (!translated.HasValue || entry.Value == translated.Value)
                return;

            entry.Value = translated.Value;
            Plugin.Log.LogInfo($"{key}: '{raw}' is from 1.2.2's sheet - carried over as {Describe(translated.Value)}.");
        }

        private static string Describe(PinIcon icon) =>
            (int)icon >= LegacyBase
                ? $"its original icon from the old sheet ({NameFor(icon)})"
                : icon.ToString();
    }
}
