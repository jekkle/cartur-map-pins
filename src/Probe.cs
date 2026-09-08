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
        private static void Emit(Terminal.ConsoleEventArgs args, string line)
        {
            Plugin.Log.LogInfo(line);
            if (args != null)
                args.Context?.AddString(line);
        }
    }
}
