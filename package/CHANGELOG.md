# Changelog

## 1.3.1

- **Renaming a pin now shows the new name straight away.** The map writes a label's text
  once, when it builds the label, and never reads the name again - so a rename from the
  pin editor left the old name on screen until the pin scrolled off and back, and a pin
  that had no name yet never got one at all. The editor now rebuilds the label the way
  vanilla's own naming does. `$`, `<` and `>` are stripped from names, as vanilla does.
- **Enter in the search box or the editor's name field no longer reaches vanilla's
  pin-naming handler.** Both boxes are clones of the map's own name field, which carried
  its handler along; pressing Enter in one could close a pin you were naming and leave it
  blank.
- **Bosses revealed by a vegvisir or a guardian stone get the mod's own altar pin** -
  Eikthyr, The Elder, Bonemass, Moder and the rest - instead of vanilla's generic boss
  marker. Recorded like any other pin, so walking up to the altar later does not add a
  second one. Off when the Boss Altar category or that boss's switch is off; vanilla's
  marker is left alone then.

## 1.3.0

**A new icon set, and the old one kept.** 153 icons, redrawn. Pins this mod placed
move onto the new art by themselves the first time you load. Pins you placed by hand
keep the exact picture you gave them — 1.2.2's sheet is still loaded underneath, and
its icons are still in the picker, listed after the new ones. Nothing picks them for
you.

**Camps pin properly.** A Fuling village used to be invisible: the mod looked for a
dungeon generator, and villages stopped having one. Now Fuling villages, abandoned
villages and farms, Charred fortresses and the Deep North village are all pinned by
name. Greydwarf camps are off by default — they are the most common location in the
Black Forest, 450 attempts a world against 405 for Fuling villages.

**Named minibosses get their own pins.** Lord Reto, Brenna, Geirrhafa, Zil & Thungr
and the Fallen Valkyrie. They used to pin as generic Charred, Skeleton and Fuling
spawners, if at all. On by default, and they do not need the Spawner category on —
one you fight once is not the same as a greydwarf nest.

**Your bed is marked as home.** Set your spawn and the bed gets a house pin that
stays there. Vanilla moves a single marker to whichever bed you slept in last, so
the outpost you used a week ago left nothing behind. Vanilla's marker gets the same
house icon so there is one house, not two markers stacked.

**Mined-out deposits stop lying to you.** Ore never respawns, so a pin on a worked-out
node is a walk across the map to an empty hole. Those pins are removed once the
deposit is gone — only pins this mod placed, and only while the game has that area
loaded, so a node you simply walked away from is never mistaken for a mined one.

**Dungeons tick off when you have emptied them.** Every chest inside empty and every
mud pile mined, and the pin greys out the way one you ticked by hand does. The game
tracks nothing of the sort itself: dungeon spawners respawn on a timer, so what you
took is the only lasting record of having been through. Unticks if a chest refills.
Per kind, so Sunken Crypts can tick and Frost Caves need not.

**The Deep North reads properly.** Winding Tunnels, Gates of Morkhalla, the Aesir
Passage and Kall Fimbulbringer's altar, the Deep North village, the Ancient Altar.
Mork Halla and Bear Cave were pinning as a generic staircase.

**Every pin can be coloured, resized and faded.** Shift-click a pin: eight colours,
a size slider and an opacity slider, alongside the icon grid and the name. Nothing
applies until you confirm, and Cancel throws it away. Works on pins you placed by
hand as well as ones the mod placed.

**Ore pins are coloured by what the ore is.** Copper warm brown, tin pale, iron dull
grey, flametal orange, sulphur yellow. Obsidian, tar and black marble are lifted
towards grey rather than drawn true - the map is dark, and a black pin on it is a
hole rather than a marker. A colour you set yourself always wins.

**Crowded pins shrink, and piled-up names get out of each other's way.** Pins sitting
on top of each other scale down, never below half. Where two labels overlap the rarer
name is kept, because CHEST appears forty times and says less than CRYPT - and a pin
under your cursor always shows its name, so nothing is unreadable for long. Icons are
never moved: a pin stays where the thing is.

**A switch for every kind.** Not just per category: every dungeon, camp, boss,
trader, spawner, miniboss, landmark, plant and ore type has its own on/off switch,
generated from the same tables that do the matching — so the menu can never fall
behind what the mod recognises.

**Maypoles are pinned**, and nothing you built ever is.

**Shift-click a pin to rename it or change its icon.** It always worked; nothing said
so. The map now says so, above the icon grid.

**The icon picker is rebuilt.** It sits beside vanilla's own pin buttons instead of
across the map from them, every cell has a border that turns gold when selected and
lifts under the cursor, and the grid scrolls more than twice as fast. Old pin names
that still read as "$piece_chestwood" are resolved on load.

Fixes: the seven guardian stones at the spawn temple put seven pins on top of each
other and are now left to vanilla's own marker · a vegvisir standing inside a
location no longer outranks the location, so a Morgen Hole is a dungeon rather than a
runestone · pin labels are translated instead of reading "$enemy_eikthyr" ·
spawner pins from older versions repair their own icons when you walk past · the
icon grid scrolls half again as fast · carturpins_reicon resets every pin the mod
placed to its category's current icon, for anyone who changes their icon settings.

## 1.2.2

- Moved the links to my other mods to the top of the page.

## 1.2.1

- Added links to my other mods.

## 1.2.0

**Names.** Spawners said "Skeleton" where they meant "Skeleton Spawner", and the
eleven skeleton variants each said something different - "Skeleton Night Noarcher",
"Skeleton Meadows Night Noarcher". Every spawner now asks what it actually spawns and
says so: "Boar Spawner", "Greydwarf Elite Spawner", and the minibosses by name -
"Brenna Spawner", "Lord Reto Spawner". 99 of 103 spawner labels changed.

The boss stones read "BossStone_Eikthyr" and now read "Eikthyr Guardian stone".
Wisps, the Bog Witch, boss altars and random-loot pickables were showing internal
names or the literal word "None"; all of them now say what they are.

**Ore.** 33 things counted as ore and 16 of them were not - a rusty crypt gate, a
cauldron, a barrel, a weapon rack, a pickaxe hung on a wall, the giants' swords and
helmets. All of them drop scrap when broken, which is not the same as being a
deposit. Now 17, and every one is something you mine.

**Lore runestones**, the eleven story stones, can be pinned. Off by default.

**Settings.** Roughly 100 entries down to about 40. Icons are a dropdown showing the
icons instead of a slider you dragged from -1 to 82. Per-category spacing and the
timing knobs moved to Advanced. Nothing was lost, it is just not all on screen.

Icon settings are stored by name now (`Icon = Pickaxe`), so your previous icon
choices reset to the defaults once.

**Fixes.** Hildir crypts were labelled "Crypt"; every charred ruin claimed to be the
Charred Fortress. Eleven dungeon and camp names that never matched anything were
removed.

## 1.1.1

Rebuilt for Valheim 1.0.7, and the diagnostics no longer ship.

**Console commands work again.** Valheim 1.0.7 added a parameter to
`Terminal.ConsoleCommand`'s constructor. C# resolves optional arguments at compile
time and writes the exact parameter count into the DLL, so the 1.1.0 build referenced
a constructor that no longer exists and threw `MissingMethodException` while
registering commands. That aborted `Awake` before the "loaded" line and took
`carturpins_clear` and `carturpins_count` with it. Pin placement was unaffected,
since the Harmony patches are applied earlier. The source never named those
parameters, so a recompile is the whole fix.

**The log is quiet now.** The probe tooling was being built into release. On a normal
session this mod produced 3,569 of 4,857 log lines - 73% of the file - almost all of
it one `Drain:` counter printed on every queue tick, plus an `AddInstance diag:`
report every 15 seconds and a one-shot node dump that was **on by default**. None of
it was useful to a player, and volume is exactly what makes a log useless to read
when something has gone wrong.

Release builds now contain none of it: no `carturpins_probe`, `carturpins_locations`
or `carturpins_catalog` commands, no `Diagnostics/AutoProbeOnSpawn` option, and no
per-tick logging. What remains is the load line, the catalog summary, pin events, and
warnings.

`carturpins_clear` and `carturpins_count` are unaffected - those are features, not
diagnostics.

## 1.1.0

First public release.

- Automatic labelled pins for ore, dungeons, caves, camps, beehives, runestones,
  chests, spawners, leviathans, traders, wisp fountains and high-value pickables.
- Emptied chests switch to a looted icon and switch back if refilled.
- 83 selectable icons, one per category, on top of the vanilla pin types.
- `carturpins_clear` and `carturpins_count` console commands.
