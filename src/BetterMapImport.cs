using System;
using System.Globalization;
using System.Linq;
using BepInEx.Bootstrap;
using UnityEngine;

namespace CarturMapPins
{
    /// Takes over the pins Better Map (xtav1m.BetterMap) left behind, so moving to this mod does not
    /// mean deleting a map full of orange circles and walking the world again.
    ///
    /// Root cause (read from Better Map's source, PinFilters/PinRecord): its pins are ordinary saved
    /// pins on type slots past the vanilla array, so a session without it loads every one as Icon3,
    /// the orange circle. It keeps a list of what it pinned in the character's m_customData
    /// ("BetterMap.pinned", entries "worldUID;category;x;z" joined with '|'), which is the only way
    /// to tell its pins from ones placed by hand.
    ///
    /// A record entry becomes one of ours when a saved pin of the player's own sits on it, on a slot
    /// that is neither vanilla nor ours (read from real saves: 19-29), or on Icon3 where a session
    /// without this mod flattened it, and we hold no record there yet. The bare category record is
    /// written and the pin moved to that category's icon; the placer promotes the record to a kind
    /// when you walk by. Pins placed by hand are not in Better Map's list and are never touched. Its
    /// categories with no counterpart here (Loot, Sap, Portal, giant remains) stay as they are.
    internal static class BetterMapImport
    {
        private const string Key = "BetterMap.pinned";

        public static int Run()
        {
            Player player = Player.m_localPlayer;
            Minimap map = Minimap.instance;
            if (player == null || map == null || ZNet.instance == null)
                return 0;
            if (Chainloader.PluginInfos.Keys.Any(k => k.StartsWith("xtav1m.", StringComparison.Ordinal)))
                return 0;   // Better Map is running and still owns them
            if (!player.m_customData.TryGetValue(Key, out string text) || string.IsNullOrEmpty(text))
                return 0;

            var pins = MinimapAccess.GetPins(map);
            if (pins == null)
                return 0;

            string world = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
            int made = 0, left = 0;
            foreach (string entry in text.Split('|'))
            {
                string[] f = entry.Split(';');
                if (f.Length != 4 || f[0] != world || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int cat)
                    || !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    continue;

                Minimap.PinData pin = pins.FirstOrDefault(p => p != null && p.m_save && p.m_ownerID == 0L && (p.m_type == Minimap.PinType.Icon3 || ((int)p.m_type >= 18 && (int)p.m_type < CustomIcons.FirstCustomType))
                                                              && Mathf.Abs(p.m_pos.x - x) < 1f && Mathf.Abs(p.m_pos.z - z) < 1f);
                if (pin == null)
                    continue;   // the player deleted it, or it is not an orange circle any more

                string mine = Ours(cat, pin.m_name);
                if (mine == null)
                {
                    left++;
                    continue;
                }
                if (!Enum.TryParse(mine, out PinCategory category) || PinRecord.HasCategoryNear(category, pin.m_pos, 2f))
                    continue;

                Plugin.CategorySettings settings = Plugin.SettingsFor(category);
                if (settings == null || !PinRecord.Repoint(map, pin, PinPlacer.IconFor(category, null, settings)))
                    continue;
                PinRecord.Add(mine, pin.m_pos);
                made++;
            }

            if (made + left > 0)
                Plugin.Log.LogInfo($"Better Map import: took over {made} pin(s); {left} of its pins have no kind here and stay as they are.");
            return made;
        }

        /// Better Map's PinCategory (Ore, Forage, Dungeon, Loot, Spawner, Vegvisir, Beehive, Tar, Sap, Boss, Portal,
        /// in that order) as our record key, or null. Ore holds more than ore in Better Map, so it is told apart by
        /// name: real saves carry "Copper Ore", "Scrap Iron", "$item_tinore", "$item_ironscrap" and "Leviathan".
        private static string Ours(int category, string name)
        {
            switch (category)
            {
                case 0:
                    if (name == "Leviathan") return "Leviathan";
                    return name != null && System.Text.RegularExpressions.Regex.IsMatch(name, "ore|scrap|deposit", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "Ore" : null;
                case 1: return "Pickable";
                case 2: return "Dungeon";
                case 4: return "Spawner";
                case 5: return "Runestone";
                case 6: return "Beehive";
                case 7: return "Ore";   // Tar pits are an Ore kind here
                case 9: return "BossAltar";
                default: return null;
            }
        }
    }
}
