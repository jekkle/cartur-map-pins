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
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        public static ConfigEntry<float> DiscoveryRadius;
        public static ConfigEntry<float> ScanInterval;

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

        public static CategorySettings SettingsFor(PinCategory category) =>
            Settings.TryGetValue(category, out CategorySettings s) ? s : null;

        private void Awake()
        {
            Log = Logger;

            DiscoveryRadius = Config.Bind("General", "DiscoveryRadius", 60f,
                "How close (metres) you must get before something is pinned. Objects load from further away than you can see, so this is what makes pins appear on discovery rather than on load.");
            ScanInterval = Config.Bind("General", "ScanIntervalSeconds", 0.33f,
                "How often to check pending objects and loaded locations against your position.");

            Bind(PinCategory.Ore, enabled: true, Minimap.PinType.Icon3, dedupe: 15f,
                "Ore deposits and mineable rocks (copper, tin, silver, obsidian, meteorite, flametal).");
            Bind(PinCategory.Dungeon, enabled: true, Minimap.PinType.Icon4, dedupe: 5f,
                "Dungeon and cave entrances (Burial Chambers, Sunken Crypts, Frost Caves, Troll Caves, Infested Mines).");
            Bind(PinCategory.BossAltar, enabled: false, Minimap.PinType.Boss, dedupe: 5f,
                "Boss summoning altars. OFF by default because vanilla already marks these with its own icon - turning this on adds a named, saved, tickable pin on top (vanilla's has no label and isn't saved).");
            Bind(PinCategory.Beehive, enabled: true, Minimap.PinType.Icon3, dedupe: 5f,
                "Wild beehives. Player-built hives are never pinned.");
            Bind(PinCategory.Runestone, enabled: true, Minimap.PinType.Icon2, dedupe: 5f,
                "Runestones and Vegvisirs. Vanilla never pins these.");
            Bind(PinCategory.Pickable, enabled: true, Minimap.PinType.Icon3, dedupe: 10f,
                "Pickables. See the Pickables section for which kinds - most are off by default because they are extremely numerous.");

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
                    "Don't place a second pin of this category within this many metres. Ore needs a wide radius so a deposit field doesn't become a pincushion."),
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
        }
    }
}
