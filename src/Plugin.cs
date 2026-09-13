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
        public const string PluginVersion = "1.2.2";

        internal static ManualLogSource Log;

        public static ConfigEntry<float> DiscoveryRadius;
        public static ConfigEntry<float> ScanInterval;
#if DIAGNOSTICS
        public static ConfigEntry<bool> AutoProbe;
#endif
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
            public ConfigEntry<PinIcon> IconIndex;
            public ConfigEntry<float> DedupeRadius;

            /// Custom icon when one is chosen and available, otherwise the vanilla pin type.
            public Minimap.PinType ResolvedPinType => CustomIcons.Resolve((int)IconIndex.Value, PinType.Value);
        }

        private static readonly Dictionary<PinCategory, CategorySettings> Settings =
            new Dictionary<PinCategory, CategorySettings>();

        /// One toggle per ore type, generated from the same token table that detects them, so a
        /// type can never exist in the detector without a matching switch in the menu.
        private static readonly Dictionary<string, ConfigEntry<bool>> OreTypeToggles =
            new Dictionary<string, ConfigEntry<bool>>();

        public static CategorySettings SettingsFor(PinCategory category) =>
            Settings.TryGetValue(category, out CategorySettings s) ? s : null;

        private static readonly Dictionary<string, ConfigEntry<bool>> LandmarkToggles =
            new Dictionary<string, ConfigEntry<bool>>();

        /// A landmark kind with no switch of its own is allowed through, so a name added to the
        /// table but not yet to the menu still pins rather than silently vanishing.
        public static bool LandmarkEnabled(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return true;
            return !LandmarkToggles.TryGetValue(kind, out ConfigEntry<bool> entry) || entry.Value;
        }

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
                new ConfigDescription("How often to check pending objects and loaded locations against your position.",
                    null, Attr(advanced: true)));
#if DIAGNOSTICS
            AutoProbe = Config.Bind("Diagnostics", "AutoProbeOnSpawn", true,
                "Logs a one-shot report of nearby nodes and the registered ore prefabs shortly after you load in. Useful for working out why something isn't being pinned.");
#endif

            MapPickerEnabled = Config.Bind("CustomIcons", "MapPicker", true,
                "Show a scrollable grid of the custom icons on the large map, next to vanilla's own row of pin-type buttons. Only affects pins you place by hand - auto-pins use each category's IconIndex.");
            MapPickerX = Config.Bind("CustomIcons", "MapPickerX", 20f,
                new ConfigDescription("Horizontal offset of the picker panel from the bottom-left of the map screen.",
                    null, Attr(advanced: true)));
            MapPickerY = Config.Bind("CustomIcons", "MapPickerY", 20f,
                new ConfigDescription("Vertical offset of the picker panel from the bottom-left of the map screen.",
                    null, Attr(advanced: true)));

            CustomIconsEnabled = Config.Bind("CustomIcons", "Enabled", true,
                "Use the bundled icon sheet, adding its icons as extra pin types alongside the vanilla ones (nothing vanilla is replaced). If this is off, or the sheet fails to load, every category falls back to its vanilla PinType.");

            // IconIndex values refer to Assets/pin_icons_numbered.png. PinType is the vanilla
            // fallback used when custom icons are off.
            Bind(PinCategory.Ore, true, Minimap.PinType.Icon3, 15f,
                "Ore deposits and mineable nodes. Individual ore types have their own toggles in the Ore Types section and their own icons in Ore Icons.",
                iconIndex: 119);  // generic ore chunk; per-type icons override this
            Bind(PinCategory.Dungeon, true, Minimap.PinType.Icon4, 5f,
                "Dungeon and cave entrances (Burial Chambers, Sunken Crypts, Frost Caves, Troll Caves, Infested Mines).",
                iconIndex: 39);   // stairs down
            Bind(PinCategory.Camp, true, Minimap.PinType.Icon3, 20f,
                "Surface camps and villages (Fuling villages, Greydwarf camps, Charred fortresses). These use the same generator as dungeons but have no interior.",
                iconIndex: 42);   // village
            Bind(PinCategory.BossAltar, true, Minimap.PinType.Boss, 5f,
                "Boss summoning altars, with a different icon per boss. Vanilla marks these itself, but its marker carries no name and isn't saved - so ours replaces it rather than stacking on top, and vanilla's is left alone for any altar you haven't found yet.",
                iconIndex: 79);   // offering bowl; per-boss icons override this
            Bind(PinCategory.Beehive, true, Minimap.PinType.Icon3, 5f,
                "Wild beehives. Player-built hives are never pinned.",
                iconIndex: 78);   // beehive
            Bind(PinCategory.Runestone, true, Minimap.PinType.Icon2, 5f,
                "Runestones and Vegvisirs. Vanilla never pins these.",
                iconIndex: 58);   // vegvisir
            Bind(PinCategory.LoreStone, false, Minimap.PinType.Icon2, 5f,
                "Lore runestones - the eleven story stones scattered across the biomes (Boars, Meadows, Draugr, Black Forest...). Separate from the boss stones above. OFF by default: they are read-once curiosities, and pinning all of them clutters a map you actually navigate with.",
                iconIndex: 57);   // runestone
            Bind(PinCategory.Chest, true, Minimap.PinType.Icon2, 5f,
                "Loot chests found in the world. Player-built containers are never pinned.",
                iconIndex: 72);   // chest
            Bind(PinCategory.Spawner, false, Minimap.PinType.Icon3, 15f,
                "Creature nests and spawners (greydwarf nests, draugr piles, bone piles, surtling geysers) - the static ones worth farming or avoiding. OFF by default - the catalog covers 103 spawner prefabs, including chicken, bat, fish and leech, so a fresh world would carpet the map. Individual creatures have their own icons in Spawner Types.",
                iconIndex: 9);    // summoning circle; per-creature icons override this
            Bind(PinCategory.Leviathan, true, Minimap.PinType.Icon3, 30f,
                "Leviathans. Note they submerge once mined, so a saved pin will outlive the creature.",
                iconIndex: 50);   // leviathan
            Bind(PinCategory.Trader, true, Minimap.PinType.Icon3, 5f,
                "Traders (Haldor, Hildir, the Bog Witch). Vanilla already marks their location with an unnamed icon; this adds a named, saved pin.",
                iconIndex: 88);   // coins; per-trader icons override this
            Bind(PinCategory.Wisp, false, Minimap.PinType.Icon3, 20f,
                "Wisp spawners in the Mistlands. OFF by default - they are numerous.",
                iconIndex: 85);   // star
            // Pickable had no binding at all, which meant SettingsFor(Pickable) returned null and
            // TryPin dropped every pickable on the floor - the whole Pickables section, the
            // classifier and the group icons were wired to nothing. Bound into the existing
            // "Pickables" section rather than a new "Pickable" one, so the category's own knobs
            // sit with the group toggles instead of in a near-identically named section.
            Bind(PinCategory.Landmark, false, Minimap.PinType.Icon2, 10f,
                "Surface landmarks with nothing inside them - wells, shipwrecks, dolmens, stone circles, swamp huts, abandoned houses. OFF by default: these are numerous and decorative, and pinning all of them buries the map. Individual kinds have their own switches in Landmark Types.",
                iconIndex: 45);   // stone circle; per-kind icons override it
            Bind(PinCategory.Pickable, true, Minimap.PinType.Icon1, 5f,
                "Pickable plants, mushrooms and one-off items. Which kinds are pinned is decided by the group switches below - this is the master switch for all of them.",
                iconIndex: 84, sectionName: "Pickables");   // question mark; group and per-plant icons override it

            foreach ((string _, string type) in PinCatalog.OreTokens)
            {
                if (OreTypeToggles.ContainsKey(type))
                    continue;   // Iron appears twice (ore + scrap) and needs only one switch
                OreTypeToggles[type] = Config.Bind("Ore Types", type, true,
                    $"Pin {type} deposits. Requires the Ore category to be enabled.");
            }

            LootedChestIcon = Config.Bind("Chest", "LootedIcon", PinIcon.UtilChestOpen,
                new ConfigDescription(
                    "Icon a chest pin switches to once you've emptied it, so cleared chests are distinguishable at a glance. -1 leaves looted chests on the normal chest icon.",
                    null, IconAttr(order: 1)));

            // These categories get an icon per kind, not one for the whole category.
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Dungeons))
                BindSubtypeIcon("Dungeon Types", e);
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Camps))
                BindSubtypeIcon("Camp Types", e);
            // "Ore Icons" rather than "Ore Types": that section already holds one bool per ore
            // type, and a section+key pair can only carry one type of value.
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Ores))
                BindSubtypeIcon("Ore Icons", e);
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Bosses))
                BindSubtypeIcon("Boss Types", e);
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Traders))
                BindSubtypeIcon("Trader Types", e);
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Spawners))
                BindSubtypeIcon("Spawner Types", e);
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Pickables))
                BindSubtypeIcon("Pickable Types", e);
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Landmarks))
                BindSubtypeIcon("Landmark Icons", e);

            // One switch per landmark kind, generated from the same table that detects them, so a
            // kind can never exist in the matcher without a matching switch in the menu.
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Landmarks))
            {
                if (LandmarkToggles.ContainsKey(e.Name))
                    continue;
                LandmarkToggles[e.Name] = Config.Bind("Landmark Types", e.Name, true,
                    $"Pin {e.Name} landmarks. Requires the Landmark category to be enabled.");
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

            // Pickable groups get their own icons - one shared icon for berries, crops and
            // surtling cores alike would lose most of the value of pinning them at all.
            BindGroupIcon(PickableGroup.Berries, 90);     // raspberries
            BindGroupIcon(PickableGroup.Mushrooms, 95);   // mushroom
            BindGroupIcon(PickableGroup.Crops, 94);       // thistle
            BindGroupIcon(PickableGroup.HighValue, 118);  // surtling core
            BindGroupIcon(PickableGroup.Junk, 107);       // branch
            BindGroupIcon(PickableGroup.Other, 84);       // question mark

            PinRecord.Load(Paths.ConfigPath);

            Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
            RegisterCommands();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        /// ConfigurationManager reads these by duck typing, so they cost nothing when it is absent.
        private static ConfigurationManagerAttributes Attr(bool? browsable = null, bool advanced = false, int order = 0) =>
            new ConfigurationManagerAttributes { Browsable = browsable, IsAdvanced = advanced, Order = order };

        private static ConfigurationManagerAttributes IconAttr(int order) =>
            new ConfigurationManagerAttributes { Order = order, CustomDrawer = IconDrawer.Draw };

        private void Bind(PinCategory category, bool enabled, Minimap.PinType pinType, float dedupe, string description, int iconIndex, string sectionName = null)
        {
            string section = sectionName ?? category.ToString();
            Settings[category] = new CategorySettings
            {
                Enabled = Config.Bind(section, "Enabled", enabled,
                    new ConfigDescription(description, null, Attr(order: 2))),
                IconIndex = Config.Bind(section, "Icon", (PinIcon)iconIndex,
                    new ConfigDescription("Pin icon for this category. Pick one, or use the default.",
                        null, IconAttr(order: 1))),
                // Kept out of the settings screen. The vanilla icon is only ever a fallback for
                // when the custom sheet fails to load, and choosing one is not a decision anybody
                // needs to make from a menu.
                PinType = Config.Bind(section, "PinTypeFallback", pinType,
                    new ConfigDescription("Vanilla map icon used only if the custom icon sheet fails to load.",
                        null, Attr(browsable: false))),
                // Spacing between two pins of the same kind. Correct values differ per category -
                // chests sit metres apart in a village, ore clusters do not - so these stay, but
                // off the settings screen where they were 13 sliders nobody wanted.
                DedupeRadius = Config.Bind(section, "MinimumSpacing", dedupe,
                    new ConfigDescription("Don't place a second pin of this kind within this many metres.",
                        null, Attr(advanced: true, order: 0))),
            };
        }

        private static readonly Dictionary<PickableGroup, ConfigEntry<PinIcon>> PickableIcons =
            new Dictionary<PickableGroup, ConfigEntry<PinIcon>>();

        /// Icon per dungeon/camp kind, keyed by subtype name.
        private static readonly Dictionary<string, ConfigEntry<PinIcon>> SubtypeIcons =
            new Dictionary<string, ConfigEntry<PinIcon>>();

        private void BindSubtypeIcon(string section, Subtypes.Entry entry)
        {
            if (SubtypeIcons.ContainsKey(entry.Name))
                return;
            SubtypeIcons[entry.Name] = Config.Bind(section, entry.Name, (PinIcon)entry.DefaultIcon,
                new ConfigDescription(
                    $"Icon for {entry.Name} (see Assets/pin_icons_numbered.png). -1 falls back to the category's own icon.",
                    null, IconAttr(order: 1)));
        }

        /// A dungeon/camp subtype's icon, falling back to the category's setting when the
        /// subtype is unknown or set to -1.
        public static Minimap.PinType SubtypeIconFor(string subtype, CategorySettings settings)
        {
            if (!string.IsNullOrEmpty(subtype) && SubtypeIcons.TryGetValue(subtype, out ConfigEntry<PinIcon> entry))
            {
                Minimap.PinType resolved = CustomIcons.Resolve((int)entry.Value, settings.ResolvedPinType);
                return resolved;
            }
            return settings.ResolvedPinType;
        }

        public static ConfigEntry<PinIcon> LootedChestIcon;

        private void BindGroupIcon(PickableGroup group, int iconIndex)
        {
            PickableIcons[group] = Config.Bind("Pickables", $"{group}Icon", (PinIcon)iconIndex,
                new ConfigDescription(
                    $"Icon for {group} pickables (see Assets/pin_icons_numbered.png). -1 falls back to the Pickable category's PinType.",
                    null, IconAttr(order: 1)));
        }

        /// A pickable's icon comes from its group, falling back to the category's own setting.
        public static Minimap.PinType PickableIconFor(PickableGroup group, Minimap.PinType fallback)
        {
            if (PickableIcons.TryGetValue(group, out ConfigEntry<PinIcon> entry))
                return CustomIcons.Resolve((int)entry.Value, fallback);
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

            new Terminal.ConsoleCommand("carturpins_forget_missing",
                "Forgets records whose pin is no longer on your map, so those nodes can be pinned again. Use this if pins were lost to a crash; it will also bring back pins you deleted on purpose.",
                args =>
                {
                    int dropped = PinRecord.ForgetMissing();
                    args.Context?.AddString(dropped > 0
                        ? $"Forgot {dropped} record(s) with no pin. They will pin again when you next go near them."
                        : "Every record still has a pin - nothing to forget.");
                });

            new Terminal.ConsoleCommand("carturpins_count",
                "Reports how many map pins Cartur's Map Pins has placed.",
                args => args.Context?.AddString($"{PinRecord.Count} pins on record."));

#if DIAGNOSTICS
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

            new Terminal.ConsoleCommand("carturpins_spawners",
                "Diagnostics: every spawner prefab, the creature it spawns, and whether it has a subtype icon. The source for the spawner icon table.",
                args => Probe.DumpSpawners(args));

            new Terminal.ConsoleCommand("carturpins_labels",
                "Diagnostics: every catalogued prefab and the map label it would get, e.g. `carturpins_labels Chest`. No argument dumps all categories.",
                args => Probe.DumpLabels(args, args.Args.Length > 1 ? args.Args[1] : null));

            new Terminal.ConsoleCommand("carturpins_catalog",
                "Diagnostics: lists the prefab names registered for a category, e.g. `carturpins_catalog Ore`.",
                args => Probe.DumpCategory(args, args.Args.Length > 1 ? args.Args[1] : null));

            // Every dump in one go. Each of these needs a restart to pick up a code change, and
            // running them one at a time means a restart per question.
            new Terminal.ConsoleCommand("carturpins_dumpall",
                "Diagnostics: runs every dump - locations, catalog, labels, spawners - into the log in one pass.",
                args =>
                {
                    Probe.DumpLocations(args);
                    Probe.DumpCategory(args, null);
                    Probe.DumpLabels(args, null);
                    Probe.DumpSpawners(args);
                });
#endif
        }
    }
}
