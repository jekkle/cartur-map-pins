# Cartur's Map Pins

BepInEx mod for Valheim. Automatically drops labelled map pins on useful world
objects as you discover them: ore deposits, dungeon and cave entrances, wild
beehives, runestones, high-value pickables, and (optionally) boss altars.

Pins are permanent — saved to your character's map like a hand-placed pin — and
they **stay** after you clear a node, so you can tick them off yourself on the
map when you're done with them.

## How it works

Two detection paths, because the game exposes these things in two different ways.

**Ore, beehives, pickables** — `ZNetScene.AddInstance(ZDO, ZNetView)` is public
and fires exactly once per spawned networked object (from the last line of
`ZNetView.Awake`), so one hook covers every spawn path. It also fires for every
arrow and dropped item, so the first thing it does is a single `int` hash-set
lookup and nothing heavier runs before that.

**Dungeons, boss altars, runestones** — no hook at all. `Location` keeps a
private static `s_allLocations` registry (the same pattern as
`Character.s_characters`), and a `Location` only `Awake()`s when its zone loads
(~64–96m), so that list is inherently proximity-gated and small.

Both feed a throttled ~3 Hz tick that only pins once you're actually within
`DiscoveryRadius`. Objects load from further away than you can see, so pinning
straight from the spawn hook would mark things that merely loaded behind you.
Anything still too far is re-queued and pins when you walk up to it.

### Things this gets right that are easy to get wrong

- **Prefab names are never hardcoded.** Not one ore/pickable/beehive prefab name
  exists as a string literal anywhere in `assembly_valheim.dll` — they're Unity
  asset references. The whitelist is built at runtime from `ZNetScene.m_prefabs`
  by **component presence**, which is self-maintaining across game updates and
  picks up modded content for free.
- **Dungeon interiors are skipped.** `Location.Awake` instantiates interiors
  **5000m straight up** (vanilla's own test is `position.y > 3000f`), and an
  interior shares its surface zone's X/Z — so without a height gate, a crypt full
  of ore would dump a cluster of pins onto the zone centre.
- **Wild vs player-built beehives** is read straight off the ZDO
  (`zdo.GetLong(ZDOVars.s_creator, 0L) == 0`), *not* via
  `Piece.IsPlacedByPlayer()`. `Piece.m_creator` is populated in `Piece.Awake`,
  Unity doesn't guarantee component `Awake` order, and a built hive read too
  early looks wild.
- **`AddPin`, not `DiscoverLocation`.** The latter always fires a `MessageHud`
  toast, which would spam the corner of the screen during bulk discovery.
- **Our own dedupe.** `Minimap.HaveSimilarPin` is private *and* hardcoded to a 1m
  radius — far too tight for an ore field where deposits sit metres apart. Hence
  the per-category `DedupeRadius`.
- **`ZoneSystem.m_locationInstances` is deliberately not used.** It's populated
  server-side only, so it's empty on a client connected to a dedicated server —
  and it would be a whole-world reveal anyway.
- **Labels are stored as raw `$token` strings.** `Minimap.PinNameData` runs pin
  names through `Localization.Localize` at render time, so storing
  `$location_forestcrypt` displays "Burial Chambers", correctly translated, for free.

## Boss altars are off by default

Vanilla already pins boss altars: `Minimap.UpdateLocationPins` polls
`ZoneSystem.GetLocationIcons` every 5s and pins any location whose `ZoneLocation`
has `m_iconAlways` or `m_iconPlaced`. That's the `Minimap: Adding unique location`
line in your log. Enabling this category adds a *second* marker on top.

It's still offered because vanilla's version is weak — `save: false`,
`PinType.None`, and an **empty name**, so no hover label — and on a dedicated
server the client's icon list is only sent at connect, so a newly generated altar
doesn't show until you relog. Turn it on if you want a named, saved, tickable
pin; leave it off to avoid the duplicate.

## Icons

Only vanilla pin types are used — no custom sprites — so there's no risk to your
saved map data. `Boss`, `Death` and `Bed` are unambiguous; `Icon0`–`Icon4` are
the five generic icons you cycle through when placing a pin by hand, and the enum
records nothing about which artwork is which. Every category's `PinType` is
therefore config-exposed: place one pin of each of the five in game, see which is
which, and set them to taste without a rebuild.

Consequence of the vanilla-only choice: all ore shares one icon. The **label**
distinguishes them ("Copper deposit" vs "Tin deposit").

## Build

Requires .NET 8 SDK, a Valheim install, and BepInEx.

```
cd src
dotnet build
```

Managed DLLs are read from the raw Steam install (`VALHEIM_INSTALL`); the built
plugin deploys to the r2modman `Default` profile (`R2MODMAN_PROFILE`). Override
either if yours differs:

```
dotnet build -p:VALHEIM_INSTALL="D:\SteamLibrary\steamapps\common\Valheim" -p:R2MODMAN_PROFILE="C:\Users\you\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\MyProfile"
```

Fully quit and relaunch Valheim afterwards — BepInEx only scans plugins at startup.

## Config

`BepInEx/config/com.jekkle.valheim.carturmappins.cfg`

- `General / DiscoveryRadius` (60) — how close you must get before something pins.
- `General / ScanIntervalSeconds` (0.33) — how often pending objects are checked.
- Per category (`Ore`, `Dungeon`, `BossAltar`, `Beehive`, `Runestone`, `Pickable`):
  `Enabled`, `PinType`, `DedupeRadius`.
- `Pickables / …` — subgroup toggles. Only `HighValue` (surtling cores, Yggdrasil
  shoots, eggs) is on by default; berries/mushrooms, crops/herbs, and
  branches/stones/flint are off because they'd carpet the map and bloat the save.
  `Unrecognised` catches anything unmatched (including modded pickables) — check
  the log to see what those are.

## Console commands

- `carturpins_clear` — removes every pin this mod created; hand-placed pins are
  left alone.
- `carturpins_count` — how many pins are on record.

Pins the mod places are tracked in a side-car file next to the config
(`…carturmappins.pins.txt`). That file is also the dedupe set across sessions.

The obvious alternative — tagging pins via `PinData.m_ownerID`, one of the few
fields Valheim persists — **must not be used**: the pin render loop skips any pin
whose `m_ownerID != 0` unless shared-map fade is active, so tagged pins would be
silently invisible while still accumulating in your save.

## Status

Builds clean and deploys. **Not yet launch-tested in game** — first run should
confirm the catalog counts in `LogOutput.log` (how many prefabs matched per
category), then that pins appear with correct labels, and specifically that going
*inside* a crypt produces no pins (the interior height gate).
