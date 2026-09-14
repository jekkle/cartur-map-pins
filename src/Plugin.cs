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

        public static CategorySettings SettingsFor(PinCategory category) =>
            Settings.TryGetValue(category, out CategorySettings s) ? s : null;

        /// One switch per kind, for every category that has kinds - Camp Types, Dungeon Types,
        /// Ore Types and the rest, each generated from the same table that detects them, so a kind
        /// can never exist in the matcher without a switch in the menu.
        ///
        /// Keyed by category and name together: two tables can hold the same name, and a Hildir
        /// Cave is both a dungeon kind and a camp kind.
        ///
        /// Switches live in "X Types" and icons in "X Icons" - a section and key pair carries only
        /// one type of value, so a bool and a PinIcon under one name collide.
        private static readonly Dictionary<string, ConfigEntry<bool>> SubtypeToggles =
            new Dictionary<string, ConfigEntry<bool>>();

        /// The kinds that start off, which is the whole of the mod's opinion about what a fresh
        /// map should not carry. Everything else in every table starts on.
        ///
        /// Greydwarf camps are the most common location in the Black Forest - 450 world-generation
        /// attempts against 405 for Fuling villages - and a camp you clear in twenty seconds is
        /// not a trip you plan.
        private static readonly HashSet<string> KindsOffByDefault =
            new HashSet<string> { "Camp:Greydwarf Camp" };

        private static string KindKey(PinCategory category, string kind) => category + ":" + kind;

        /// One switch per dungeon kind for the looted tick, separate from the switches that decide
        /// what gets pinned: whether a Sunken Crypt is worth marking and whether it is worth
        /// ticking off once emptied are different questions. A crypt you strip for iron is done
        /// with; a Frost Cave you dip into for one chest may not be.
        private static readonly Dictionary<string, ConfigEntry<bool>> TickToggles =
            new Dictionary<string, ConfigEntry<bool>>();

        /// A kind with no switch of its own ticks, same rule as everywhere else.
        public static bool TickKindEnabled(string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return true;
            return !TickToggles.TryGetValue(kind, out ConfigEntry<bool> entry) || entry.Value;
        }

        /// Whether one kind is switched on.
        ///
        /// A kind with no switch of its own is allowed through, which does two jobs: a name added
        /// to a table but not yet to the menu still pins rather than silently vanishing, and the
        /// pickable groups - which carry their own switches elsewhere - are not gated twice.
        public static bool SubtypeEnabled(PinCategory category, string kind)
        {
            if (string.IsNullOrEmpty(kind))
                return true;
            return !SubtypeToggles.TryGetValue(KindKey(category, kind), out ConfigEntry<bool> entry)
                   || entry.Value;
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
            AutoProbe = Config.Bind("Diagnostics", "AutoProbeOnSpawn", false,
                "Writes the full diagnostics dump to BepInEx/config/carturpins_dump.txt a few seconds after you load in - every location, the catalog, map labels, spawners, every prefab. OFF by default now that the answers are in the repo: it costs a hitch on every load and rewrites a 800KB file nobody is reading. Turn it on when a game update has moved something, or run carturpins_dumpall once instead.");
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
            Bind(PinCategory.Home, true, Minimap.PinType.Bed, 5f,
                "Marks a bed as home when you claim it as your spawn. Vanilla marks only your current spawn and moves that one marker when you sleep somewhere else, so an outpost you slept in last week leaves nothing behind - these pins stay.",
                iconIndex: 70);   // house with a bed in it
            Bind(PinCategory.Miniboss, true, Minimap.PinType.Boss, 5f,
                "Named minibosses - Lord Reto in the Ashlands, and Hildir's three. Single hand-placed creatures you fight once, so they are on even though the Spawner category they would otherwise sit in is off.",
                iconIndex: 130);  // Lord Reto; per-miniboss icons override this
            Bind(PinCategory.Prop, true, Minimap.PinType.Icon2, 5f,
                "One-off world objects that carry no component saying what they are, so the mod knows them by name - currently the maypole standing in an abandoned Meadows village. Anything you built yourself is never pinned.",
                iconIndex: 149);  // maypole; per-prop icons override this
            Bind(PinCategory.Pickable, true, Minimap.PinType.Icon1, 5f,
                "Pickable plants, mushrooms and one-off items. Which kinds are pinned is decided by the group switches below - this is the master switch for all of them.",
                iconIndex: 84, sectionName: "Pickables");   // question mark; group and per-plant icons override it

            // "Ore Types" is where 1.2.2 kept these switches, so it keeps them.
            // Ore switches come from the catalog's own token table rather than from Subtypes,
            // so an ore type the detector recognises always has a switch - including one a mod
            // adds that the icon table has never heard of.
            foreach ((string _, string type) in PinCatalog.OreTokens)
                BindKindToggle(PinCategory.Ore, "Ore Types", type, $"Pin {type} deposits.");

            TickLootedDungeons = Config.Bind("Dungeon", "TickWhenLooted", true,
                "Tick a dungeon's pin off once nothing inside is worth coming back for - every chest empty and every mud pile mined. The game tracks no such thing itself: dungeon spawners respawn on a timer, so what you took is the only lasting record of having been through. Unticks again if a chest refills.");

            ReplaceBedMarker = Config.Bind("Home", "ReplaceBedMarker", true,
                "Give vanilla's own spawn-point marker the same house icon. Without this, your current bed carries both markers - ours and vanilla's bed glyph - stacked on the same spot.");

            ForgetMinedOre = Config.Bind("Ore", "ForgetMined", true,
                "Remove an ore pin once its deposit has been mined out. Deposits never respawn, so the pin marks an empty hole and sends you back to it. Only pins this mod placed are removed, and only while the game has that area loaded - a node you have simply walked away from is never mistaken for a mined one.");

            LootedChestIcon = Config.Bind("Chest", "LootedIcon", PinIcon.UtilChestOpen,
                new ConfigDescription(
                    "Icon a chest pin switches to once you've emptied it, so cleared chests are distinguishable at a glance. -1 leaves looted chests on the normal chest icon.",
                    null, IconAttr(order: 1)));

            // A switch and an icon for every kind the mod can tell apart. Both are generated from
            // the tables that do the matching, so the menu and the matcher cannot drift.
            // "Dungeon Types" and "Camp Types" held the icons in 1.2.2 and still do.
            BindKinds(PinCategory.Dungeon, "Dungeon", Subtypes.Dungeons, iconSection: "Dungeon Types");
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Dungeons))
            {
                if (TickToggles.ContainsKey(e.Name))
                    continue;
                TickToggles[e.Name] = Config.Bind("Dungeon Tick", e.Name, true,
                    $"Tick a {e.Name} off once everything inside it has been taken. Requires Dungeon/TickWhenLooted.");
            }
            BindKinds(PinCategory.Camp, "Camp", Subtypes.Camps, iconSection: "Camp Types");
            BindKinds(PinCategory.BossAltar, "Boss", Subtypes.Bosses);
            BindKinds(PinCategory.Trader, "Trader", Subtypes.Traders);
            BindKinds(PinCategory.Spawner, "Spawner", Subtypes.Spawners);
            BindKinds(PinCategory.Miniboss, "Miniboss", Subtypes.Minibosses);
            BindKinds(PinCategory.Pickable, "Pickable", Subtypes.Pickables);
            BindKinds(PinCategory.Landmark, "Landmark", Subtypes.Landmarks);
            BindKinds(PinCategory.Prop, "Prop", Subtypes.Props);
            // Ruins that never get a pin of their own - their chest carries the name and icon, so
            // switching one off means those ruins stop being pinned at all.
            BindKinds(PinCategory.Chest, "Chest Site", Subtypes.ChestSites);
            // Ore icons only: the switches above came from the catalog.
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Ores))
                BindSubtypeIcon("Ore Icons", e);

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

        /// One switch and one icon per kind in a table.
        ///
        /// Sections are "X Kinds" for the switches and "X Icons" for the icons - except where 1.2.2
        /// already shipped a section, which keeps its name whatever it holds. A section and key
        /// pair carries one type of value, so reusing "Dungeon Types" for switches would have read
        /// somebody's saved icon choice as a true/false, failed, and silently reset it. Two odd
        /// names are cheaper than every updating player losing the icons they picked.
        private void BindKinds(PinCategory category, string name, Subtypes.Entry[] table,
                               string switchSection = null, string iconSection = null)
        {
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(table))
            {
                BindKindToggle(category, switchSection ?? name + " Kinds", e.Name, $"Pin {e.Name}.");
                BindSubtypeIcon(iconSection ?? name + " Icons", e);
            }
        }

        private void BindKindToggle(PinCategory category, string section, string kind, string description)
        {
            string key = KindKey(category, kind);
            if (SubtypeToggles.ContainsKey(key))
                return;
            bool on = !KindsOffByDefault.Contains(key);
            SubtypeToggles[key] = Config.Bind(section, kind, on,
                $"{description} Requires the {category} category to be enabled."
                + (on ? "" : " OFF by default."));
        }

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
        public static ConfigEntry<bool> ForgetMinedOre;
        public static ConfigEntry<bool> ReplaceBedMarker;
        public static ConfigEntry<bool> TickLootedDungeons;

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

            new Terminal.ConsoleCommand("carturpins_flag_unknown",
                "Marks pins you placed by hand that carry a custom icon, so you can see which ones need their icon picking again after the sheet changed. Names are left alone. Run it once after updating.",
                args =>
                {
                    Minimap.PinType warning = CustomIcons.Resolve((int)PinIcon.UtilWarning, Minimap.PinType.Icon3);
                    if (!CustomIcons.IsCustom(warning))
                    {
                        args.Context?.AddString("Custom icons are off, so there is no warning icon to use.");
                        return;
                    }
                    int flagged = PinRecord.FlagUnknownCustomPins(warning);
                    args.Context?.AddString(flagged > 0
                        ? $"Marked {flagged} hand-placed pin(s) with the warning icon. Their names are unchanged - shift-click one to pick its icon again."
                        : "No hand-placed pins with a custom icon - nothing to mark.");
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
                args => Probe.DumpEverything(args));
#endif
        }
    }
}
