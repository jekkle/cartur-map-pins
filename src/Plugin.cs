using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CarturMapPins
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturmappins";
        public const string PluginName = "Cartur's Map Pins";
        public const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log;

        public static ConfigEntry<float> DiscoveryRadius;
        public static ConfigEntry<float> ScanInterval;
        public static ConfigEntry<bool> AutoProbe;

        private static ConfigEntry<bool> _pickHighValue;
        private static ConfigEntry<bool> _pickBerries;
        private static ConfigEntry<bool> _pickCrops;
        private static ConfigEntry<bool> _pickJunk;
        private static ConfigEntry<bool> _pickOther;

        /// Per-category knobs. PinType is exposed because the enum names Icon0-Icon4 say nothing
        /// about which artwork they actually are - that has to be checked in game, and then any
        /// slot can be reshuffled here without a rebuild.
        public class CategorySettings
        {
            public ConfigEntry<bool> Enabled;
            public ConfigEntry<Minimap.PinType> PinType;
            public ConfigEntry<float> DedupeRadius;
        }

        private static readonly Dictionary<PinCategory, CategorySettings> Settings =
            new Dictionary<PinCategory, CategorySettings>();

        /// One toggle per ore type, generated from the same token table that detects them, so a
        /// type can never exist in the detector without a matching switch in the menu.
        private static readonly Dictionary<string, ConfigEntry<bool>> OreTypeToggles =
            new Dictionary<string, ConfigEntry<bool>>();

        public static CategorySettings SettingsFor(PinCategory category) =>
            Settings.TryGetValue(category, out CategorySettings s) ? s : null;

        public static bool OreTypeEnabled(string oreType)
        {
            if (string.IsNullOrEmpty(oreType))
                return true;
            return !OreTypeToggles.TryGetValue(oreType, out ConfigEntry<bool> entry) || entry.Value;
        }

        private void Awake()
        {
            Log = Logger;

            DiscoveryRadius = Config.Bind("General", "DiscoveryRadius", 60f,
                "How close (metres) you must get before something is pinned. Objects load from further away than you can see, so this is what makes pins appear on discovery rather than on load.");
            ScanInterval = Config.Bind("General", "ScanIntervalSeconds", 0.33f,
                "How often to check pending objects and loaded locations against your position.");
            AutoProbe = Config.Bind("Diagnostics", "AutoProbeOnSpawn", false,
                "Logs a one-shot report of nearby nodes and the registered ore prefabs shortly after you load in. Useful for working out why something isn't being pinned.");

            // Most categories use Icon3 (the plain dot) with a simple label; chests and
            // runestones use Icon2, dungeons Icon4, boss altars the dedicated Boss icon.
            Bind(PinCategory.Ore, true, Minimap.PinType.Icon3, 15f,
                "Ore deposits and mineable nodes. Individual ore types have their own toggles in the Ore Types section.");
            Bind(PinCategory.Dungeon, true, Minimap.PinType.Icon4, 5f,
                "Dungeon and cave entrances (Burial Chambers, Sunken Crypts, Frost Caves, Troll Caves, Infested Mines).");
            Bind(PinCategory.Camp, true, Minimap.PinType.Icon3, 20f,
                "Surface camps and villages (Fuling villages, Greydwarf camps, Charred fortresses). These use the same generator as dungeons but have no interior.");
            Bind(PinCategory.BossAltar, false, Minimap.PinType.Boss, 5f,
                "Boss summoning altars. OFF by default because vanilla already marks these with its own icon - turning this on adds a named, saved, tickable pin on top (vanilla's has no label and isn't saved).");
            Bind(PinCategory.Beehive, true, Minimap.PinType.Icon3, 5f,
                "Wild beehives. Player-built hives are never pinned.");
            Bind(PinCategory.Runestone, true, Minimap.PinType.Icon2, 5f,
                "Runestones and Vegvisirs. Vanilla never pins these.");
            Bind(PinCategory.Chest, true, Minimap.PinType.Icon2, 5f,
                "Loot chests found in the world. Player-built containers are never pinned.");
            Bind(PinCategory.Spawner, true, Minimap.PinType.Icon3, 15f,
                "Creature nests and spawners (greydwarf nests, draugr piles, bone piles, surtling geysers) - the static ones worth farming or avoiding.");
            Bind(PinCategory.Leviathan, true, Minimap.PinType.Icon3, 30f,
                "Leviathans. Note they submerge once mined, so a saved pin will outlive the creature.");
            Bind(PinCategory.Trader, true, Minimap.PinType.Icon3, 5f,
                "Traders (Haldor, Hildir). Vanilla already marks their location with an unnamed icon; this adds a named, saved pin.");
            Bind(PinCategory.Wisp, false, Minimap.PinType.Icon3, 20f,
                "Wisp spawners in the Mistlands. OFF by default - they are numerous.");

            foreach ((string _, string type) in PinCatalog.OreTokens)
            {
                if (OreTypeToggles.ContainsKey(type))
                    continue;   // Iron appears twice (ore + scrap) and needs only one switch
                OreTypeToggles[type] = Config.Bind("Ore Types", type, true,
                    $"Pin {type} deposits. Requires the Ore category to be enabled.");
            }

            _pickHighValue = Config.Bind("Pickables", "HighValue", true,
                "Surtling cores, Yggdrasil shoots, eggs. Rare and worth remembering.");
            _pickBerries = Config.Bind("Pickables", "BerriesAndMushrooms", false,
                "Raspberry/blueberry/cloudberry bushes and mushrooms.");
            _pickCrops = Config.Bind("Pickables", "CropsAndHerbs", false,
                "Thistle, dandelion, seeds, barley, flax.");
            _pickJunk = Config.Bind("Pickables", "BranchesStonesFlint", false,
                "Not recommended: these blanket every biome and would carpet the map and bloat your save file.");
            _pickOther = Config.Bind("Pickables", "Unrecognised", false,
                "Any pickable that didn't match a known group (including modded ones). Check the log to see what these are.");

            PinRecord.Load(Paths.ConfigPath);

            Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
            RegisterCommands();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void Bind(PinCategory category, bool enabled, Minimap.PinType pinType, float dedupe, string description)
        {
            string section = category.ToString();
            Settings[category] = new CategorySettings
            {
                Enabled = Config.Bind(section, "Enabled", enabled, description),
                PinType = Config.Bind(section, "PinType", pinType,
                    "Which vanilla map icon to use. Icon0-Icon4 are the five generic pin icons you cycle through when placing a pin by hand."),
                DedupeRadius = Config.Bind(section, "DedupeRadius", dedupe,
                    "Don't place a second pin of this kind within this many metres. Ore dedupes per ore type, so copper never suppresses a nearby tin node."),
            };
        }

        public static bool PickableGroupEnabled(PickableGroup group)
        {
            switch (group)
            {
                case PickableGroup.HighValue: return _pickHighValue.Value;
                case PickableGroup.Berries: return _pickBerries.Value;
                case PickableGroup.Crops: return _pickCrops.Value;
                case PickableGroup.Junk: return _pickJunk.Value;
                default: return _pickOther.Value;
            }
        }

        private void Update()
        {
            PinPlacer.Tick(UnityEngine.Time.deltaTime);
        }

        private static void RegisterCommands()
        {
            new Terminal.ConsoleCommand("carturpins_clear",
                "Removes every map pin Cartur's Map Pins created. Hand-placed pins are left alone.",
                args =>
                {
                    int before = PinRecord.Count;
                    int removed = PinRecord.RemoveAll();
                    Minimap.instance?.SaveMapData();
                    args.Context?.AddString($"Removed {removed} of {before} recorded pins.");
                });

            new Terminal.ConsoleCommand("carturpins_count",
                "Reports how many map pins Cartur's Map Pins has placed.",
                args => args.Context?.AddString($"{PinRecord.Count} pins on record."));

            new Terminal.ConsoleCommand("carturpins_probe",
                "Diagnostics: inspects nearby nodes and reports whether their prefab is in the catalog. Optional radius, default 20.",
                args =>
                {
                    float radius = 20f;
                    if (args.Args.Length > 1)
                        float.TryParse(args.Args[1], out radius);
                    Probe.Nearby(args, radius);
                });

            new Terminal.ConsoleCommand("carturpins_catalog",
                "Diagnostics: lists the prefab names registered for a category, e.g. `carturpins_catalog Ore`.",
                args => Probe.DumpCategory(args, args.Args.Length > 1 ? args.Args[1] : null));
        }
    }
}
