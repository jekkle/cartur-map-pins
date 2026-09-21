# Cartur's Map Pins

Pins the world onto your map as you explore it, labelled, so you can find your way
back to things without writing them down.

![A Meadows map with pins](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/map-overview.jpg)

Walk near an ore deposit, a crypt entrance, a wild beehive, an abandoned chest —
it lands on your map with a name on it. Pins are saved to your character like
hand-placed ones, and they stay put after you clear the node so you can tick them
off yourself.

## What gets pinned

Ore deposits · dungeon and cave entrances · Fuling villages, Greydwarf camps and
Charred fortresses · wild beehives · runestones · abandoned chests · named
minibosses · leviathans · traders · maypoles · your bed, as home · spawners ·
wisp fountains · high-value pickables · boss altars · Deep North dungeons by name.

Every category is a switch. Berries, mushrooms, crops and surface flint are **off
by default** — they would carpet the map and bloat your save.

![Ore pins coloured by what the ore is](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/map-close.jpg)

## Changing a pin's icon

**Shift-click any pin** to open an editor beside it: rename it, pick a different
icon, choose a colour, set its size and opacity. Works on pins this mod placed and
on pins you placed yourself.

![The icon picker](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/icon-picker.jpg)

There is also an icon grid on the large map, next to vanilla's own pin buttons, for
choosing what a hand-placed pin will look like. Drag the small gold square in its
top left corner to move the whole panel, and it stays where you put it. Only the
square drags, so clicking an icon still picks the icon.

## Keeping the map readable

A world you have played for a while fills up, and an ore field is a dozen separate
deposits — so it was a dozen identical pins in one white blob. Pins with the same
name standing on the same patch of ground now draw as one. Zoom in and they
separate again. Nothing is deleted: every pin is still on the map, still saved and
still searchable.

Pins are full size until the map is 15% zoomed out and shrink to 60% at the
whole-world view, and names stop being drawn past 20% out. Both points are
settings, and both can be turned off. Sizes glide and pins fade rather than popping
in and out.

There is also a switch to draw pins with **no names at all**, if you want the map to
read as icons — the names stay searchable, they are just not drawn. And one to stop
drawing pins further than a set distance from your character, off by default.

Chests you have emptied fade to half, so a chest you have been through reads as
dealt with without losing its place.

## Death markers

The game drops a marker where you died and never takes it away, so a long save ends
up with markers for graves that are long gone. Loot the grave and its marker goes
with it.

## Finding a pin

Type in the search box across the top of the map. Everything that doesn't match
fades, so you can see where the copper is without losing the shape of the map.

![Searching pins](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/search-bar.jpg)

## Notable bits

- **153 icons to pick from.** Every category and kind takes its own.
- **Emptied chests change icon.** Loot a chest and its pin switches to a looted
  marker. Refill it and it switches back.
- **Mined-out ore pins remove themselves.** Deposits never respawn, so a pin on a
  worked-out node is a walk to an empty hole.
- **Dungeons tick off** once every chest is empty and every mud pile mined.
- **Crowded pins get out of each other's way.** Stacked pins shrink, and where two
  names overlap the rarer one is kept — CHEST appears forty times and says less
  than CRYPT. Point at a pin and its name always shows.
- **Names are properly translated.** Stored as the game's own localisation tokens,
  so a crypt reads "Burial Chambers" in your language.
- **Nothing is hardcoded to a prefab name.** What counts as ore, a beehive or a
  pickable is worked out at runtime from components, so it survives game updates
  and picks up modded content for free.

![The whole icon set](https://raw.githubusercontent.com/jekkle/cartur-map-pins/master/docs/images/icons-all.png)

## Settings

`BepInEx/config/com.jekkle.valheim.carturmappins.cfg`.

| Setting | Default | What it does |
| --- | --- | --- |
| `DiscoveryRadius` | 60 | Metres. How close you must get before something pins. |
| `ShowPinLabels` | true | Draw the name under each pin. |
| `HideCollidingLabels` | true | Stop names being drawn on top of each other. |
| `HideLabelsBeyondZoom` | 0.5 | Hide names once zoomed out past this. 1 never hides. |
| `MergeRepeatedPins` | true | Draw one icon where several same-named pins sit together. |
| `ShrinkCrowdedPins` | true | Shrink pins stacked on one spot. |
| `ZoomedOutPinScale` | 0.6 | How small pins get at full zoom-out. 1 turns it off. |
| `HidePinsBeyond` | 0 | Metres. Hide pins further than this from you. 0 draws all. |
| `Ore / TintByType` | true | Colour ore pins by what the ore is. |
| `Ore / ForgetMined` | true | Remove an ore pin once its deposit is mined out. |
| `Chest / LootedIcon` | Chest open | Icon an emptied chest switches to. |
| `Dungeon / TickWhenLooted` | true | Tick a dungeon off once it is cleared. |
| `Home / ReplaceBedMarker` | true | Mark your bed as home. |
| `CustomIcons / MapPicker` | true | Show the icon grid on the large map. |
| `General / ApplyPreset` | None | Sets every switch at once — see below. |
| `General / ResetPins` | No | Starts a world's pins over — see below. |

Per category there is also `Enabled`, an icon, and a `MinimumSpacing` — how far
apart two of the same thing must be to earn separate pins.

**Most of the list is hidden until you ask for it.** There is a switch and an icon
for every kind of thing this mod knows about, about 290 settings on their own. They
sit behind the **Advanced** tick box, leaving the seventeen categories in view.

**`ApplyPreset`** sets every switch at once. *Minimal* pins the few things worth
walking back to, *Everything* turns it all on, *Defaults* restores a fresh install.
It is a button, not a state — it returns to None once applied.

**Boss altars are off by default.** Vanilla already pins them, more weakly. Turn
this on for a proper named pin, or leave it off to avoid two markers stacked.

## Starting a world's pins over

`General / ResetPins` removes every pin this mod placed and forgets them, so each
is pinned again as you rediscover it.

Pins you placed by hand are untouched, and so is any colour or size you set through
the editor. Locations come back within seconds; ore, chests and beehives reappear
when their part of the world next loads.

It is a spelled-out dropdown rather than a tick box on purpose — a stray click here
would throw away a map somebody spent a long time filling in.

## Commands

| | |
|---|---|
| `carturpins_clear` | Removes every pin this mod placed. |
| `carturpins_reicon` | Resets this mod's pins to their category's current icon. Hand-placed pins are left alone. |
| `carturpins_count` | How many are on record. |
| `carturpins_forget_missing` | Forgets records whose pin is gone, so those places can be pinned again. |
| `carturpins_zoom` | Prints the zoom and sizing numbers behind what is drawn. For working out why the map looks the way it does. |

## If you use badgers Valheim Skies

That mod opens its menu on a hotkey without checking whether you are typing, so a
letter typed into the search box fires it. There is an optional download on this
page that fixes it — install it only if you use that mod.

## Install

Use a mod manager (r2modman / Thunderstore / Gale) and it pulls in BepInEx for you.
Manually: drop `CarturMapPins.dll` into `BepInEx/plugins`.

Client-side. The server doesn't need it and neither do the people you play with.

Pin names come out in the language you play in, and are re-worded if you change it.
A pin you have renamed yourself is left alone.

Pairs with **Cartur's Compass and Clock**, which puts these same pins on a compass
bar and drops the ones you've ticked off.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**More from Cartur:**
[HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[UI HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/) ·
[Waste Management](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Waste_Management/)
