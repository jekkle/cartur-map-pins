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
        public static ConfigEntry<bool> CustomIconsEnabled;
        public static ConfigEntry<bool> MapPickerEnabled;
        public static ConfigEntry<float> MapPickerX;
        public static ConfigEntry<float> MapPickerY;

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
            public ConfigEntry<int> IconIndex;
            public ConfigEntry<float> DedupeRadius;

            /// Custom icon when one is chosen and available, otherwise the vanilla pin type.
            public Minimap.PinType ResolvedPinType => CustomIcons.Resolve(IconIndex.Value, PinType.Value);
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
            AutoProbe = Config.Bind("Diagnostics", "AutoProbeOnSpawn", true,
                "Logs a one-shot report of nearby nodes and the registered ore prefabs shortly after you load in. Useful for working out why something isn't being pinned.");

            MapPickerEnabled = Config.Bind("CustomIcons", "MapPicker", true,
                "Show a scrollable grid of the custom icons on the large map, next to vanilla's own row of pin-type buttons. Only affects pins you place by hand - auto-pins use each category's IconIndex.");
            MapPickerX = Config.Bind("CustomIcons", "MapPickerX", 20f,
                "Horizontal offset of the picker panel from the bottom-left of the map screen.");
            MapPickerY = Config.Bind("CustomIcons", "MapPickerY", 20f,
                "Vertical offset of the picker panel from the bottom-left of the map screen.");

            CustomIconsEnabled = Config.Bind("CustomIcons", "Enabled", true,
                "Use the bundled icon sheet, adding its icons as extra pin types alongside the vanilla ones (nothing vanilla is replaced). If this is off, or the sheet fails to load, every category falls back to its vanilla PinType.");

            // IconIndex values refer to Assets/pin_icons_numbered.png. PinType is the vanilla
            // fallback used when custom icons are off.
            Bind(PinCategory.Ore, true, Minimap.PinType.Icon3, 15f,
                "Ore deposits and mineable nodes. Individual ore types have their own toggles in the Ore Types section.",
                iconIndex: 47);   // pickaxe striking rock
            Bind(PinCategory.Dungeon, true, Minimap.PinType.Icon4, 5f,
                "Dungeon and cave entrances (Burial Chambers, Sunken Crypts, Frost Caves, Troll Caves, Infested Mines).",
                iconIndex: 22);   // cobwebbed arch
            Bind(PinCategory.Camp, true, Minimap.PinType.Icon3, 20f,
                "Surface camps and villages (Fuling villages, Greydwarf camps, Charred fortresses). These use the same generator as dungeons but have no interior.",
                iconIndex: 34);   // armed tent camp
            Bind(PinCategory.BossAltar, false, Minimap.PinType.Boss, 5f,
                "Boss summoning altars. OFF by default because vanilla already marks these with its own icon - turning this on adds a named, saved, tickable pin on top (vanilla's has no label and isn't saved).",
                iconIndex: -1);   // -1 = keep the vanilla Boss icon
            Bind(PinCategory.Beehive, true, Minimap.PinType.Icon3, 5f,
                "Wild beehives. Player-built hives are never pinned.",
                iconIndex: 6);    // bee
            Bind(PinCategory.Runestone, true, Minimap.PinType.Icon2, 5f,
                "Runestones and Vegvisirs. Vanilla never pins these.",
                iconIndex: -1);   // -1 = keep the vanilla Icon2 below
            Bind(PinCategory.Chest, true, Minimap.PinType.Icon2, 5f,
                "Loot chests found in the world. Player-built containers are never pinned.",
                iconIndex: 45);   // treasure chest
            Bind(PinCategory.Spawner, true, Minimap.PinType.Icon3, 15f,
                "Creature nests and spawners (greydwarf nests, draugr piles, bone piles, surtling geysers) - the static ones worth farming or avoiding.",
                iconIndex: 48);   // nest with eggs
            Bind(PinCategory.Leviathan, true, Minimap.PinType.Icon3, 30f,
                "Leviathans. Note they submerge once mined, so a saved pin will outlive the creature.",
                iconIndex: 59);   // sea serpent
            Bind(PinCategory.Trader, true, Minimap.PinType.Icon3, 5f,
                "Traders (Haldor, Hildir). Vanilla already marks their location with an unnamed icon; this adds a named, saved pin.",
                iconIndex: 15);   // coin pouch
            Bind(PinCategory.Wisp, false, Minimap.PinType.Icon3, 20f,
                "Wisp spawners in the Mistlands. OFF by default - they are numerous.",
                iconIndex: 32);   // flame

            foreach ((string _, string type) in PinCatalog.OreTokens)
            {
                if (OreTypeToggles.ContainsKey(type))
                    continue;   // Iron appears twice (ore + scrap) and needs only one switch
                OreTypeToggles[type] = Config.Bind("Ore Types", type, true,
                    $"Pin {type} deposits. Requires the Ore category to be enabled.");
            }

            LootedChestIcon = Config.Bind("Chest", "LootedIconIndex", 51,
                new ConfigDescription(
                    "Icon a chest pin switches to once you've emptied it, so cleared chests are distinguishable at a glance. -1 leaves looted chests on the normal chest icon.",
                    new AcceptableValueRange<int>(-1, CustomIcons.IconCount - 1)));

            // Dungeons and camps get an icon per kind, not one for the whole category.
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Dungeons))
                BindSubtypeIcon("Dungeon Types", e);
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Camps))
                BindSubtypeIcon("Camp Types", e);

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

            // Pickable groups get their own icons - one shared icon for berries, crops and
            // surtling cores alike would lose most of the value of pinning them at all.
            BindGroupIcon(PickableGroup.Berries, 36);     // grape/berry cluster
            BindGroupIcon(PickableGroup.Mushrooms, 76);   // split out of berries
            BindGroupIcon(PickableGroup.Crops, 37);       // plant between rocks
            BindGroupIcon(PickableGroup.HighValue, 21);   // egg with sparkles
            BindGroupIcon(PickableGroup.Junk, 0);         // plain dot
            BindGroupIcon(PickableGroup.Other, 0);        // plain dot

            PinRecord.Load(Paths.ConfigPath);

            Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
            RegisterCommands();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void Bind(PinCategory category, bool enabled, Minimap.PinType pinType, float dedupe, string description, int iconIndex)
        {
            string section = category.ToString();
            Settings[category] = new CategorySettings
            {
                Enabled = Config.Bind(section, "Enabled", enabled, description),
                IconIndex = Config.Bind(section, "IconIndex", iconIndex,
                    new ConfigDescription(
                        "Which icon from the custom sheet to use (see Assets/pin_icons_numbered.png for the numbering). -1 uses the vanilla PinType below instead. Ignored unless CustomIcons/Enabled is on.",
                        new AcceptableValueRange<int>(-1, CustomIcons.IconCount - 1))),
                PinType = Config.Bind(section, "PinType", pinType,
                    "Vanilla map icon, used when IconIndex is -1 or custom icons are off. Icon0-Icon4 are the five generic pins you cycle through when placing one by hand."),
                DedupeRadius = Config.Bind(section, "DedupeRadius", dedupe,
                    "Don't place a second pin of this kind within this many metres. Ore dedupes per ore type, so copper never suppresses a nearby tin node."),
            };
        }

        private static readonly Dictionary<PickableGroup, ConfigEntry<int>> PickableIcons =
            new Dictionary<PickableGroup, ConfigEntry<int>>();

        /// Icon per dungeon/camp kind, keyed by subtype name.
        private static readonly Dictionary<string, ConfigEntry<int>> SubtypeIcons =
            new Dictionary<string, ConfigEntry<int>>();

        private void BindSubtypeIcon(string section, Subtypes.Entry entry)
        {
            if (SubtypeIcons.ContainsKey(entry.Name))
                return;
            SubtypeIcons[entry.Name] = Config.Bind(section, entry.Name, entry.DefaultIcon,
                new ConfigDescription(
                    $"Icon for {entry.Name} (see Assets/pin_icons_numbered.png). -1 falls back to the category's own icon.",
                    new AcceptableValueRange<int>(-1, CustomIcons.IconCount - 1)));
        }

        /// A dungeon/camp subtype's icon, falling back to the category's setting when the
        /// subtype is unknown or set to -1.
        public static Minimap.PinType SubtypeIconFor(string subtype, CategorySettings settings)
        {
            if (!string.IsNullOrEmpty(subtype) && SubtypeIcons.TryGetValue(subtype, out ConfigEntry<int> entry))
            {
                Minimap.PinType resolved = CustomIcons.Resolve(entry.Value, settings.ResolvedPinType);
                return resolved;
            }
            return settings.ResolvedPinType;
        }

        public static ConfigEntry<int> LootedChestIcon;

        private void BindGroupIcon(PickableGroup group, int iconIndex)
        {
            PickableIcons[group] = Config.Bind("Pickables", $"{group}Icon", iconIndex,
                new ConfigDescription(
                    $"Icon for {group} pickables (see Assets/pin_icons_numbered.png). -1 falls back to the Pickable category's PinType.",
                    new AcceptableValueRange<int>(-1, CustomIcons.IconCount - 1)));
        }

        /// A pickable's icon comes from its group, falling back to the category's own setting.
        public static Minimap.PinType PickableIconFor(PickableGroup group, Minimap.PinType fallback)
        {
            if (PickableIcons.TryGetValue(group, out ConfigEntry<int> entry))
                return CustomIcons.Resolve(entry.Value, fallback);
            return fallback;
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

            new Terminal.ConsoleCommand("carturpins_locations",
                "Diagnostics: dumps every location prefab name the world generator knows, which is the authoritative source for the dungeon/camp subtype tables.",
                args => Probe.DumpLocations(args));

            new Terminal.ConsoleCommand("carturpins_catalog",
                "Diagnostics: lists the prefab names registered for a category, e.g. `carturpins_catalog Ore`.",
                args => Probe.DumpCategory(args, args.Args.Length > 1 ? args.Args[1] : null));
        }
    }
}
