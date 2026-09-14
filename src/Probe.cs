using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CarturMapPins
{
    /// Diagnostics for "why didn't this thing get pinned?".
    ///
    /// Exists because the catalog registered 58 ore prefabs yet no ore instance ever matched a
    /// live ZDO - which can only be answered by inspecting a real object in the world and
    /// comparing its prefab name/hash against what's registered.
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

            // Everything that could plausibly be a pinnable node, found by component type rather
            // than by prefab name - the same principle the catalog uses.
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
        /// This is the authoritative source for the subtype tables: location prefab names are
        /// NOT string literals in assembly_valheim (they're Unity asset references), so they
        /// can't be read offline - but ZoneSystem.m_locations is public and populated on clients
        /// too, and it also picks up locations added by other mods.
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
                // The game's own name for the place, which a prefab name only hints at: a location
                // called MorkBorg or FimbulLocation01 says nothing about what a player would call
                // it, and matching artwork to places needs the player's name for them.
                string named = !string.IsNullOrEmpty(zl.m_name) ? $" name=\"{Labels.Localize(zl.m_name)}\"" : "";
                Emit(args, $"    {name}  biome={zl.m_biome} quantity={zl.m_quantity}{flags}{named}{Shape(zl)}");
            }
        }

        /// The two fields TryClassifyLocation branches on, read off the location prefab itself.
        /// Without them the dump cannot tell a location nobody pins from one pinned on the generic
        /// category icon, which is the whole question when deciding what deserves a subtype.
        ///
        /// ZoneLocation.m_prefab is a SoftReference, and its Asset getter throws until the prefab
        /// is loaded - so this is the sequence ZoneSystem.SpawnLocation itself uses: Load, read,
        /// Release. A location already held by the world stays held; only ones this loads are
        /// released again.
        private static string Shape(ZoneSystem.ZoneLocation zl)
        {
            if (!zl.m_prefab.IsValid)
                return " kind=? (no soft reference)";

            bool alreadyLoaded = zl.m_prefab.IsLoaded;
            if (!alreadyLoaded)
            {
                SoftReferenceableAssets.LoadResult result = zl.m_prefab.Load();
                // Says which of "the bundle would not give it up" and "it has no Location on it"
                // a blank answer is. Nineteen locations came back unreadable with no way to tell.
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

                // What the game prints when you walk into the place - "Winding Church" rather
                // than "MorkBorg". The one field that can match the icon sheet's names to
                // locations, and nothing has been reading it.
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
        /// "Greydwarf_camp1 is surface, with no generator, and pins nothing" leaves the real
        /// question open - whether anything inside it is pinned instead, or whether a Greydwarf
        /// camp is invisible on the map entirely. That cannot be answered from the outside of the
        /// prefab, and every unanswered question about a location costs a relaunch to ask.
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
            // Localized, because that is what the pin ends up carrying - a dump full of
            // "$enemy_eikthyr" cannot be checked against what is actually on the map.
            return $"  -> {icon} [{state}] \"{Labels.Localize(label)}\"";
        }

        /// Lists the game's TMP font assets by exact name.
        ///
        /// Unrelated to pinning, but there's nowhere else that can answer it: mods that take a
        /// font name in config (e.g. Ammo Count's `AmmoTextFont`) match it exactly against
        /// Resources.FindObjectsOfTypeAll&lt;TMP_FontAsset&gt;(), and a stale name silently yields a
        /// null font - text renders as nothing while its icon still shows. The names can't be
        /// read from the shipped tmp_fonts bundle because it's compressed.
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

        /// `args` is null when the probe runs itself on spawn rather than from a console
        /// command - the log is the real output channel either way, which is what makes the
        /// auto-probe usable without the game's console being enabled at all.
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

                    // Labels are stored as $tokens and localised when the pin is drawn, so the
                    // raw value is not what anyone sees. Show both: the stored string, and what
                    // it actually reads as on the map.
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
        /// This is the authoritative source for that table and there is no offline substitute:
        /// creature prefab names are Unity asset references, not string literals, so the table
        /// can only be written from a list the running game hands over. Anything reported NONE
        /// here is a spawner showing the generic category icon.
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
            DumpCategory(args, null);
            DumpLabels(args, null);
            DumpSpawners(args);
        }

        private static void Emit(Terminal.ConsoleEventArgs args, string line)
        {
            Plugin.Log.LogInfo(line);
            _file?.WriteLine(line);
            if (args != null)
                args.Context?.AddString(line);
        }

        private static System.IO.StreamWriter _file;

        /// Runs a set of dumps into their own file as well as the log.
        ///
        /// The BepInEx log is shared with every other mod, is rewritten each launch, and this mod
        /// alone writes thousands of lines into it - so answering one question out of it means
        /// running a command at exactly the right moment and reading fast. A file that holds only
        /// the dump can be read whenever.
        ///
        /// Overwrites on each run: the interesting dump is always the current one.
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
                // Diagnostics must never be the reason a session dies. The dumps still went to the
                // log, which is where they used to go anyway.
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
