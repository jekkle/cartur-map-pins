using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    public enum PinCategory
    {
        Ore,
        Dungeon,
        BossAltar,
        Beehive,
        Pickable,
        Runestone
    }

    /// Which bucket a pickable falls into. Pickables are by far the most numerous thing in the
    /// world, so they're grouped and toggled separately from everything else.
    public enum PickableGroup
    {
        HighValue,   // surtling cores, Yggdrasil shoots, eggs
        Berries,     // berries + mushrooms
        Crops,       // thistle, dandelion, seeds, barley, flax
        Junk,        // branches, stones, flint, feathers - would carpet the map
        Other        // anything unrecognised; logged once so it can be classified later
    }

    /// Prefab-hash -> category lookup, built once at runtime.
    ///
    /// Deliberately classifies by COMPONENT PRESENCE rather than by prefab name: not one
    /// ore/pickable/beehive prefab name exists as a string literal anywhere in
    /// assembly_valheim.dll (they're Unity asset references), so a hardcoded name list is
    /// impossible to derive from the game and would rot on every update. Reading the live
    /// prefab list instead is self-maintaining, and picks up modded content for free.
    internal static class PinCatalog
    {
        private static readonly Dictionary<int, PinCategory> ByHash = new Dictionary<int, PinCategory>();
        private static readonly Dictionary<int, PickableGroup> PickableGroups = new Dictionary<int, PickableGroup>();

        public static bool Built { get; private set; }

        public static int Size => ByHash.Count;

        public static bool TryGet(int prefabHash, out PinCategory category) =>
            ByHash.TryGetValue(prefabHash, out category);

        public static PickableGroup GroupOf(int prefabHash) =>
            PickableGroups.TryGetValue(prefabHash, out PickableGroup g) ? g : PickableGroup.Other;

        /// Diagnostics only: prefab names per category, so a probe can report what actually got
        /// registered rather than just a count.
        public static readonly Dictionary<PinCategory, List<string>> NamesByCategory =
            new Dictionary<PinCategory, List<string>>();

        public static bool Contains(int hash) => ByHash.ContainsKey(hash);

        public static void Build(ZNetScene scene)
        {
            ByHash.Clear();
            PickableGroups.Clear();
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
            // Boss altars and runestones are checked first: they live inside location prefabs
            // and would otherwise never be reached.
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
            // Ore is classified by WHAT IT DROPS, not by component type.
            //
            // The obvious test - "has MineRock5 or MineRock" - is wrong twice over: the Black
            // Forest copper deposit (`rock4_copper`) is a plain Destructible with neither
            // component, while the prefabs that *do* carry MineRock are mostly destruction
            // debris (cliff_ashlands1_frac, mudpile_frac, Rock_3_frac...). That test registered
            // 58 "ore" prefabs and matched no actual deposit.
            if (!LooksLikeFragment(prefab.name) && YieldsOre(prefab, out string via))
            {
                category = PinCategory.Ore;
                OreQualifiedBy[prefab.name] = via;
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

        /// Which dropped item caused a prefab to be treated as ore - diagnostics, so the catalog
        /// dump is verifiable rather than a bare list of names.
        public static readonly Dictionary<string, string> OreQualifiedBy = new Dictionary<string, string>();

        /// Item prefab-name fragments that mark a drop as "ore worth pinning".
        private static readonly string[] OreDropTokens =
        {
            "copperore", "tinore", "ironore", "silverore", "ironscrap",
            "obsidian", "flametal", "sulfur", "blackmarble", "softtissue"
        };

        /// Destruction-debris prefabs carry the same mining components as real deposits but are
        /// spawned only when a node shatters, so they must never be pinned.
        private static bool LooksLikeFragment(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("_frac") || n.Contains("_destruction") || n.Contains("fractured");
        }

        private static bool YieldsOre(GameObject prefab, out string via, int depth = 0)
        {
            via = null;

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
                    foreach (string token in OreDropTokens)
                    {
                        if (itemName.Contains(token))
                        {
                            via = drop.m_item.name;
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
                if (spawned != null && YieldsOre(spawned, out string innerVia, depth + 1))
                {
                    via = $"{innerVia} (via {spawned.name})";
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
            if (ContainsAny(haystack, "raspberr", "blueberr", "cloudberr", "mushroom", "jotunpuff", "magecap", "smokepuff"))
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
