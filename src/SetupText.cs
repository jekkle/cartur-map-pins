using System.Collections.Generic;

namespace CarturMapPins
{
    /// The hover descriptions in the setup menu. Player-facing text only; where a kind has no line
    /// of its own it gets its category's, and the biome line under it is read from the game.
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

        public static string For(string kind, PinCategory category) =>
            Kinds.TryGetValue(kind, out string text) ? text
            : Categories.TryGetValue(category, out string general) ? general
            : "";

        public static string ForGroup(PickableGroup group) =>
            Groups.TryGetValue(group, out string text) ? text : "";

        /// A category's own settings description, up to its first full stop: the config text is
        /// already written for players, and the rest of it is about defaults, which the menu shows
        /// on its own line.
        public static string ForCategory(PinCategory category, string configText)
        {
            if (string.IsNullOrEmpty(configText))
                return "";
            int stop = configText.IndexOf(". ");
            return stop > 0 ? configText.Substring(0, stop + 1) : configText;
        }
    }
}
