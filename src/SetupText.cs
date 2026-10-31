using System.Collections.Generic;

namespace CarturMapPins
{
    /// The hover descriptions in the setup menu. Player-facing text only; where a kind has no line
    /// of its own it gets its category's, and the biome line under it is read from the game.
    ///
    /// Every line here is also a translation key (Words, read by Translations.Build), so the
    /// wizard follows the same carturpins_names_<language>.txt as the map labels. The wizard's
    /// first version drew plain English whatever the player's file said (totya, 2026-10-30).
    internal static class SetupText
    {
        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        {
            // Ores
            { "Copper", "Copper deposits. Copper and tin smelt into bronze, the first metal you need." },
            { "Tin", "Tin ore, found along shorelines and riverbeds. Smelt it with copper for bronze." },
            { "Iron", "Muddy scrap piles, the source of scrap iron." },
            { "Silver", "Silver veins in the Mountains. A Wishbone helps you find them." },
            { "Obsidian", "Obsidian deposits in the Mountains." },
            { "Flametal", "Flametal ore in the Ashlands." },
            { "Sulfur", "Sulfur rocks in the Ashlands." },
            { "Black Marble", "Giant skulls and ruins in the Mistlands, mined for black marble." },
            { "Giant Remains", "Giant remains in the Mistlands, mined for soft tissue." },
            { PinCatalog.GiantArmor, "The fallen giants' swords and helmets in the Mistlands. Mine them with a pickaxe for iron and copper scrap." },

            // Dungeons
            { "Crypt", "Burial chambers in the Black Forest." },
            { "Sunken Crypt", "Swamp crypts behind an iron gate. You need the Swamp key from The Elder. Scrap iron inside." },
            { "Frost Cave", "Caves in the Mountains." },
            { "Troll Cave", "Black Forest caves with a troll inside." },
            { "Infested Mine", "Dvergr mines in the Mistlands, full of Seekers. Black cores inside." },
            { "Infested Citadel", "The Mistlands citadel where The Queen waits." },
            { "Hildir Crypt", "One of Hildir's quest dungeons. Brings back one of her chests." },
            { "Hildir Cave", "One of Hildir's quest dungeons. Brings back one of her chests." },

            // Camps
            { "Fuling Village", "Fuling villages in the Plains: goblins, totems and loot." },
            { "Greydwarf Camp", "Greydwarf camps, the most common place in the Black Forest. Off by default: cleared in seconds and rarely worth a trip back." },
            { "Hildir Fortress", "Hildir's quest fortress. Brings back one of her chests." },
            { "Hildir Camp", "Hildir's own camp, where she trades." },

            // Bosses
            { "Eikthyr", "Where you summon Eikthyr, the first boss." },
            { "The Elder", "Where you summon The Elder." },
            { "Bonemass", "Where you summon Bonemass." },
            { "Moder", "Where you summon Moder." },
            { "Yagluth", "Where you summon Yagluth." },
            { "The Queen", "Where you face The Queen." },
            { "Fader", "Where you summon Fader." },

            // Traders
            { "Haldor", "Haldor the trader." },
            { "Hildir", "Hildir the trader: clothing, and quests for her lost chests." },
            { "Bog Witch", "The Bog Witch, a trader." },
        };

        private static readonly Dictionary<PinCategory, string> Categories = new Dictionary<PinCategory, string>
        {
            { PinCategory.Ore, "An ore deposit. Pins for mined-out deposits can be removed automatically." },
            { PinCategory.Dungeon, "A dungeon entrance. Ticked off on the map once you have cleared it." },
            { PinCategory.Camp, "A camp, village or fortress." },
            { PinCategory.BossAltar, "A boss's summoning place." },
            { PinCategory.Miniboss, "A miniboss." },
            { PinCategory.Landmark, "A landmark with nothing inside it." },
            { PinCategory.Spawner, "A spawner that keeps making creatures until you break it." },
            { PinCategory.Trader, "A trader." },
            { PinCategory.Portal, "Your portals, named with the tag you gave them." },
        };

        private static readonly Dictionary<PickableGroup, string> Groups = new Dictionary<PickableGroup, string>
        {
            { PickableGroup.HighValue, "Surtling cores, eggs, and wild barley and flax." },
            { PickableGroup.Berries, "Berry bushes and mushrooms. Each berry bush gets its berry's colour." },
            { PickableGroup.Crops, "Thistle, dandelion, seeds, carrots, turnips and onions." },
            { PickableGroup.Junk, "Branches, stones and flint. There are a lot of these: they will carpet the map." },
            { PickableGroup.Other, "Anything you can pick that Map Pins does not recognise." },
        };

        /// The wizard's own row names, for rows that are a whole category or group rather than a kind.
        private static readonly Dictionary<PinCategory, string> RowNames = new Dictionary<PinCategory, string>
        {
            { PinCategory.Chest, "Chests" },
            { PinCategory.Beehive, "Wild beehives" },
            { PinCategory.Runestone, "Runestones" },
            { PinCategory.LoreStone, "Lore stones" },
            { PinCategory.Leviathan, "Leviathans" },
            { PinCategory.Wisp, "Wisp fountains" },
            { PinCategory.Prop, "Maypole" },
            { PinCategory.Home, "Your bed (home)" },
            { PinCategory.Portal, "Portals" },
        };

        private static readonly Dictionary<PickableGroup, string> GroupNames = new Dictionary<PickableGroup, string>
        {
            { PickableGroup.HighValue, "Valuable finds" },
            { PickableGroup.Berries, "Berries and mushrooms" },
            { PickableGroup.Crops, "Crops and herbs" },
            { PickableGroup.Junk, "Branches, stones and flint" },
            { PickableGroup.Other, "Anything else you can pick" },
        };

        private const string GiantArmorName = "Giant swords and helmets";

        private static string Key(string kind, object part) => Translations.Prefix + kind + "_" + Translations.Key(part.ToString());

        /// Every English line above as (key, text), for Translations to register and put in the template.
        public static void Words(Dictionary<string, string> into)
        {
            foreach (var kv in Kinds) into[Key("desc", kv.Key)] = kv.Value;
            foreach (var kv in Categories) into[Key("desc_category", kv.Key)] = kv.Value;
            foreach (var kv in Groups) into[Key("desc_group", kv.Key)] = kv.Value;
            foreach (var kv in GroupNames) into[Key("row_group", kv.Key)] = kv.Value;
            into[Key("row", PinCatalog.GiantArmor)] = GiantArmorName;
            foreach (var kv in RowNames)
            {
                into[Key("row", kv.Key)] = kv.Value;
                // The row's description is the category's own config text, read when the keys are built.
                string config = Plugin.SettingsFor(kv.Key)?.Enabled.Description.Description;
                if (!string.IsNullOrEmpty(config))
                    into[Key("desc_row", kv.Key)] = FirstSentence(config);
            }
        }

        public static string KindName(string kind) =>
            kind == PinCatalog.GiantArmor ? Translations.Say(Key("row", kind), GiantArmorName)
            : Labels.Localize(Translations.Token(kind));

        public static string RowName(PinCategory category) => Translations.Say(Key("row", category), RowNames[category]);

        public static string GroupName(PickableGroup group) => Translations.Say(Key("row_group", group), GroupNames[group]);

        public static string For(string kind, PinCategory category) =>
            Kinds.TryGetValue(kind, out string text) ? Translations.Say(Key("desc", kind), text)
            : Categories.TryGetValue(category, out string general) ? Translations.Say(Key("desc_category", category), general)
            : "";

        public static string ForGroup(PickableGroup group) =>
            Groups.TryGetValue(group, out string text) ? Translations.Say(Key("desc_group", group), text) : "";

        /// A category's own settings description, up to its first full stop: the config text is
        /// already written for players, and the rest of it is about defaults, which the menu shows
        /// on its own line.
        public static string ForCategory(PinCategory category, string configText)
        {
            string english = FirstSentence(configText);
            return english.Length == 0 ? "" : Translations.Say(Key("desc_row", category), english);
        }

        private static string FirstSentence(string configText)
        {
            if (string.IsNullOrEmpty(configText))
                return "";
            int stop = configText.IndexOf(". ");
            return stop > 0 ? configText.Substring(0, stop + 1) : configText;
        }
    }
}
