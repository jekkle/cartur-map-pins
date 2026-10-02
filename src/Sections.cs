using System.Collections.Generic;
using BepInEx.Configuration;

namespace CarturMapPins
{
    /// What each settings section is called on screen, and how to carry a player's old values
    /// across when that name changes.
    ///
    /// Configuration Manager sorts sections alphabetically and offers no way to say otherwise, so
    /// the only lever on order is the name itself. Unnumbered, the first thing anybody saw was
    /// "Beehive", "General" sat fourteenth between "Dungeon Types" and "Home", and the three parts
    /// of Tracking were at positions 2, 36 and 37. Numbering is therefore load-bearing, not
    /// decoration. Two digits and a zero, because "10" sorts before "2" otherwise.
    ///
    /// Sections are renamed and regrouped; NO key is renamed and no two sections are merged. That
    /// is deliberate and the reason is already written in this codebase: a section and key pair
    /// carries one type of value, and "Ore Types/Copper" is a bool while "Ore Icons/Copper" is an
    /// icon. Merging those would have read somebody's saved icon as a true/false, failed, and
    /// silently reset it. So the switches and the icons stay apart and are named to sort together
    /// instead.
    ///
    /// The migration is what stops the rename wiping everybody's settings. BepInEx keys a setting
    /// by (section, key): rename the section and it binds a fresh entry at its default and leaves
    /// the old line orphaned in the file. Read the file before any binding, then fill in from the
    /// old name anything the new name has no value for.
    internal static class Sections
    {
        /// Logical name - what the code calls a section - to what it is called on screen. A few
        /// entries are keyed "Section/Key" because General has been split across five pages and
        /// those keys each go somewhere different; the bare "General" entry catches the rest.
        private static readonly Dictionary<string, string> Display =
            new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "General/ApplyPreset",         "01. Start Here" },
            { "General/PinSharing",          "03. Multiplayer" },
            { "General/DiscoveryRadius",     "11. Advanced" },
            { "General/ScanIntervalSeconds", "11. Advanced" },
            { "General/ResetPins",           "13. Danger Zone" },
            { "General",                     "02. Map Display" },

            // Each of these gets its own page rather than sharing "11. Advanced" with General.
            // Not cosmetic: the migration maps a display name back to ONE old section, so three
            // sources feeding one page would have silently dropped two of them.
            { "CustomIcons",  "11a. Custom Icons" },
            { "Diagnostics",  "11b. Diagnostics" },

            { "Ore",          "04. Ores" },
            { "Ore Types",    "04a. Ore Switches" },
            { "Ore Icons",    "04b. Ore Icons" },

            { "Pickables",       "05. Pickables" },
            { "Pickable Kinds",  "05a. Pickable Switches" },
            { "Pickable Icons",  "05b. Pickable Icons" },

            { "Dungeon",        "06. Dungeons" },
            { "Dungeon Kinds",  "06a. Dungeon Switches" },
            { "Dungeon Types",  "06b. Dungeon Icons" },
            { "Dungeon Tick",   "06c. Dungeon Tick Switches" },

            { "Camp",        "07. Camps" },
            { "Camp Kinds",  "07a. Camp Switches" },
            { "Camp Types",  "07b. Camp Icons" },

            { "Landmark",        "08. Landmarks" },
            { "Landmark Kinds",  "08a. Landmark Switches" },
            { "Landmark Icons",  "08b. Landmark Icons" },
            { "Runestone",       "08c. Runestones" },
            { "LoreStone",       "08d. Lore Stones" },
            { "Leviathan",       "08e. Leviathans" },
            { "Prop",            "08f. Props" },
            { "Prop Kinds",      "08g. Prop Switches" },
            { "Prop Icons",      "08h. Prop Icons" },

            { "Spawner",        "09. Spawners" },
            { "Spawner Kinds",  "09a. Spawner Switches" },
            { "Spawner Icons",  "09b. Spawner Icons" },
            { "Miniboss",       "09c. Minibosses" },
            { "Miniboss Kinds", "09d. Miniboss Switches" },
            { "Miniboss Icons", "09e. Miniboss Icons" },
            { "BossAltar",      "09f. Boss Altars" },
            { "Boss Kinds",     "09g. Boss Switches" },
            { "Boss Icons",     "09h. Boss Icons" },

            { "Chest",             "10. Chests" },
            { "Chest Site Kinds",  "10a. Chest Site Switches" },
            { "Chest Site Icons",  "10b. Chest Site Icons" },
            { "Trader",            "10c. Traders" },
            { "Trader Kinds",      "10d. Trader Switches" },
            { "Trader Icons",      "10e. Trader Icons" },
            { "Beehive",           "10f. Beehives" },
            { "Wisp",              "10g. Wisps" },
            { "Home",              "10h. Home" },

            { "Tracking",    "12. Tracking" },
            { "Boat Icons",  "12a. Boat Icons" },
            { "Tame Icons",  "12b. Tame Icons" },
        };

        /// Display name back to the logical name it came from, for the migration. Built from the
        /// table above so the two cannot drift; several display names share one source, which is
        /// fine - General split five ways, and a key missing from the old section simply is not
        /// adopted.
        private static readonly Dictionary<string, string> Origin =
            new Dictionary<string, string>(System.StringComparer.Ordinal);

        /// Every "section/key" as it stood in the config file before anything was bound.
        private static readonly Dictionary<string, string> Raw =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        static Sections()
        {
            foreach (KeyValuePair<string, string> kv in Display)
            {
                int slash = kv.Key.IndexOf('/');
                string logical = slash > 0 ? kv.Key.Substring(0, slash) : kv.Key;

                // Many display names may come from one old section - General alone splits five
                // ways - but one display name must never come from two, because this map is how
                // the migration finds the old values and it can only hold one answer. Checked
                // rather than trusted: the version of this table that was written first quietly
                // pointed three old sections at one page, and two of them would have reset.
                if (Origin.TryGetValue(kv.Value, out string already))
                {
                    if (already != logical)
                        Plugin.Log.LogWarning(
                            $"Section table: '{kv.Value}' claims both '{already}' and '{logical}'. " +
                            $"'{logical}' will not carry its old values over.");
                    continue;
                }

                Origin[kv.Value] = logical;
            }
        }

        /// The name to show for a section, optionally narrowed by which key is being bound.
        public static string Of(string logical, string key = null)
        {
            if (key != null && Display.TryGetValue(logical + "/" + key, out string byKey))
                return byKey;
            return Display.TryGetValue(logical, out string whole) ? whole : logical;
        }

        /// Reads the config file as text, before BepInEx has bound anything and therefore before
        /// it rewrites the file. Same shape as LegacyIconNames.Capture, and run beside it.
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
                // Losing the migration costs people their settings once. Taking the whole mod
                // down with it costs them the mod, so this is reported and survived.
                Plugin.Log.LogWarning($"Could not read the old config for migration: {e.Message}");
            }
        }

        /// Fills in every setting whose section was renamed, from the value the player had under
        /// the old name.
        ///
        /// Run once, after everything is bound. A setting that already has a value under the new
        /// name is left alone - that is somebody who has used this version, and their current
        /// choice beats a stale line further up the same file.
        ///
        /// SetSerializedValue rather than an assignment: it takes the raw string and runs the
        /// same converters binding would have. There is no converter for the 1.2.2 icon names -
        /// those are handled by LegacyIconNames.Adopt at bind time, under the section they were
        /// bound in - so a value that does not parse here is logged below and left at default.
        public static int Adopt(ConfigFile config)
        {
            if (Raw.Count == 0 || config == null)
                return 0;

            int carried = 0;

            foreach (ConfigDefinition def in new List<ConfigDefinition>(config.Keys))
            {
                if (!Origin.TryGetValue(def.Section, out string old) || old == def.Section)
                    continue;
                if (Raw.ContainsKey(def.Section + "/" + def.Key))
                    continue;
                if (!Raw.TryGetValue(old + "/" + def.Key, out string value))
                    continue;

                try
                {
                    config[def].SetSerializedValue(value);
                    carried++;
                }
                catch (System.Exception e)
                {
                    // One unreadable value must not stop the other two hundred moving.
                    Plugin.Log.LogWarning($"Could not carry {old}/{def.Key} over: {e.Message}");
                }
            }

            return carried;
        }
    }
}
