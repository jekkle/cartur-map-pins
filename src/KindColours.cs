using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    /// The colour each kind of pin is given when "New default colours" is on: the colour of the
    /// thing it stands for. Ores and berries are the tables Subtypes already holds (copper brown,
    /// raspberry red); the rest follow the same idea, from how each thing looks in the game.
    ///
    /// A starting set for the owner to approve and correct (the Hugin menu shows every one, and
    /// each is a setting per kind: Kind Colours / <kind>, hex RRGGBB, empty for none). Where the
    /// real object is dark, the colour is lifted so the pin does not become a hole on the map, as
    /// the ore colours already are.
    ///
    /// Keyed by the kind's name as the settings know it. A pin with no kind falls back to its
    /// category name (Chest, Beehive, ...), and a pickable group to the group name.
    internal static class KindColours
    {
        public static readonly Dictionary<string, string> Table = Build();

        private static Dictionary<string, string> Build()
        {
            var t = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in Subtypes.OreColours) t[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, string> kv in Subtypes.BerryColours) t[kv.Key] = kv.Value;

            // Pickables, by group where no plant has its own
            t["Mushroom"] = "D94B3C"; t["Yellow Mushroom"] = "E8D44D"; t["Magecap"] = "6FA8F0";
            t["Jotun Puffs"] = "D8C8E6"; t["Smoke Puff"] = "A8A8A8";
            t["HighValue"] = "F28A2E"; t["Berries"] = "C45C8A"; t["Mushrooms"] = "D94B3C";
            t["Crops"] = "86C46B"; t["Junk"] = "8A8A8A";

            // Dungeons
            t["Crypt"] = "8C8F94"; t["Sunken Crypt"] = "6F9A68"; t["Frost Cave"] = "9FD4EE";
            t["Troll Cave"] = "9A7A58"; t["Infested Mine"] = "9A7BC4"; t["Infested Citadel"] = "9A7BC4";
            t["Hildir Crypt"] = "C9473A"; t["Hildir Cave"] = "C9473A"; t["Putrid Hole"] = "8FA55A";
            t["Gates of Morkhalla"] = "A5604A"; t["The Darkest Hole"] = "7A7A96"; t["Winding Tunnels"] = "8A6C48";
            t["Bear Cave"] = "8A6C48"; t["Place of Mystery"] = "C9A0FF";

            // Camps and villages
            t["Fuling Village"] = "D98A3B"; t["Greydwarf Camp"] = "6FA55A"; t["Charred Fortress"] = "E0662A";
            t["Ashlands Ruin"] = "A5705A"; t["Abandoned Village"] = "B8A27C"; t["Abandoned Farm"] = "B8A27C";
            t["Deep North Village"] = "BFD7EA"; t["Hildir Fortress"] = "C9473A"; t["Hildir Camp"] = "C9473A";

            // Bosses and minibosses
            t["Eikthyr"] = "7FD0FF"; t["The Elder"] = "7FBF6A"; t["Bonemass"] = "A9B85A"; t["Moder"] = "BFE6F5";
            t["Yagluth"] = "FF7A2E"; t["The Queen"] = "A98BE6"; t["Fader"] = "E0524A"; t["Kall Fimbulbringer"] = "BFE6F5";
            t["Memorial Site"] = "C8C8C8"; t["Brenna"] = "E0662A"; t["Zil & Thungr"] = "8FB0E0";
            t["Geirrhafa"] = "9FD4EE"; t["Fallen Valkyrie"] = "EDE8D4"; t["Lord Reto"] = "E0662A";

            // Traders
            t["Haldor"] = "E8C25A"; t["Hildir"] = "C9473A"; t["Bog Witch"] = "7FBF6A";

            // Landmarks and the rest of the world
            t["Shipwreck"] = "9A7A58"; t["Ship Setting"] = "B8A27C"; t["Swamp Hut"] = "8A7A52"; t["Well"] = "7FB0E0";
            t["Dolmen"] = "A8A090"; t["Stone Circle"] = "A8A090"; t["Stone House"] = "A8A090"; t["Abandoned House"] = "B8A27C";
            t["Log Cabin"] = "9A7A58"; t["Deep North Hut"] = "BFD7EA"; t["Fire Geyser"] = "E8562A"; t["Tar Pit"] = "8A8274";
            t["Sulfur Arch"] = "E8D44D"; t["Viaduct"] = "A8A090"; t["Harbour"] = "9A7A58"; t["Giant Sword"] = "E0D2AC";
            t["Giant Skull"] = "E0D2AC"; t["Dvergr Excavation"] = "9A7BC4"; t["Lighthouse"] = "E8D44D";
            t["Dvergr Tower"] = "9A7BC4"; t["Petrified Troll"] = "9A9A9A"; t["Lava Leviathan"] = "E8562A";
            t["Ancient Upgrade Station"] = "C9A0FF"; t["Infested Tree"] = "8FA55A";
            t["Stone Tower Ruin"] = "A8A090"; t["Stone Tower"] = "A8A090"; t["Buried Chest"] = "D9A840"; t["Maypole"] = "D95B4A";

            // Spawners, by the creature
            t["Greydwarf Shaman"] = "7FBF6A"; t["Greydwarf"] = "6FA55A"; t["Skeleton"] = "EDE8D4"; t["Draugr"] = "9AB394";
            t["Fuling"] = "D98A3B"; t["Seeker"] = "9A7BC4"; t["Surtling"] = "F28A2E"; t["Charred"] = "E0662A";
            t["Drake"] = "BFE6F5"; t["Abomination"] = "8FA55A"; t["Morgen"] = "A5705A"; t["Volture"] = "9A8A7A";
            t["Tar Blob"] = "8A8274"; t["Growth"] = "9AC96A"; t["Boar"] = "9A6C48"; t["Fenring"] = "C8CED8";
            t["Troll"] = "7FA58A"; t["Wolf"] = "A8AEB8"; t["Ulv"] = "A8AEB8"; t["Deathsquito"] = "D0D86A";
            t["Lox"] = "9A7A58"; t["Gjall"] = "C9A0FF"; t["Tick"] = "9A8A7A"; t["Wraith"] = "BFE6F5";
            t["Stone Golem"] = "9A9A9A"; t["Serpent"] = "6FA8D0";

            // Categories with no kinds
            t["Portal"] = "8FA0FF"; t["Chest"] = "D9A840"; t["Beehive"] = "F2C33D"; t["Runestone"] = "7FB0E0"; t["LoreStone"] = "7FB0E0";
            t["Leviathan"] = "5FA0D0"; t["Wisp"] = "9CE8FF"; t["Home"] = "EDE0B8";
            return t;
        }

        /// The key a pin's colour is looked up by.
        public static string KeyFor(PinCategory category, string subtype) =>
            string.IsNullOrEmpty(subtype) ? category.ToString() : subtype;

        /// What a new pin of this kind gets, or null for none: the player's setting for the kind
        /// (Kind Colours), which starts as the table above.
        public static Color? For(PinCategory category, string subtype)
        {
            BepInEx.Configuration.ConfigEntry<string> entry = Plugin.KindColourEntry(KeyFor(category, subtype));
            if (entry == null)
                return null;
            return ColorUtility.TryParseHtmlString("#" + entry.Value.Trim().TrimStart('#'), out Color c) ? c : (Color?)null;
        }

        /// Gives every pin this mod has placed that has no colour of its own the colour of its
        /// kind. A pin the player coloured is left as they made it. Returns how many were changed.
        private static readonly System.Reflection.FieldInfo UpdateRequired =
            HarmonyLib.AccessTools.Field(typeof(Minimap), "m_pinUpdateRequired");

        public static int ApplyToExisting(Dictionary<string, int> pending = null)
        {
            int changed = 0;
            foreach (KeyValuePair<Vector3, string> record in PinRecord.AllPositionsAndKeys())
            {
                if (PinStyles.For(record.Key).Colour != 0)
                    continue;
                string[] parts = (record.Value ?? string.Empty).Split(new[] { ':' }, 2);
                if (parts.Length != 2 || !System.Enum.TryParse(parts[0], out PinCategory category))
                    continue;
                // `pending` holds colours the player has changed in the setup menu but not saved
                // yet, as PinStyles codes (0 = none), so the button applies what is on screen.
                string key = KeyFor(category, parts[1]);
                Color? colour = pending != null && pending.TryGetValue(key, out int code)
                    ? (code == 0 ? (Color?)null : PinStyles.FromCode(code))
                    : For(category, parts[1]);
                if (!colour.HasValue)
                    continue;
                PinStyles.Style style = PinStyles.For(record.Key);
                style.Colour = PinStyles.ToCode(colour.Value);
                PinStyles.Set(record.Key, style);
                changed++;
            }
            if (changed > 0 && Minimap.instance != null)
                UpdateRequired?.SetValue(Minimap.instance, true);
            return changed;
        }
    }
}
