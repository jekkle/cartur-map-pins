using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    public enum PinCategory
    {
        Ore,
        Dungeon,
        Camp,
        BossAltar,
        Beehive,
        Pickable,
        Runestone,
        Chest,
        Spawner,
        Leviathan,
        Trader,
        Wisp
    }

    /// Which bucket a pickable falls into. Pickables are by far the most numerous thing in the
    /// world, so they're grouped and toggled separately from everything else.
    public enum PickableGroup
    {
        HighValue,   // surtling cores, Yggdrasil shoots, eggs
        Berries,     // berry bushes
        Mushrooms,   // mushrooms, Jotun puffs, magecap - separate icon from berries
        Crops,       // thistle, dandelion, seeds, barley, flax
        Junk,        // branches, stones, flint - would carpet the map
        Other        // anything unrecognised; logged once so it can be classified later
    }

    /// Prefab-hash -> category lookup, built once at runtime.
    ///
    /// Deliberately classifies by COMPONENT PRESENCE (and, for ore, by what a node drops) rather
    /// than by prefab name: not one ore/pickable/beehive prefab name exists as a string literal
    /// anywhere in assembly_valheim.dll (they're Unity asset references), so a hardcoded name
    /// list is impossible to derive from the game and would rot on every update. Reading the live
    /// prefab list instead is self-maintaining, and picks up modded content for free.
    internal static class PinCatalog
    {
        private static readonly Dictionary<int, PinCategory> ByHash = new Dictionary<int, PinCategory>();
        private static readonly Dictionary<int, PickableGroup> PickableGroups = new Dictionary<int, PickableGroup>();

        /// Ore type per prefab hash ("Copper", "Tin", ...) - drives both the label and the
        /// per-ore-type toggle, and keys dedupe so a copper pin can't suppress nearby tin.
        private static readonly Dictionary<int, string> OreTypes = new Dictionary<int, string>();

        public static bool Built { get; private set; }
        public static int Size => ByHash.Count;

        public static bool TryGet(int prefabHash, out PinCategory category) =>
            ByHash.TryGetValue(prefabHash, out category);

        public static bool Contains(int hash) => ByHash.ContainsKey(hash);

        public static PickableGroup GroupOf(int prefabHash) =>
            PickableGroups.TryGetValue(prefabHash, out PickableGroup g) ? g : PickableGroup.Other;

        public static string OreTypeOf(int prefabHash) =>
            OreTypes.TryGetValue(prefabHash, out string t) ? t : null;

        /// Diagnostics only.
        public static readonly Dictionary<PinCategory, List<string>> NamesByCategory =
            new Dictionary<PinCategory, List<string>>();
        public static readonly Dictionary<string, string> OreQualifiedBy = new Dictionary<string, string>();

        /// Drop-item name fragment -> the ore type shown on the map and used for its toggle.
        /// This list is both the detector and the source of the per-type config entries, so a
        /// type can't exist in one without the other.
        public static readonly (string Token, string Type)[] OreTokens =
        {
            ("copperore",  "Copper"),
            ("tinore",     "Tin"),
            ("ironore",    "Iron"),
            ("ironscrap",  "Iron"),
            ("silverore",  "Silver"),
            ("obsidian",   "Obsidian"),
            ("flametal",   "Flametal"),
            ("sulfur",     "Sulfur"),
            ("blackmarble", "Black Marble"),
            ("softtissue", "Giant Remains"),
            ("chitin",     "Chitin"),
            ("tar",        "Tar"),
        };

        public static void Build(ZNetScene scene)
        {
            ByHash.Clear();
            PickableGroups.Clear();
            OreTypes.Clear();
            NamesByCategory.Clear();
            OreQualifiedBy.Clear();

            if (scene == null || scene.m_prefabs == null)
                return;

            var counts = new Dictionary<PinCategory, int>();

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;

                if (!TryClassify(prefab, out PinCategory category))
                    continue;

                int hash = prefab.name.GetStableHashCode();
                ByHash[hash] = category;
                counts.TryGetValue(category, out int n);
                counts[category] = n + 1;

                if (!NamesByCategory.TryGetValue(category, out List<string> names))
                {
                    names = new List<string>();
                    NamesByCategory[category] = names;
                }
                names.Add(prefab.name);

                if (category == PinCategory.Pickable)
                    PickableGroups[hash] = ClassifyPickable(prefab);
            }

            Built = true;

            foreach (KeyValuePair<PinCategory, int> kv in counts)
                Plugin.Log.LogInfo($"Catalog: {kv.Value} {kv.Key} prefabs");
        }

        private static bool TryClassify(GameObject prefab, out PinCategory category)
        {
            // Ordered so that the more specific components win: a boss altar or runestone can
            // sit on an object that also matches something broader.
            if (prefab.GetComponent<OfferingBowl>() != null)
            {
                category = PinCategory.BossAltar;
                return true;
            }
            if (prefab.GetComponent<Vegvisir>() != null || prefab.GetComponent<RuneStone>() != null)
            {
                category = PinCategory.Runestone;
                return true;
            }
            if (prefab.GetComponent<Leviathan>() != null)
            {
                category = PinCategory.Leviathan;
                return true;
            }
            if (prefab.GetComponent<Trader>() != null)
            {
                category = PinCategory.Trader;
                return true;
            }
            if (prefab.GetComponent<WispSpawner>() != null)
            {
                category = PinCategory.Wisp;
                return true;
            }
            if (prefab.GetComponent<SpawnArea>() != null || prefab.GetComponent<CreatureSpawner>() != null)
            {
                category = PinCategory.Spawner;
                return true;
            }

            // Loot containers. The wild/player-built distinction can't be made here (it's per
            // instance, from the ZDO creator), so that check happens at pin time.
            Container container = prefab.GetComponent<Container>();
            if (container != null && container.m_defaultItems?.m_drops != null &&
                container.m_defaultItems.m_drops.Count > 0)
            {
                category = PinCategory.Chest;
                return true;
            }

            // Ore is classified by WHAT IT DROPS, not by component type.
            //
            // The obvious test - "has MineRock5 or MineRock" - is wrong twice over: the Black
            // Forest copper deposit (`rock4_copper`) is a plain Destructible with neither
            // component, while the prefabs that *do* carry MineRock are mostly destruction
            // debris (cliff_ashlands1_frac, mudpile_frac, Rock_3_frac...).
            if (!LooksLikeFragment(prefab.name) && YieldsOre(prefab, out string via, out string oreType))
            {
                category = PinCategory.Ore;
                OreQualifiedBy[prefab.name] = via;
                OreTypes[prefab.name.GetStableHashCode()] = oreType;
                return true;
            }

            if (prefab.GetComponent<Beehive>() != null)
            {
                category = PinCategory.Beehive;
                return true;
            }
            if (prefab.GetComponent<Pickable>() != null || prefab.GetComponent<PickableItem>() != null)
            {
                category = PinCategory.Pickable;
                return true;
            }

            category = default;
            return false;
        }

        /// Destruction-debris prefabs carry the same mining components as real deposits but are
        /// spawned only when a node shatters, so they must never be pinned.
        private static bool LooksLikeFragment(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("_frac") || n.Contains("_destruction") || n.Contains("fractured");
        }

        private static bool YieldsOre(GameObject prefab, out string via, out string oreType, int depth = 0)
        {
            via = null;
            oreType = null;

            var tables = new List<DropTable>();
            MineRock5 rock5 = prefab.GetComponent<MineRock5>();
            if (rock5?.m_dropItems != null)
                tables.Add(rock5.m_dropItems);
            MineRock rock = prefab.GetComponent<MineRock>();
            if (rock?.m_dropItems != null)
                tables.Add(rock.m_dropItems);
            DropOnDestroyed dropOnDestroyed = prefab.GetComponent<DropOnDestroyed>();
            if (dropOnDestroyed?.m_dropWhenDestroyed != null)
                tables.Add(dropOnDestroyed.m_dropWhenDestroyed);

            foreach (DropTable table in tables)
            {
                if (table.m_drops == null)
                    continue;
                foreach (DropTable.DropData drop in table.m_drops)
                {
                    if (drop.m_item == null)
                        continue;
                    string itemName = drop.m_item.name.ToLowerInvariant();
                    foreach ((string token, string type) in OreTokens)
                    {
                        if (itemName.Contains(token))
                        {
                            via = drop.m_item.name;
                            oreType = type;
                            return true;
                        }
                    }
                }
            }

            // A big deposit often doesn't drop ore itself - it spawns a mineable chunk that does
            // (Destructible.m_spawnWhenDestroyed). Follow that one level so the parent deposit,
            // which is the thing you actually see and want pinned, still qualifies.
            if (depth == 0)
            {
                Destructible destructible = prefab.GetComponent<Destructible>();
                GameObject spawned = destructible?.m_spawnWhenDestroyed;
                if (spawned != null && YieldsOre(spawned, out string innerVia, out string innerType, depth + 1))
                {
                    via = $"{innerVia} (via {spawned.name})";
                    oreType = innerType;
                    return true;
                }
            }

            return false;
        }

        /// Grouped by what the pickable YIELDS rather than by its prefab name, so a renamed or
        /// modded bush still lands in the right bucket.
        private static PickableGroup ClassifyPickable(GameObject prefab)
        {
            string haystack = prefab.name.ToLowerInvariant();

            Pickable pickable = prefab.GetComponent<Pickable>();
            if (pickable != null && pickable.m_itemPrefab != null)
            {
                haystack += " " + pickable.m_itemPrefab.name.ToLowerInvariant();
                ItemDrop drop = pickable.m_itemPrefab.GetComponent<ItemDrop>();
                if (drop != null && drop.m_itemData?.m_shared != null)
                    haystack += " " + drop.m_itemData.m_shared.m_name.ToLowerInvariant();
            }

            if (ContainsAny(haystack, "surtlingcore", "yggdrasil", "egg"))
                return PickableGroup.HighValue;
            if (ContainsAny(haystack, "mushroom", "jotunpuff", "magecap", "smokepuff"))
                return PickableGroup.Mushrooms;
            if (ContainsAny(haystack, "raspberr", "blueberr", "cloudberr", "berry", "berries"))
                return PickableGroup.Berries;
            if (ContainsAny(haystack, "thistle", "dandelion", "seed", "barley", "flax", "carrot", "turnip", "onion"))
                return PickableGroup.Crops;
            if (ContainsAny(haystack, "branch", "wood", "stone", "flint", "feather", "resin"))
                return PickableGroup.Junk;

            return PickableGroup.Other;
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            foreach (string n in needles)
            {
                if (haystack.Contains(n))
                    return true;
            }
            return false;
        }
    }
}
