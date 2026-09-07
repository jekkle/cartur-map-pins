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

        public static bool TryGet(int prefabHash, out PinCategory category) =>
            ByHash.TryGetValue(prefabHash, out category);

        public static PickableGroup GroupOf(int prefabHash) =>
            PickableGroups.TryGetValue(prefabHash, out PickableGroup g) ? g : PickableGroup.Other;

        public static void Build(ZNetScene scene)
        {
            ByHash.Clear();
            PickableGroups.Clear();

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
            // MineRock5 is current; MineRock is the legacy component still on some deposits.
            if (prefab.GetComponent<MineRock5>() != null || prefab.GetComponent<MineRock>() != null)
            {
                category = PinCategory.Ore;
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
