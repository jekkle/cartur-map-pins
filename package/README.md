# Cartur's Map Pins

*Free, and always will be — if it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**More from Cartur:** [HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/)

Pins the world onto your map as you explore it, labelled, so you can find your
way back to things without writing them down.

Walk near an ore deposit, a crypt entrance, a wild beehive, an abandoned chest —
it lands on your map with a name on it. The pins are saved to your character like
hand-placed ones, and they stay put after you clear the node, so you can tick
them off yourself when you're done.

## What gets pinned

Ore deposits · dungeon and cave entrances · goblin and draugr camps · wild
beehives · runestones · abandoned chests · spawners · leviathans · traders ·
wisp fountains · high-value pickables (surtling cores, Yggdrasil shoots, eggs) ·
boss altars.

Every category is a switch. Turn off what you don't care about.

Berries, mushrooms, crops and surface flint are **off by default** — they would
carpet the map and bloat your save. Turn them on if you want them.

## Changing a pin's icon

**Shift-click any pin on the map** to open an editor beside it: rename it, pick a
different icon from the grid, confirm. It works on pins this mod placed and on
pins you placed yourself.

There is also an icon grid on the large map, next to vanilla's own row of pin
buttons, for choosing what a pin you place by hand will look like.

## Notable bits

- **Emptied chests change icon.** Loot a chest and its pin switches to a looted
  marker, so cleared ones are obvious at a glance. Refill it and it switches
  back.
- **83 icons to pick from.** Each category takes an icon index, so you can set
  ore, dungeons, chests and the rest to whatever reads best for you. Vanilla pin
  types still work if you'd rather stay with them.
- **Dungeon interiors don't leak.** Valheim builds crypt interiors 5000m
  straight up, sharing their surface zone's coordinates. Without a height gate a
  crypt full of ore would dump a cluster of pins on the zone centre. This checks.
- **Nothing is hardcoded to a prefab name.** The list of what counts as ore, a
  beehive, or a pickable is built at runtime by looking at what components an
  object has — so it survives game updates, and picks up modded content for free.
- **Named labels, properly translated.** Names are stored as the game's own
  localisation tokens, so a crypt reads "Burial Chambers" in your language.

## Commands

- `carturpins_clear` — removes every pin this mod placed. Hand-placed pins are
  left alone.
- `carturpins_count` — how many are on record.
- `carturpins_forget_missing` — forgets records whose pin is gone, so those
  places can be pinned again.
- `carturpins_flag_unknown` — marks hand-placed pins carrying a custom icon with
  a warning glyph, so you can spot the ones whose art changed with the sheet.
  Runs itself once per world; this is only for doing it again.

## Install

Use a mod manager (r2modman / Thunderstore / Gale) — it pulls in BepInEx for you.

Manually: drop `CarturMapPins.dll` into `BepInEx/plugins`.

## Config

`BepInEx/config/com.jekkle.valheim.carturmappins.cfg`, written on first run.

- `DiscoveryRadius` (60) — how close you have to get before something pins.
  Things load from further away than you can see, so this stops the map filling
  in behind you.
- Per category: `Enabled`, its icon, and a `DedupeRadius` — how far apart two of
  the same thing have to be to earn separate pins.
- `Chest / LootedIconIndex` — the icon an emptied chest switches to. Set it to
  -1 to leave looted chests alone.

**Boss altars are off by default.** Vanilla already pins them. Its version is
weaker — unsaved, unnamed, no hover label — so turn this on if you want a proper
named pin, and leave it off if you'd rather not have two markers stacked.

## Compatibility

Client-side only. The server doesn't need it and neither do the people you play
with. Pins go into your own map data.

Pairs with **Cartur's Compass and Clock**, which puts these same pins on a
compass bar and drops the ones you've ticked off or emptied.
