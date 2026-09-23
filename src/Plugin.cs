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
        public const string PluginVersion = "1.5.0";

        internal static ManualLogSource Log;

        public static ConfigEntry<float> DiscoveryRadius;
        public static ConfigEntry<float> ScanInterval;
#if DIAGNOSTICS
        public static ConfigEntry<bool> AutoProbe;
#endif
        public static ConfigEntry<bool> CustomIconsEnabled;
        public static ConfigEntry<bool> MapPickerEnabled;
        public static ConfigEntry<float> MapPickerRight;
        public static ConfigEntry<float> MapPickerBottom;

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

        /// Whether a dungeon kind ticks off once emptied - a different question from whether it is
        /// worth pinning, so a different switch. A crypt you strip for iron is done with; a Frost
        /// Cave you dip into for one chest may not be.
        ///
        /// Stored in the same place as every other per-kind switch, under a key of its own rather
        /// than in a second dictionary that would need its own lookup, its own binder and its own
        /// "missing means yes" rule.
        private const PinCategory TickPseudoCategory = (PinCategory)(-1);

        public static bool TickKindEnabled(string kind) => SubtypeEnabled(TickPseudoCategory, kind);

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

            // Before the first Bind of anything: Bind rewrites the file once it has bound an
            // entry, replacing any value it could not parse with the default. This reads what the
            // player actually had while that is still what is on disk.
            LegacyIconNames.Capture(Config.ConfigFilePath);

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
            // Renamed from MapPickerX/Y, which measured from the bottom LEFT corner. The panel
            // moved to the right, so an old config's 20 would now mean 20 pixels from the right
            // edge and bury the picker under vanilla's pin buttons. New names, so every existing
            // config takes the new defaults and the stale lines sit there harmlessly.
            MapPickerRight = Config.Bind("CustomIcons", "MapPickerRight", 108f,
                new ConfigDescription("How far in from the right edge of the map screen the picker sits. The default puts it against vanilla's column of pin buttons without overlapping them.",
                    null, Attr(advanced: true)));
            MapPickerBottom = Config.Bind("CustomIcons", "MapPickerBottom", 74f,
                new ConfigDescription("How far up from the bottom of the map screen the picker sits. The default clears the Visible to other players box below it.",
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

            ShrinkCrowdedPins = Config.Bind("General", "ShrinkCrowdedPins", true,
                "Shrink pins that are sitting on top of each other, so a cluster reads as several things rather than one blob. Never below half size, and measured in screen pixels - so the same two pins shrink when you zoom out and return to full size when you zoom in.");

            // Written as an action rather than a state: choosing one applies it and the setting
            // drops back to None. Anything else would claim a preset is still in force while you
            // change switches underneath it, and there is no honest way to know when it stopped
            // being true.
            // Ordered to the top of General: ConfigurationManager sorts a section by Order
            // descending, and the two settings that DO something belong above the ones that
            // merely describe a preference.
            ApplyPreset = Config.Bind("General", "ApplyPreset", Preset.None,
                new ConfigDescription("Set every switch at once. Minimal pins the few things worth walking back to; Everything turns on all of it, chickens and abandoned houses included; Defaults restores what a fresh install uses. Returns to None once applied - it is a button, not a state.",
                    null, Attr(order: 100)));
            ApplyPreset.SettingChanged += (_, __) => Apply(ApplyPreset.Value);

            // Written as a dropdown rather than a tick box, and with the frightening option spelled
            // out in full, because this throws away work: a tick box sits one stray click from
            // deleting a map somebody filled in over fifty hours. Choosing a named option is
            // deliberate in a way that ticking a box is not.
            ResetPins = Config.Bind("General", "ResetPins", PinReset.No,
                new ConfigDescription("Start this world's pins over. Removes every pin this mod placed and forgets them, so each one is pinned again as you rediscover it. Pins you placed by hand are untouched, and so are any colours or sizes you set. Returns to No once it has run - it is a button, not a state.",
                    null, Attr(order: 99)));
            ResetPins.SettingChanged += (_, __) => Reset(ResetPins.Value);

            ZoomedOutPinScale = Config.Bind("General", "ZoomedOutPinScale", 0.6f,
                "How big a pin is drawn when the map is zoomed all the way out, as a fraction of its normal size. Pins are full size zoomed in and shrink steadily towards this as you zoom out, so a wide view reads as territory rather than a wall of icons. 1 turns the shrinking off; 0.2 is as small as it goes. Pin names are separate and always have been: the game itself stops drawing those once you zoom past halfway.");

            HidePinsBeyond = Config.Bind("General", "HidePinsBeyond", 0f,
                "Stop drawing pins further than this many metres from your character. 0 draws all of them, which is the default - the large map is usually wanted whole. Set it to a few thousand to keep the map to the part of the world you are actually in. Nothing is deleted: the pins come straight back when you raise it or walk towards them.");

            MergeRepeatedPins = Config.Bind("General", "MergeRepeatedPins", true,
                "Where several pins with the same name sit on the same patch of screen, draw one of them. A tin field is a dozen separate deposits and so a dozen identical pins, which at map scale is one white blob you can neither count nor click - one icon says as much and leaves the map readable. Pins of different kinds sitting together are all still drawn, and zooming in separates the rest as it always did. Nothing is deleted: every pin is still on the map, still saved, still searchable.");

            HideLabelsFromZoom = Config.Bind("General", "HideLabelsFromZoom", 20f,
                "Stop drawing pin names once the map is zoomed out past this percentage. 0 is fully zoomed in, 100 is the whole world; 100 never hides a name. The game has a setting of its own for this and it does nothing - m_showNamesZoom is 2 where the furthest the map zooms is 1, so its test is true at every zoom and no name is ever hidden by it.");

            ShrinkPinsFromZoom = Config.Bind("General", "ShrinkPinsFromZoom", 15f,
                "How far out the map has to be zoomed before pins start shrinking, as a percentage. 0 is fully zoomed in and 100 is the whole world. Below this pins are full size; past it they shrink steadily towards ZoomedOutPinScale, reaching it at 100.");

            ShowPinLabels = Config.Bind("General", "ShowPinLabels", true,
                "Draw the name under each pin. Turn it off for a map of icons alone - the names are still there, still saved and still searchable, they are simply not drawn, so turning this back on restores every one of them. The pin under your cursor always shows its name, so nothing becomes unidentifiable.");

            HideCollidingLabels = Config.Bind("General", "HideCollidingLabels", true,
                "Stop pin names from being drawn on top of each other. Where two labels overlap, the rarer name is kept - CRYPT beats CHEST, because CHEST appears forty times and says less. The icons are untouched, and a pin under your cursor always shows its name, so nothing is unreadable for long.");

            TintOreByType = Config.Bind("Ore", "TintByType", true,
                "Colour each ore pin by what it is - copper warm brown, tin pale, flametal orange, and so on. The darkest ores are lifted towards grey rather than drawn true, because the map is dark and a black pin on it is a hole. A colour you set on a pin yourself always wins.");

            ForgetMinedOre = Config.Bind("Ore", "ForgetMined", true,
                "Remove an ore pin once its deposit has been mined out. Deposits never respawn, so the pin marks an empty hole and sends you back to it. Only pins this mod placed are removed, and only while the game has that area loaded - a node you have simply walked away from is never mistaken for a mined one.");

            LootedChestIcon = AdoptLegacy(Config.Bind("Chest", "LootedIcon", PinIcon.UtilChestOpen,
                new ConfigDescription(
                    "Icon a chest pin switches to once you've emptied it, so cleared chests are distinguishable at a glance. Default leaves looted chests on the normal chest icon.",
                    null, IconAttr(order: 1))));

            // A switch and an icon for every kind the mod can tell apart. Both are generated from
            // the tables that do the matching, so the menu and the matcher cannot drift.
            // "Dungeon Types" and "Camp Types" held the icons in 1.2.2 and still do.
            BindKinds(PinCategory.Dungeon, "Dungeon", Subtypes.Dungeons, iconSection: "Dungeon Types");
            foreach (Subtypes.Entry e in Subtypes.DistinctOf(Subtypes.Dungeons))
            {
                BindKindToggle(TickPseudoCategory, "Dungeon Tick", e.Name,
                    $"Tick a {e.Name} off once everything inside it has been taken.");
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
                new ConfigDescription("Surtling cores, Yggdrasil shoots, eggs. Rare and worth remembering.",
                    null, Attr(advanced: true)));
            _pickBerries = Config.Bind("Pickables", "BerriesAndMushrooms", false,
                new ConfigDescription("Raspberry/blueberry/cloudberry bushes and mushrooms.",
                    null, Attr(advanced: true)));
            _pickCrops = Config.Bind("Pickables", "CropsAndHerbs", false,
                new ConfigDescription("Thistle, dandelion, seeds, barley, flax.",
                    null, Attr(advanced: true)));
            _pickJunk = Config.Bind("Pickables", "BranchesStonesFlint", false,
                new ConfigDescription("Not recommended: these blanket every biome and would carpet the map and bloat your save file.",
                    null, Attr(advanced: true)));
            _pickOther = Config.Bind("Pickables", "Unrecognised", false,
                new ConfigDescription("Any pickable that didn't match a known group (including modded ones). Check the log to see what these are.",
                    null, Attr(advanced: true)));

            // Pickable groups get their own icons - one shared icon for berries, crops and
            // surtling cores alike would lose most of the value of pinning them at all.
            BindGroupIcon(PickableGroup.Berries, 90);     // raspberries
            BindGroupIcon(PickableGroup.Mushrooms, 95);   // mushroom
            BindGroupIcon(PickableGroup.Crops, 94);       // thistle
            BindGroupIcon(PickableGroup.HighValue, 118);  // surtling core
            BindGroupIcon(PickableGroup.Junk, 107);       // branch
            BindGroupIcon(PickableGroup.Other, 84);       // question mark

            // Only notes where records live. Which file is this one depends on the world and the
            // character, and at plugin load there is neither - it resolves on first use.
            PinRecord.Load(Paths.ConfigPath);
            PinStyles.Load(Paths.ConfigPath);

            // Valheim raises this when the language is changed in the settings, and Minimap
            // subscribes to it for its own event pins (read off the installed assembly_valheim,
            // Minimap.Awake). Ordinary pin names are plain saved text and are not re-read, so
            // ours are the only ones anybody can put back into the new language.
            Localization.OnLanguageChange += OnLanguageChanged;

            // A mod that throws on load takes every plugin after it down with it. Every target
            // resolves against the game installed today, so this catches nothing now - it is here
            // for the update that renames one of them, where the honest outcome is this mod not
            // working and the rest of the game starting.
            try
            {
                Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, PluginGuid);
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Patching failed - the mod will do nothing this session: {e}");
                return;
            }
            RegisterCommands();

            Log.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        /// Wrapped, because this runs on the game's own event: an exception escaping here would
        /// land inside Valheim's language-change notification with every later subscriber
        /// unnotified, which is a settings menu that half-applies.
        private static void OnLanguageChanged()
        {
            try
            {
                int reworded = PinRecord.Relabel();
                if (reworded > 0)
                    Log.LogInfo($"Language changed: re-worded {reworded} pin(s).");
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Could not re-word pins for the new language: {e.Message}");
            }
        }

        public enum PinReset
        {
            No,
            YesRemoveEveryPinThisModPlaced,
        }

        /// Clears this world's pins and the record of them, so everything is pinned afresh as it
        /// is rediscovered.
        ///
        /// Refuses unless a map exists. PinRecord.RemoveAll drops its records whether or not it
        /// found pins to remove, so running this from the main menu would forget everything while
        /// leaving the pins on the map - and the next world load would then pin all of it a second
        /// time on top of what is already there, with nothing left that knows they are duplicates.
        ///
        /// Styles are deliberately left alone. A colour or size is keyed by position and is set by
        /// hand through the pin editor, which works on hand-placed pins too - so wiping them here
        /// would throw away work this reset never touched.
        private static void Reset(PinReset choice)
        {
            if (choice == PinReset.No)
                return;

            if (Minimap.instance == null)
            {
                Log.LogWarning("ResetPins needs a world loaded - nothing was changed. Load your save and set it again.");
            }
            else
            {
                int before = PinRecord.Count;
                int removed = PinRecord.RemoveAll();
                Minimap.instance.SaveMapData();
                Log.LogInfo($"Reset pins: removed {removed} of {before} recorded pin(s). They will be pinned again as you rediscover them.");
            }

            // Cleared last and only if it is still what we were asked for: setting it fires this
            // handler again, and No returns immediately.
            if (ResetPins.Value == choice)
                ResetPins.Value = PinReset.No;
        }

        public enum Preset
        {
            None,
            Minimal,
            Defaults,
            Everything,
        }

        /// The categories a Minimal map keeps: the things worth crossing a map for.
        private static readonly HashSet<PinCategory> MinimalCategories = new HashSet<PinCategory>
        {
            PinCategory.Ore, PinCategory.Dungeon, PinCategory.Chest,
            PinCategory.BossAltar, PinCategory.Miniboss, PinCategory.Home,
        };

        /// Applies a preset and then clears itself.
        ///
        /// Writes through the config entries rather than to some parallel state, so the file, the
        /// settings screen and the mod all say the same thing afterwards and a preset can be used
        /// as a starting point to tweak from.
        private static void Apply(Preset preset)
        {
            if (preset == Preset.None)
                return;

            foreach (KeyValuePair<PinCategory, CategorySettings> kv in Settings)
            {
                switch (preset)
                {
                    case Preset.Minimal:
                        kv.Value.Enabled.Value = MinimalCategories.Contains(kv.Key);
                        break;
                    case Preset.Everything:
                        kv.Value.Enabled.Value = true;
                        break;
                    default:
                        kv.Value.Enabled.Value = (bool)kv.Value.Enabled.DefaultValue;
                        break;
                }
            }

            foreach (KeyValuePair<string, ConfigEntry<bool>> kv in SubtypeToggles)
            {
                // Everything means every kind. Minimal and Defaults both want the shipped
                // defaults underneath - Minimal narrows by switching categories off, not by
                // second-guessing which crypt is worth pinning.
                kv.Value.Value = preset == Preset.Everything || (bool)kv.Value.DefaultValue;
            }

            foreach (ConfigEntry<bool> group in new[] { _pickHighValue, _pickBerries, _pickCrops, _pickJunk, _pickOther })
            {
                if (group == null)
                    continue;
                group.Value = preset == Preset.Everything ? true
                    : preset == Preset.Minimal ? false
                    : (bool)group.DefaultValue;
            }

            Log.LogInfo($"Applied the {preset} preset.");

            // Cleared last, and only if it is still what we were asked for: setting it fires this
            // handler again, and None returns immediately.
            if (ApplyPreset.Value == preset)
                ApplyPreset.Value = Preset.None;
        }

        /// ConfigurationManager reads these by duck typing, so they cost nothing when it is absent.
        /// Every icon setting goes through here on its way out of Bind, so a name written by
        /// 1.2.2 is carried over in the one moment it still can be.
        private static ConfigEntry<PinIcon> AdoptLegacy(ConfigEntry<PinIcon> entry)
        {
            LegacyIconNames.Adopt(entry);
            return entry;
        }

        private static ConfigurationManagerAttributes Attr(bool? browsable = null, bool advanced = false, int order = 0) =>
            new ConfigurationManagerAttributes { Browsable = browsable, IsAdvanced = advanced, Order = order };

        private static ConfigurationManagerAttributes IconAttr(int order, bool advanced = false) =>
            new ConfigurationManagerAttributes { Order = order, IsAdvanced = advanced, CustomDrawer = IconDrawer.Draw };

        private void Bind(PinCategory category, bool enabled, Minimap.PinType pinType, float dedupe, string description, int iconIndex, string sectionName = null)
        {
            string section = sectionName ?? category.ToString();
            Settings[category] = new CategorySettings
            {
                Enabled = Config.Bind(section, "Enabled", enabled,
                    new ConfigDescription(description, null, Attr(order: 2))),
                IconIndex = AdoptLegacy(Config.Bind(section, "Icon", (PinIcon)iconIndex,
                    new ConfigDescription("Pin icon for this category. Pick one, or use the default.",
                        null, IconAttr(order: 1)))),
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
            // The tick switches hang off Dungeon/TickWhenLooted rather than a category of their
            // own, so they say so instead of naming the pseudo-category they are keyed under.
            string requires = category == TickPseudoCategory
                ? " Requires Dungeon/TickWhenLooted."
                : $" Requires the {category} category to be enabled.";

            // Advanced, along with every other per-kind row. There are 379 settings here and
            // roughly 340 of them are one of these: a switch or an icon for a single kind of
            // thing. Shown by default they bury the seventeen settings that decide what the mod
            // does at all, so somebody who only wants to turn camps off has to find "Camp" among
            // nine sections whose names begin with Camp. They are one tick away, under Advanced,
            // and nothing about them has changed.
            SubtypeToggles[key] = Config.Bind(section, kind, on,
                new ConfigDescription(description + requires + (on ? "" : " OFF by default."),
                    null, Attr(advanced: true)));
        }

        private void BindSubtypeIcon(string section, Subtypes.Entry entry)
        {
            if (SubtypeIcons.ContainsKey(entry.Name))
                return;
            SubtypeIcons[entry.Name] = AdoptLegacy(Config.Bind(section, entry.Name, (PinIcon)entry.DefaultIcon,
                new ConfigDescription(
                    $"Icon for {entry.Name}. Default falls back to the category's own icon.",
                    null, IconAttr(order: 1, advanced: true))));
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
        public static ConfigEntry<bool> TintOreByType;
        public static ConfigEntry<bool> ShrinkCrowdedPins;
        public static ConfigEntry<bool> HideCollidingLabels;
        public static ConfigEntry<bool> ShowPinLabels;
        public static ConfigEntry<float> HideLabelsFromZoom;
        public static ConfigEntry<float> ShrinkPinsFromZoom;
        public static ConfigEntry<bool> MergeRepeatedPins;
        public static ConfigEntry<float> ZoomedOutPinScale;
        public static ConfigEntry<float> HidePinsBeyond;
        public static ConfigEntry<Preset> ApplyPreset;
        public static ConfigEntry<PinReset> ResetPins;

        private void BindGroupIcon(PickableGroup group, int iconIndex)
        {
            PickableIcons[group] = AdoptLegacy(Config.Bind("Pickables", $"{group}Icon", (PinIcon)iconIndex,
                new ConfigDescription(
                    $"Icon for {group} pickables. Default falls back to the Pickable category's own icon.",
                    null, IconAttr(order: 1, advanced: true))));
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
            new Terminal.ConsoleCommand("carturpins_dedupe",
                "Removes leftover duplicate pins - a second copy of one of this mod's pins that no record points at, which nothing else can ever clean up. Run it with the word yes to actually remove them; without it, it only counts.",
                args =>
                {
                    // Counts unless told otherwise. This deletes pins the mod has no record of,
                    // and the whole reason they are being removed is that nothing is tracking
                    // them - so there is no undo and no way to put one back.
                    bool confirmed = args.Args != null && args.Args.Length > 1 &&
                                     string.Equals(args.Args[1], "yes", System.StringComparison.OrdinalIgnoreCase);

                    if (!confirmed)
                    {
                        args.Context?.AddString(PinRecord.Audit());
                        args.Context?.AddString("Nothing removed. Run 'carturpins_dedupe yes' to remove the twinned ones.");
                        return;
                    }

                    int removed = PinRecord.RemoveOrphanTwins();
                    args.Context?.AddString($"Removed {removed} duplicate pin(s).");
                    Log.LogInfo($"dedupe: removed {removed} duplicate pin(s).");
                });

            new Terminal.ConsoleCommand("carturpins_audit",
                "Reports how many records point at a pin, how many had to be moved onto one, and how many describe nothing.",
                args =>
                {
                    string report = PinRecord.Audit();
                    args.Context?.AddString(report);
                    Log.LogInfo($"audit: {report}");
                });

            new Terminal.ConsoleCommand("carturpins_zoom",
                "Dumps the zoom and size numbers behind pin scaling, plus a sample of pins as they are actually drawn. Run it once zoomed in and once zoomed out.",
                args =>
                {
                    string summary = Crowding.Describe();
                    args.Context?.AddString(summary);
                    Log.LogInfo($"zoom dump: {summary}");

                    List<Minimap.PinData> pins = MinimapAccess.GetPins(Minimap.instance);
                    int sampled = 0;
                    if (pins != null)
                    {
                        foreach (Minimap.PinData pin in pins)
                        {
                            if (pin?.m_iconElement == null || !pin.m_iconElement.gameObject.activeInHierarchy)
                                continue;

                            bool labelOn = pin.m_NamePinData?.PinNameGameObject != null
                                           && pin.m_NamePinData.PinNameGameObject.activeInHierarchy;
                            string line = $"  \"{pin.m_name}\" scale={pin.m_iconElement.transform.localScale.x:F3}"
                                        + $" size={pin.m_iconElement.rectTransform.rect.width:F1}px"
                                        + $" icon={(pin.m_iconElement.enabled ? "on" : "off")}"
                                        + $" label={(labelOn ? "on" : "off")}";
                            args.Context?.AddString(line);
                            Log.LogInfo(line);

                            if (++sampled >= 8)
                                break;
                        }
                    }

                    args.Context?.AddString($"sampled {sampled} of {pins?.Count ?? 0} pin(s).");
                });

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

            new Terminal.ConsoleCommand("carturpins_reicon",
                "Sets every pin this mod placed to the icon its category currently uses. Run it after changing icon settings, or if pins are showing artwork from an older icon sheet. Pins you placed by hand are left alone.",
                args =>
                {
                    int changed = PinRecord.RepointAllToCurrent();
                    args.Context?.AddString(changed > 0
                        ? $"Repointed {changed} pin(s) to their current icons."
                        : "Every recorded pin already carries its current icon.");
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
