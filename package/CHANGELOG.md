# Changelog

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
