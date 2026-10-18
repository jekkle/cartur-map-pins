# Cartur's Map Pins

Pins things onto your map as you come across them, with a name and an icon, so you can find your way back without writing anything down.

![A map with pins](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/map-overview.jpg)

## What it does

Walk within `DiscoveryRadius` (60 metres by default) of an ore deposit, a crypt entrance, a wild beehive or an abandoned chest and it lands on your map with a name on it. The pins are saved with your character like hand-placed ones. They stay after you clear the node, so you can cross them off yourself, with a few exceptions listed below.

The pinned categories are ore deposits, dungeon and cave entrances, camps and villages (Fuling villages, Charred fortresses, abandoned villages and farms, the Deep North village), wild beehives, runestones, abandoned chests, buried chests, named minibosses, leviathans, traders, maypoles, boss altars, high-value pickables and your bed, marked as home. Every category has its own switch. Some are off in a fresh install: landmarks (wells, shipwrecks, stone circles and similar), lore runestones, spawners, wisp spawners, and Greydwarf camps. Within pickables, only the high-value group is on. Berries and mushrooms, crops and herbs, branches, stones and flint, and unrecognised pickables are off, because they would cover the map and bloat your save. Vanilla already marks boss altars with an unnamed icon that is not saved. This mod's pin replaces it rather than sitting on top.

![Ore pins coloured by what the ore is](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/map-close.jpg)

Some pins follow the thing instead of marking a spot. Your boats and carts, tames you have named, anything you can ride, and bears are tracked. When the game does not have the thing loaded, the pin stays where you last saw it. Unnamed livestock is not tracked. These are under `Tracking` in the config.

A few pins tidy themselves. An ore pin is removed once the deposit is mined out, since deposits never respawn. A looted chest switches to an open-chest icon and fades to half opacity, and goes back if the chest is refilled. A dungeon pin is ticked off once every chest inside is empty and every mud pile is mined. When you loot your grave, its death marker is removed too.

## Changing a pin's icon

Shift-click any pin on the large map to open an editor beside it. You can rename the pin, pick a different icon, choose a colour, and set its size and opacity. It works on pins this mod placed and on ones you placed by hand. There are 158 icons, and every category and kind of thing takes its own.

![The icon picker](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/icon-picker.jpg)

There is also an icon grid on the large map next to vanilla's pin buttons, for choosing what a hand-placed pin will look like. Drag the small gold square in its top left corner to move the panel. It stays where you put it. Only the square drags, so clicking an icon still picks the icon.

![The whole icon set](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/icons-all.png)

## Keeping the map readable

An ore field is a dozen separate deposits, which used to be a dozen identical pins in one white blob. Pins with the same name on the same patch of the map now draw as one, and separate again as you zoom in. Nothing is deleted. Every pin is still on the map, still saved and still searchable.

Pins are full size until the map is 15% zoomed out and shrink to 60% at the whole-world view. Pin names stop being drawn once the map is 20% zoomed out. Both points are settings and both can be turned off. Pins that sit on top of each other shrink, never below half size, and where two names overlap the rarer one is kept, so CRYPT beats CHEST. Pointing at a pin always shows its name. Sizes and fades change smoothly rather than popping.

You can draw pins with no names at all, so the map reads as icons. The names stay searchable, they just are not drawn. You can also stop drawing pins further than a set distance from your character. That is off by default. The `HiddenPins` setting takes a list of names to hide without deleting the pins, and its settings row has a list to pick from your own map.

Type in the search box across the top of the map and everything that does not match fades, so you can see where the copper is without losing the shape of the map.

![The search box](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/search-bar.jpg)

## Your own short labels

If you keep a compact naming convention such as R5, B7, SC or FC, write it in `BepInEx/config/carturpins_labels.txt`, one kind per line:

```
Pickable:Raspberry=R{qty}
Dungeon:Sunken Crypt=SC
Ore:Copper={qty} {name}
```

`{qty}` is how many pins of that kind are merged into the marker at the current zoom, and is left out when there is just one. `{name}` is the normal name in your language. The file is written for you the first time you start the game, with every line commented out. The key of any pin is the first field of its line in the `.pins.txt` record in `BepInEx/config`. Changes apply the next time you load a world.

Only what is drawn changes. The saved name stays, so search, the rename box and the pin list still read "Sunken Crypt". Pins you renamed yourself keep your name, and pointing at a pin shows its full name. The labels are yours alone. Other players only see them if they use the same file.

## Names and languages

Names are stored as the game's own localisation tokens, so a crypt reads "Burial Chambers" in your language and is re-worded if you change language. A pin you renamed yourself is left alone. Names the mod makes up itself, such as Sunken Crypt, Frost Cave and Fuling Village, translate through `carturpins_names_<language>.txt`. `carturpins_names_english_template.txt` is written to `BepInEx/config` the first time you start the game. Copy it to `carturpins_names_french.txt` (or `german`, `spanish`, and so on: the game's own language name), translate the text after each `=` and restart the game. `carturpins_labels.txt` is for short labels, not translation.

What counts as ore, a beehive or a pickable is worked out at runtime from components rather than from a list of prefab names, which is why it should survive game updates and pick up modded content. Player-built beehives and containers are never pinned.

## Settings

Config file: `BepInEx/config/com.jekkle.valheim.carturmappins.cfg`.

| Setting | Default | What it does |
| --- | --- | --- |
| `DiscoveryRadius` | 60 | Metres. How close you must get before something pins. |
| `AutoPinning` | true | Place pins automatically. Off pauses it without touching any category switch. |
| `AutoPinningKey` | F8 | Key that toggles `AutoPinning` in game. |
| `ShowPinLabels` | true | Draw the name under each pin. |
| `HideCollidingLabels` | true | Stop names being drawn on top of each other. |
| `HideLabelsFromZoom` | 20 | Percent zoomed out past which names stop being drawn. 100 never hides. |
| `ShrinkPinsFromZoom` | 15 | Percent zoomed out before pins start shrinking. |
| `MergeRepeatedPins` | true | Draw one icon where several same-named pins sit together. |
| `ShrinkCrowdedPins` | true | Shrink pins stacked on one spot. |
| `ZoomedOutPinScale` | 0.6 | How small pins get at full zoom-out. 1 turns it off. |
| `HidePinsBeyond` | 0 | Metres. Hide pins further than this from you. 0 draws all. |
| `PinSharing` | Everything | What a cartography table publishes: Everything, HandPlacedOnly or Nothing. |
| `Pickables / BerriesAndMushrooms` | false | Raspberry, blueberry, cloudberry, lingonberry and vineberry bushes, and mushrooms. Off by default: turn it on to pin berries. |
| `Ore / TintByType` | true | Colour ore pins by what the ore is. |
| `Ore / ForgetMined` | true | Remove an ore pin once its deposit is mined out. |
| `Chest / LootedIcon` | Chest open | Icon an emptied chest switches to. |
| `Dungeon / TickWhenLooted` | true | Tick a dungeon off once it is cleared. |
| `Home / ReplaceBedMarker` | true | Give vanilla's spawn marker the same house icon. |
| `CustomIcons / MapPicker` | true | Show the icon grid on the large map. |
| `Tracking / Boats`, `Carts`, `Tames`, `Wild` | true | Pins that follow boats, carts, named tames and rideables, and bears. |
| `General / ApplyPreset` | None | Sets every switch at once, see below. |
| `General / ResetPins` | No | Starts a world's pins over, see below. |

Each category also has `Enabled`, an `Icon`, and a `MinimumSpacing` (under Advanced), which is how far apart two of the same thing must be to earn separate pins.

Most of the list is hidden until you tick Advanced in the config manager. There is a switch and an icon for every kind of thing the mod knows about, several hundred settings, and the seventeen categories are what you see otherwise.

`ApplyPreset` sets every switch at once. Minimal keeps ore, dungeons, chests, boss altars, minibosses and home. Everything turns it all on. Defaults restores a fresh install. It is a button rather than a state, and returns to None once applied. It changes what gets pinned from then on, and pins already on the map stay.

## Starting a world's pins over

`General / ResetPins` removes every pin this mod placed and forgets them, so each is pinned again as you rediscover it. Pins you placed by hand are untouched, and so is any colour or size you set through the editor. Locations come back within seconds. Ore, chests and beehives reappear when their part of the world next loads.

It is a spelled-out dropdown rather than a tick box on purpose, because a stray click here would throw away a map somebody spent a long time filling in.

## Commands

| | |
|---|---|
| `carturpins_clear` | Removes every pin this mod placed. |
| `carturpins_reicon` | Resets this mod's pins to their category's current icon. Hand-placed pins are left alone. |
| `carturpins_count` | How many are on record. |
| `carturpins_forget_missing` | Forgets records whose pin is gone, so those places can be pinned again. |
| `carturpins_dedupe` | Removes leftover duplicate pins no record points at. Counts only, unless you pass `yes`. |
| `carturpins_zoom` | Prints the zoom and sizing numbers behind what is drawn. |

## Compatibility and known issues

Client-side. The server does not need it and neither do the people you play with. With `PinSharing` left on Everything, a cartography table publishes the pins this mod placed along with your own.

Pairs with Cartur's Compass and Clock, which puts these pins on a compass bar.

Badger's Valheim Skies opens its menu on a hotkey without checking whether you are typing, so a letter typed into the search box fires it. The Nexus page for this mod has an optional download that fixes that. Install it only if you use that mod.

## Install

Use a mod manager (r2modman, Thunderstore or Gale) and it pulls in BepInEx for you. To install by hand, drop `CarturMapPins.dll` into `BepInEx/plugins`.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**[Discord](https://discord.gg/nd5RqpwNkz)**: bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

**More from Cartur:**
[HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[UI HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/) ·
[Waste Management](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Waste_Management/) ·
[Feeding Trough](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Feeding_Trough/) ·
[Combat Text](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Combat_Text/)
