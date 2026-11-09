using System.Collections.Generic;

namespace CarturMapPins
{
    /// One plain sentence for every setting, shown first when the setting is hovered in a configuration
    /// manager and first in the .cfg comment, with the long description below it (the owner, 2.5: "every
    /// setting when hovered should have a simple explanation"). Written for somebody who has never seen the
    /// setting: what it does, in everyday words, no other setting names.
    ///
    /// Looked up by the setting's key, then by its page. The per-kind switches and icons are generated, one
    /// per pin kind, so they are covered by the page ("@Ore Types") rather than by a line each. A translator
    /// can replace any of these with a "cfg.<Key>.plain=" line in the translation file.
    internal static class ConfigPlain
    {
        public static readonly Dictionary<string, string> English = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            // Start Here
            { "ApplyPreset", "Turn many pin kinds on or off at once. It returns to None after it runs." },
            { "OpenSetup", "Open Hugin's setup menu, where you choose what gets a pin, its icon and its colour." },

            // Map Display and Pin Size
            { "ConfirmDelete", "Ask before a right-click deletes a pin." },
            { "ZoomedInPinScale", "How big pins are when the map is zoomed in." },
            { "ZoomedOutPinScale", "How small pins get when the map is zoomed all the way out." },
            { "ShrinkPinsFromZoom", "How far you zoom out before pins begin to shrink." },
            { "HidePinsBeyond", "Hide pins that are far from your character. 0 shows every pin." },
            { "MergeRepeatedPins", "Draw one pin where several pins with the same name sit together." },
            { "PriorityZoomOut", "When zoomed far out, keep only the pins worth the walk and hide the rest." },
            { "HiddenPins", "Hide every pin with one of these names. They stay on your map and in your save." },
            { "PinSharing", "Choose which of your pins a cartography table shares with other players." },
            { "NewDefaultColours", "Give new pins the colour of what they mark." },

            // Map Zoom
            { "SmoothMapZoom", "Make the large map's mouse-wheel zoom smooth and zoom towards the pointer." },
            { "MapZoomStepPercent", "How much one turn of the mouse wheel zooms the map." },
            { "MapZoomDurationMs", "How long a zoom takes to settle, in thousandths of a second." },
            { "ZoomPreset", "Pick a ready-made wheel speed. It returns to None after it is applied." },
            { "PrecisionZoomPercent", "How much one wheel turn zooms while the precision key is held." },
            { "PrecisionZoomKey", "Hold this key while using the wheel for finer zoom steps." },
            { "CursorCenteredZoom", "Zoom towards the mouse pointer instead of the middle of the map." },

            // Pin Clusters
            { "ClusterPins", "Group crowded pins into one pin with a number on it when you zoom out." },
            { "ClusterClickAction", "What happens when you click a group's number." },
            { "MixedCategoryClusters", "Let different kinds of pins share one group." },
            { "SpiderfyMaxPins", "The biggest group that fans out. Bigger groups open the list." },
            { "ShrinkCrowdedPins", "Make pins that overlap smaller so you can tell them apart." },

            // Pin Names
            { "ShowPinLabels", "Show the name under each pin this mod places." },
            { "HideCollidingLabels", "Hide a name when it would be drawn on top of another name." },
            { "SmartLabelPlacement", "Try moving a name above or beside its pin before hiding it." },
            { "HideGroupedLabels", "Don't show the name on a group's pin until you point at it." },
            { "LabelScale", "How big pin names are drawn." },
            { "HideLabelsFromZoom", "Stop showing pin names once you zoom out past this point." },

            // Controls and automatic pins
            { "AutoPinning", "Place pins automatically. Off pauses it." },
            { "AutoPinningKey", "The key that turns automatic pinning on or off while you play." },
            { "HidePinsKey", "On the large map, the key that steps through all pins, mine only, auto only and none." },
            { "HideLabelsKey", "On the large map, the key that hides or shows every pin's name." },
            { "SetupKey", "On the large map, the key that opens Hugin's setup menu." },
            { "DiscoveryRadius", "How close you must get, in metres, before something is pinned." },
            { "ScanIntervalSeconds", "How often the mod checks what is near you, in seconds." },
            { "ResetPins", "Remove every pin this mod placed in this world so they are pinned again. Returns to No." },

            // Custom icons and the picker
            { "MapPicker", "Show the grid of custom pin icons on the large map." },
            { "MapPickerRight", "How far from the right edge the icon grid sits." },
            { "MapPickerBottom", "How far from the bottom edge the icon grid sits." },
            { "AutoProbeOnSpawn", "Write a large diagnostics file a few seconds after loading. For mod developers." },

            // Per-category keys shared by every category page
            { "Enabled", "Turn automatic pins for this on or off." },
            { "Icon", "Which icon the pins use on the map." },
            { "PinTypeFallback", "The game's own pin icon used when the custom icons are off." },
            { "MinimumSpacing", "Don't place a second pin closer than this many metres to the first." },

            // Page-specific
            { "TintByType", "Colour each pin by what it is." },
            { "ForgetMined", "Remove an ore pin once that deposit has been mined out." },
            { "TintBerriesByType", "Colour each berry pin by its berry." },
            { "HighValue", "Pin the valuable pickables." },
            { "BerriesAndMushrooms", "Pin berry bushes and mushrooms." },
            { "CropsAndHerbs", "Pin wild crops and herbs." },
            { "BranchesStonesFlint", "Pin branches, stones and flint." },
            { "HighValueIcon", "The icon valuable pickables use." },
            { "BerriesIcon", "The icon berry bushes use." },
            { "MushroomsIcon", "The icon mushrooms use." },
            { "CropsIcon", "The icon wild crops and herbs use." },
            { "JunkIcon", "The icon branches, stones and flint use." },
            { "OtherIcon", "The icon other pickables use." },
            { "Unrecognised", "Pin pickables the mod does not have a name for." },
            { "TickWhenLooted", "Tick a dungeon's pin off once you have looted it." },
            { "LootedIcon", "The icon an emptied chest gets." },
            { "LootedDisplay", "How a looted chest's pin looks: faded, a new icon, or removed." },
            { "ReplaceBedMarker", "Draw the mod's own icon for your bed." },
            { "Boats", "Keep a pin on each of your boats as it moves." },
            { "Carts", "Keep a pin on each of your carts as it moves." },
            { "Tames", "Keep a pin on your named tames and anything you can ride." },
            { "Wild", "Keep a pin on bears you have come across." },
            { "CartIcon", "The icon for your carts." },
            { "ForgetRadius", "How close you must be to a tracked thing's last spot to accept that it is gone." },

            // Whole pages, for the settings generated one per pin kind
            { "@Ore Types", "Pin this kind of ore." },
            { "@Ore Icons", "The icon this kind of ore uses." },
            { "@Pickable Kinds", "Pin this kind of pickable." },
            { "@Pickable Icons", "The icon this kind of pickable uses." },
            { "@Dungeon Kinds", "Pin this kind of dungeon." },
            { "@Dungeon Types", "The icon this kind of dungeon uses." },
            { "@Dungeon Tick", "Tick this kind of dungeon off once you have looted it." },
            { "@Camp Kinds", "Pin this kind of camp." },
            { "@Camp Types", "The icon this kind of camp uses." },
            { "@Landmark Kinds", "Pin this kind of landmark." },
            { "@Landmark Icons", "The icon this kind of landmark uses." },
            { "@Prop Kinds", "Pin this kind of prop." },
            { "@Prop Icons", "The icon this kind of prop uses." },
            { "@Spawner Kinds", "Pin this kind of spawner." },
            { "@Spawner Icons", "The icon this kind of spawner uses." },
            { "@Miniboss Kinds", "Pin this miniboss." },
            { "@Miniboss Icons", "The icon this miniboss uses." },
            { "@Boss Kinds", "Pin this boss altar." },
            { "@Boss Icons", "The icon this boss altar uses." },
            { "@Chest Site Kinds", "Pin this kind of chest site." },
            { "@Chest Site Icons", "The icon this kind of chest site uses." },
            { "@Trader Kinds", "Pin this trader." },
            { "@Trader Icons", "The icon this trader uses." },
            { "@Boat Icons", "The icon this kind of boat uses." },
            { "@Tame Icons", "The icon this tame uses." },
            { "@Wild Icons", "The icon this wild animal uses." },
            { "@Kind Colours", "The colour new pins of this kind are given. Empty for none." },
            { "@Kind Opacity", "How solid new pins of this kind are, in percent." },
        };

        /// The plain sentence for one setting: its page and key together, then its key, then its page.
        public static string For(string logicalSection, string key)
        {
            if (English.TryGetValue("@" + logicalSection + "/" + key, out string both))
                return both;
            if (English.TryGetValue(key, out string byKey))
                return byKey;
            return English.TryGetValue("@" + logicalSection, out string byPage) ? byPage : null;
        }
    }
}
