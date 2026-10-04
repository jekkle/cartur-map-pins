using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CarturMapPins
{
    /// Diagnostics for "why didn't this thing get pinned?". Written because the catalog registered
    /// 58 ore prefabs yet no ore instance matched a live ZDO; only a real object's prefab
    /// name/hash compared against the registered ones could explain that.
    internal static class Probe
    {
        public static void Nearby(Terminal.ConsoleEventArgs args, float radius)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                args.Context?.AddString("No local player.");
                return;
            }

            Vector3 origin = player.transform.position;
            var report = new List<string>();

            // Found by component type rather than prefab name, same as the catalog.
            Inspect<MineRock5>(origin, radius, "MineRock5", report);
            Inspect<MineRock>(origin, radius, "MineRock", report);
            Inspect<Beehive>(origin, radius, "Beehive", report);
            Inspect<Pickable>(origin, radius, "Pickable", report);
            Inspect<Destructible>(origin, radius, "Destructible", report);

            if (report.Count == 0)
            {
                Emit(args, $"No candidate nodes within {radius:F0}m.");
                return;
            }

            Emit(args, $"--- {report.Count} candidate(s) within {radius:F0}m ---");
            foreach (string line in report)
                Emit(args, line);
        }

        private static void Inspect<T>(Vector3 origin, float radius, string label, List<string> report)
            where T : Component
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            foreach (T comp in found)
            {
                if (comp == null)
                    continue;

                Vector3 pos = comp.transform.position;
                float dx = pos.x - origin.x, dz = pos.z - origin.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > radius)
                    continue;

                // The name the game itself would hash: root object, "(Clone)" stripped.
                GameObject root = comp.transform.root.gameObject;
                string rootName = Utils.GetPrefabName(root);
                string ownName = Utils.GetPrefabName(comp.gameObject);

                ZNetView nview = comp.GetComponentInParent<ZNetView>();
                string zdoInfo = "no ZNetView";
                if (nview != null)
                {
                    ZDO zdo = nview.GetZDO();
                    zdoInfo = zdo == null
                        ? "ZNetView but no ZDO"
                        : $"zdoPrefabHash={zdo.GetPrefab()} inCatalog={PinCatalog.Contains(zdo.GetPrefab())}";
                }

                report.Add(
                    $"[{label}] own='{ownName}' root='{rootName}' " +
                    $"ownHashInCatalog={PinCatalog.Contains(ownName.GetStableHashCode())} " +
                    $"rootHashInCatalog={PinCatalog.Contains(rootName.GetStableHashCode())} " +
                    $"{zdoInfo} y={pos.y:F0} dist={dist:F0}m");
            }
        }

        /// Dumps what the catalog actually registered for a category.
        public static void DumpCategory(Terminal.ConsoleEventArgs args, string categoryName)
        {
            foreach (KeyValuePair<PinCategory, List<string>> kv in PinCatalog.NamesByCategory)
            {
                if (!string.IsNullOrEmpty(categoryName) &&
                    !kv.Key.ToString().Equals(categoryName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Emit(args, $"--- {kv.Key}: {kv.Value.Count} prefabs ---");

                // Ore lists what qualified each prefab, so the classification can be sanity
                // checked rather than taken on trust.
                if (kv.Key == PinCategory.Ore)
                {
                    foreach (string n in kv.Value)
                    {
                        string via = PinCatalog.OreQualifiedBy.TryGetValue(n, out string v) ? v : "?";
                        Emit(args, $"    {n}  (drops {via})");
                    }
                    continue;
                }

                var sb = new StringBuilder();
                foreach (string n in kv.Value)
                {
                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append(n);
                    if (sb.Length > 200)
                    {
                        Emit(args, sb.ToString());
                        sb.Length = 0;
                    }
                }
                if (sb.Length > 0)
                    Emit(args, sb.ToString());
            }
        }

        /// Dumps every location the world generator knows about, by exact prefab name.
        ///
        /// Authoritative source for the subtype tables: location prefab names are Unity asset
        /// references, not string literals in assembly_valheim, so they cannot be read offline.
        /// ZoneSystem.m_locations is public, populated on clients too, and includes locations
        /// added by other mods.
        public static void DumpLocations(Terminal.ConsoleEventArgs args)
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null || zs.m_locations == null)
            {
                Emit(args, "ZoneSystem not ready.");
                return;
            }

            Emit(args, $"--- {zs.m_locations.Count} ZoneLocation definitions ---");
            foreach (ZoneSystem.ZoneLocation zl in zs.m_locations)
            {
                if (zl == null)
                    continue;
                string name = !string.IsNullOrEmpty(zl.m_prefabName) ? zl.m_prefabName : "(unnamed)";
                string flags = zl.m_iconAlways ? " iconAlways" : (zl.m_iconPlaced ? " iconPlaced" : "");
                // The game's own name for the place. A prefab name like MorkBorg or FimbulLocation01
                // does not say what a player calls it, and matching artwork needs that.
                string named = !string.IsNullOrEmpty(zl.m_name) ? $" name=\"{Labels.Localize(zl.m_name)}\"" : "";
                Emit(args, $"    {name}  biome={zl.m_biome} quantity={zl.m_quantity}{flags}{named}{Shape(zl)}");
            }
        }

        /// Every prefab the game registers, by name, with the components that decide whether this
        /// mod can pin it.
        ///
        /// The catalog only reports what it accepted; this reports what exists. Prefab names are
        /// Unity asset references, not strings in the assembly, so they cannot be read offline.
        /// This is the list the icon sheet's names have to be matched against.
        public static void DumpAllPrefabs(Terminal.ConsoleEventArgs args)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null)
            {
                Emit(args, "ZNetScene not ready.");
                return;
            }

            Emit(args, $"=== {scene.m_prefabs.Count} ZNetScene prefabs ===");
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;

                var sb = new StringBuilder();
                Note<Piece>(prefab, "piece", sb);
                Note<Container>(prefab, "container", sb);
                Note<Pickable>(prefab, "pickable", sb);
                Note<CreatureSpawner>(prefab, "spawner", sb);
                Note<SpawnArea>(prefab, "spawnarea", sb);
                Note<Character>(prefab, "creature", sb);
                Note<Destructible>(prefab, "destructible", sb);
                Note<MineRock5>(prefab, "minerock5", sb);
                Note<MineRock>(prefab, "minerock", sb);
                Note<Vegvisir>(prefab, "vegvisir", sb);
                Note<RuneStone>(prefab, "runestone", sb);
                Note<OfferingBowl>(prefab, "altar", sb);
                Note<Beehive>(prefab, "beehive", sb);
                Note<Trader>(prefab, "trader", sb);

                bool known = PinCatalog.Contains(prefab.name.GetStableHashCode());
                Emit(args, $"    {prefab.name,-42} {(known ? "PINNED" : "      ")} {sb}");
            }
        }

        private static void Note<T>(GameObject prefab, string label, StringBuilder sb) where T : Component
        {
            if (prefab.GetComponent<T>() == null)
                return;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(label);
        }

        /// Every field of every location definition, written out once.
        ///
        /// The summary line above is a chosen subset (biome, quantity, the two flags the classifier
        /// reads), and each missing field cost a relaunch to add. The 232 definitions do not change
        /// between sessions, so this writes all of it down once: the ZoneLocation's fields and the
        /// Location component's, by reflection so a field added by a game update appears unasked.
        /// Kept in the repo, it is answerable offline.
        public static void DumpLocationsFull(Terminal.ConsoleEventArgs args)
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null || zs.m_locations == null)
            {
                Emit(args, "ZoneSystem not ready.");
                return;
            }

            Emit(args, $"=== every field of {zs.m_locations.Count} ZoneLocation definitions ===");
            foreach (ZoneSystem.ZoneLocation zl in zs.m_locations)
            {
                if (zl == null)
                    continue;

                Emit(args, $"[{(!string.IsNullOrEmpty(zl.m_prefabName) ? zl.m_prefabName : "(unnamed)")}]");
                foreach (System.Reflection.FieldInfo f in typeof(ZoneSystem.ZoneLocation).GetFields())
                    Emit(args, $"    zl.{f.Name} = {Describe(f.GetValue(zl))}");

                Location loc = LoadedLocation(zl, out bool release);
                if (loc == null)
                {
                    Emit(args, "    (location prefab unavailable)");
                    continue;
                }

                try
                {
                    foreach (System.Reflection.FieldInfo f in typeof(Location).GetFields())
                        Emit(args, $"    loc.{f.Name} = {Describe(f.GetValue(loc))}");
                    Emit(args, $"    loc.contents ={Contents(loc)}");
                }
                finally
                {
                    if (release)
                        zl.m_prefab.Release();
                }
            }
        }

        /// A value as one readable line. Unity objects print their name rather than their
        /// ToString, which is "name (Type)" and repeats the type on every field; lists print
        /// their length and members, since a list of spawn groups says more than "List`1".
        private static string Describe(object value)
        {
            if (value == null)
                return "null";
            if (value is string s)
                return s.Length == 0 ? "\"\"" : $"\"{Labels.Localize(s)}\"";
            if (value is Object unityObject)
                return unityObject == null ? "null" : unityObject.name;
            if (value is System.Collections.IEnumerable list && !(value is string))
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (object item in list)
                {
                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append(item is Object o ? o.name : item?.ToString() ?? "null");
                    n++;
                }
                return n == 0 ? "[]" : $"[{n}: {sb}]";
            }
            return value.ToString();
        }

        /// The Location component off a definition's prefab, loading it if the world hasn't.
        /// `release` says whether the caller owes a Release - the same Load/read/Release sequence
        /// ZoneSystem.SpawnLocation uses.
        private static Location LoadedLocation(ZoneSystem.ZoneLocation zl, out bool release)
        {
            release = false;
            if (!zl.m_prefab.IsValid)
                return null;

            if (!zl.m_prefab.IsLoaded)
            {
                if (zl.m_prefab.Load() != SoftReferenceableAssets.LoadResult.Succeeded)
                    return null;
                release = true;
            }

            GameObject asset = zl.m_prefab.Asset;
            return asset != null ? asset.GetComponent<Location>() : null;
        }

        /// The two fields TryClassifyLocation branches on, read off the location prefab itself.
        /// Without them the dump cannot tell a location nobody pins from one pinned on the generic
        /// category icon.
        ///
        /// ZoneLocation.m_prefab is a SoftReference whose Asset getter throws until the prefab is
        /// loaded, so this uses the sequence ZoneSystem.SpawnLocation uses: Load, read, Release.
        /// A location already held by the world stays held; only ones this loads are released.
        private static string Shape(ZoneSystem.ZoneLocation zl)
        {
            if (!zl.m_prefab.IsValid)
                return " kind=? (no soft reference)";

            bool alreadyLoaded = zl.m_prefab.IsLoaded;
            if (!alreadyLoaded)
            {
                SoftReferenceableAssets.LoadResult result = zl.m_prefab.Load();
                // Tells "the bundle would not give it up" from "it has no Location on it".
                if (result != SoftReferenceableAssets.LoadResult.Succeeded)
                    return $" kind=? (load {result})";
            }

            try
            {
                GameObject asset = zl.m_prefab.Asset;
                if (asset == null)
                    return " kind=? (no asset)";
                Location loc = asset.GetComponent<Location>();
                if (loc == null)
                    return " kind=? (no Location component)";

                // What the game prints when you walk into the place, "Winding Church" rather than
                // "MorkBorg". The one field that can match the icon sheet's names to locations.
                string discover = !string.IsNullOrEmpty(loc.m_discoverLabel)
                    ? $" discover=\"{Labels.Localize(loc.m_discoverLabel)}\"" : "";
                string gen = loc.m_generator != null ? $" generator={loc.m_generator.name}" : "";
                string kind = (loc.m_hasInterior ? " kind=dungeon"
                               : loc.m_generator != null ? " kind=camp"
                               : " kind=surface") + gen + discover;
                return kind + Verdict(loc) + Contents(loc);
            }
            finally
            {
                if (!alreadyLoaded)
                    zl.m_prefab.Release();
            }
        }

        /// What a location is built out of: the components the mod pins things by, counted on the
        /// prefab itself.
        ///
        /// "Greydwarf_camp1 is surface, with no generator, and pins nothing" leaves open whether
        /// something inside it is pinned instead, or the camp is invisible on the map entirely.
        /// That cannot be answered from outside the prefab.
        ///
        /// Inactive children are included: a location's spawners are frequently disabled until the
        /// location is placed.
        private static string Contents(Location loc)
        {
            var sb = new StringBuilder();
            Count<CreatureSpawner>(loc, "spawner", sb);
            Count<SpawnArea>(loc, "spawnarea", sb);
            Count<Container>(loc, "container", sb);
            Count<Pickable>(loc, "pickable", sb);
            Count<Vegvisir>(loc, "vegvisir", sb);
            Count<RuneStone>(loc, "runestone", sb);
            Count<BossStone>(loc, "bossstone", sb);
            Count<OfferingBowl>(loc, "altar", sb);
            Count<Beehive>(loc, "beehive", sb);
            return sb.Length > 0 ? "  {" + sb + "}" : "";
        }

        private static void Count<T>(Location loc, string label, StringBuilder sb) where T : Component
        {
            int n = loc.GetComponentsInChildren<T>(includeInactive: true).Length;
            if (n == 0)
                return;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(label).Append('=').Append(n);
        }

        /// What the mod would actually do with this location, asked of the real classifier rather
        /// than worked out again here. Answers the question the rest of the line only hints at:
        /// whether a location is pinned at all, which category claims it, and whether it lands on
        /// a subtype icon or on the category's generic one.
        private static string Verdict(Location loc)
        {
            if (!PinPlacer.TryClassifyLocation(loc, out PinCategory category, out string label, out string subtype))
                return "  -> not pinned";

            Plugin.CategorySettings settings = Plugin.SettingsFor(category);
            if (settings == null)
                return $"  -> {category} (no settings bound)";

            string icon = subtype != null ? $"{category}:{subtype}" : $"{category}, category icon";
            string state = settings.Enabled.Value ? "on" : "OFF";
            // Localized, because that is what the pin carries; "$enemy_eikthyr" cannot be checked
            // against the map.
            return $"  -> {icon} [{state}] \"{Labels.Localize(label)}\"";
        }

        /// Lists the game's TMP font assets by exact name.
        ///
        /// Unrelated to pinning, but nowhere else can answer it: mods that take a font name in
        /// config (e.g. Ammo Count's `AmmoTextFont`) match it exactly against
        /// Resources.FindObjectsOfTypeAll&lt;TMP_FontAsset&gt;(), and a stale name silently yields a
        /// null font, so text renders as nothing while its icon still shows. The names cannot be
        /// read from the shipped tmp_fonts bundle because it is compressed.
        public static void DumpFonts(Terminal.ConsoleEventArgs args)
        {
            TMPro.TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>();
            Emit(args, $"--- {fonts.Length} TMP_FontAsset(s) loaded ---");
            foreach (TMPro.TMP_FontAsset font in fonts)
            {
                if (font != null)
                    Emit(args, $"    TMP font: '{font.name}'");
            }
        }

        /// Every catalogued prefab and the label it would actually get, resolved through the same
        /// code the pinning path uses. This is the coverage check: walking the whole world to see
        /// each label is not a test anyone runs, so ask the catalog instead.
        ///
        /// Only covers categories fed by the spawn hook - Dungeon, Camp and LoreStone come from
        /// ZoneLocations and are listed by carturpins_locations instead.
        public static void DumpLabels(Terminal.ConsoleEventArgs args, string categoryName)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                Emit(args, "No ZNetScene - load into a world first.");
                return;
            }

            foreach (KeyValuePair<PinCategory, List<string>> kv in PinCatalog.NamesByCategory)
            {
                if (!string.IsNullOrEmpty(categoryName) &&
                    !kv.Key.ToString().Equals(categoryName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Emit(args, $"--- {kv.Key}: {kv.Value.Count} prefabs ---");

                foreach (string prefabName in kv.Value)
                {
                    GameObject prefab = scene.GetPrefab(prefabName.GetStableHashCode());
                    if (prefab == null)
                    {
                        Emit(args, $"    {prefabName,-42} <prefab not in scene>");
                        continue;
                    }

                    string subtype = kv.Key == PinCategory.Ore
                        ? PinCatalog.OreTypeOf(prefabName.GetStableHashCode())
                        : null;

                    string label;
                    try { label = PinPlacer.PreviewLabel(kv.Key, prefab, subtype); }
                    catch (System.Exception e) { label = "<threw: " + e.GetType().Name + ">"; }

                    string flag = string.IsNullOrEmpty(label) || label == prefabName ? "   <-- UNCHANGED" : "";

                    // Labels are stored as $tokens and localised at draw, so show both the stored
                    // string and what it reads as on the map.
                    string shown = label;
                    if (!string.IsNullOrEmpty(label) && label.Contains("$") && Localization.instance != null)
                        shown = Localization.instance.Localize(label);

                    string asRead = shown != label ? $"   ==  {shown}" : "";
                    Emit(args, $"    {prefabName,-42} -> {label}{asRead}{flag}");
                }
            }
        }

        /// Every registered spawner, with the creature prefab name it actually spawns and whether
        /// Subtypes.Spawners currently matches it.
        ///
        /// Authoritative source for that table, with no offline substitute: creature prefab names
        /// are Unity asset references, not string literals, so the table can only be written from
        /// a list the running game hands over. Anything reported NONE is a spawner showing the
        /// generic category icon.
        public static void DumpSpawners(Terminal.ConsoleEventArgs args)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                Emit(args, "No ZNetScene - load into a world first.");
                return;
            }

            if (!PinCatalog.NamesByCategory.TryGetValue(PinCategory.Spawner, out List<string> names))
            {
                Emit(args, "No Spawner prefabs in the catalog.");
                return;
            }

            Emit(args, $"--- {names.Count} Spawner prefabs: spawner -> creature -> matched subtype ---");

            int unmatched = 0;
            foreach (string prefabName in names)
            {
                GameObject prefab = scene.GetPrefab(prefabName.GetStableHashCode());
                if (prefab == null)
                {
                    Emit(args, $"    {prefabName,-38} <prefab not in scene>");
                    continue;
                }

                string creature = PinPlacer.SpawnedCreaturePrefabName(prefab);
                string all = PinPlacer.AllSpawnedCreatureNames(prefab);
                string matched = Subtypes.Match(Subtypes.Spawners, creature);
                if (matched == null)
                    unmatched++;

                string extra = all != null && all != creature ? $"  [all: {all}]" : "";
                Emit(args, $"    {prefabName,-38} -> {creature ?? "(none)",-24} -> {matched ?? "NONE"}{extra}");
            }

            Emit(args, $"--- {unmatched} of {names.Count} spawners have no subtype icon ---");
        }

        /// Every dump, in one call, so the console command and the auto-probe cannot drift into
        /// answering different questions.
        public static void DumpEverything(Terminal.ConsoleEventArgs args)
        {
            DumpLocations(args);
            DumpLocationsFull(args);
            DumpAllPrefabs(args);
            DumpCategory(args, null);
            DumpLabels(args, null);
            DumpSpawners(args);
            DumpPlants(args);
        }

        /// What a planted sapling turns into - the one thing blocking a "wild only" rule for
        /// barley and flax.
        ///
        /// Barley and flax each exist as two prefabs, plain and _Wild, and both are pinned. To
        /// keep a player's own field off the map the mod has to tell them apart, and the obvious
        /// test does not work: read off the DLL, Plant.Grow instantiates one of m_grownPrefabs,
        /// sets its scale and destroys the sapling, and never writes a creator onto the grown
        /// object's ZDO. So IsWild(zdo), which the bee hive and chest rules use, reports a farmed
        /// crop as wild and would pass everything through.
        ///
        /// That leaves the prefab as the only discriminator, and whether the _Wild suffix carries
        /// it is prefab data, not readable offline. This reads it off the live prefab list.
        ///
        /// Every Plant is listed, not just the two: the same question decides the Crops group for
        /// carrot, turnip, onion and kale, and a game update can move typed-out names.
        public static void DumpPlants(Terminal.ConsoleEventArgs args)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null)
            {
                Emit(args, "ZNetScene not ready.");
                return;
            }

            int found = 0;
            Emit(args, "=== planted crops: sapling -> what it grows into ===");

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;

                Plant plant = prefab.GetComponent<Plant>();
                if (plant == null)
                    continue;

                found++;
                var grown = new StringBuilder();
                if (plant.m_grownPrefabs == null || plant.m_grownPrefabs.Length == 0)
                {
                    grown.Append("<none>");
                }
                else
                {
                    foreach (GameObject g in plant.m_grownPrefabs)
                    {
                        if (grown.Length > 0)
                            grown.Append(", ");
                        grown.Append(PinTag(g));
                    }
                }

                Emit(args, $"    {prefab.name,-26} -> {grown}");
            }

            if (found == 0)
                Emit(args, "    no Plant prefabs in the scene list.");
        }

        /// A prefab with what the catalog would do to it, so the dump above needs no cross-reference
        /// against carturpins_catalog.
        private static string PinTag(GameObject prefab)
        {
            if (prefab == null)
                return "<null>";

            int hash = prefab.name.GetStableHashCode();
            if (!PinCatalog.TryGet(hash, out PinCategory category))
                return $"{prefab.name} [not pinned]";

            string group = category == PinCategory.Pickable
                ? "/" + PinCatalog.GroupOf(hash)
                : "";
            return $"{prefab.name} [PINNED {category}{group}]";
        }

        /// `args` is null when the probe runs itself on spawn rather than from a console command;
        /// the log is the real output channel either way, so the auto-probe works without the
        /// game's console enabled.
        private static void Emit(Terminal.ConsoleEventArgs args, string line)
        {
            Plugin.Log.LogInfo(line);
            _file?.WriteLine(line);
            if (args != null)
                args.Context?.AddString(line);
        }

        private static System.IO.StreamWriter _file;

        /// Runs a set of dumps into their own file as well as the log. The BepInEx log is shared
        /// with every other mod, rewritten each launch, and this mod alone writes thousands of
        /// lines into it; a file holding only the dump can be read whenever. Overwrites on each
        /// run, since the current dump is the interesting one.
        internal static void ToFile(string path, System.Action dumps)
        {
            try
            {
                _file = new System.IO.StreamWriter(path, append: false);
                _file.WriteLine($"Cartur's Map Pins {Plugin.PluginVersion} - {System.DateTime.Now:yyyy-MM-dd HH:mm}");
                dumps();
            }
            catch (System.Exception e)
            {
                // Diagnostics must never be the reason a session dies. The dumps still went to the log.
                Plugin.Log.LogWarning($"Could not write the dump file: {e.Message}");
            }
            finally
            {
                _file?.Dispose();
                _file = null;
            }
        }
    }
}
