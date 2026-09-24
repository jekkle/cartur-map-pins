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

The mod ships its own sheet of 153 icons, registered as **extra** pin types
appended after the vanilla ones — nothing vanilla is replaced, so `Boss`, `Death`,
`Bed` and `Icon0`–`Icon4` all keep working and stay selectable.

Three things have to line up for that to be safe, and only the first is obvious:
`Minimap.m_icons` is a predicate-searched list, so a `PinType` cast from an
arbitrary int resolves fine — but `AddPin` **clamps** any type beyond
`m_visibleIconTypes.Length` to `Icon3`, and the render loop indexes that array
raw. So the private `bool[] m_visibleIconTypes` is grown before any custom pin can
exist. Without that, custom pins would be silently downgraded to `Icon3` *and
written that way into your save*.

If the sheet fails to load, or `CustomIcons/Enabled` is off, every category falls
back to its vanilla `PinTypeFallback` — a bad sheet degrades to "normal pins"
rather than invisible ones.

Icons are picked per kind rather than per category, so the map distinguishes
things worth distinguishing: copper from tin, a Burial Chamber from a Frost Cave,
Haldor from the Bog Witch, a wolf den from a draugr pile. Unmatched kinds fall
back to the category icon and are logged, so a name the tables don't know yet is
a line in the log rather than a silent wrong marker.

### Rebuilding the sheet

`src/Assets/icons/order.txt` is the source of truth: one icon name per line, in
index order, each with a matching 128×128 PNG beside it. Then:

```
python tools/build_sheet.py
```

That regenerates the packed sheet, the numbered reference copy, and `PinIcon.cs`.
The enum is **generated, not hand-written** — an enum that disagreed with the
sheet would write the wrong icon into people's saves. Append to `order.txt`
rather than reordering it: the index is what gets stored in the config.

A `carturmappins_icons.png` placed next to the config overrides the embedded
sheet, so the artwork can be swapped without a rebuild.

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
- Per category (`Ore`, `Dungeon`, `Camp`, `BossAltar`, `Beehive`, `Runestone`,
  `LoreStone`, `Chest`, `Spawner`, `Leviathan`, `Trader`, `Wisp`): `Enabled`,
  `Icon`, and `MinimumSpacing` / `PinTypeFallback` under advanced.
- Per kind, overriding the category icon: `Ore Icons`, `Dungeon Types`,
  `Camp Types`, `Boss Types`, `Trader Types`, `Spawner Types`. Set one to
  `Default` to fall back to the category's own icon.
- `Ore Types / <type>` — one on/off switch per ore type, so you can pin copper
  and ignore tin.
- `CustomIcons / Enabled` — off falls everything back to vanilla pin types.
- `CustomIcons / MapPicker` — the icon grid on the large map, for pins you place
  by hand. `MapPickerX` / `MapPickerY` move the panel.
- `Pickables / …` — subgroup toggles. Only `HighValue` (surtling cores, Yggdrasil
  shoots, eggs) is on by default; berries/mushrooms, crops/herbs, and
  branches/stones/flint are off because they'd carpet the map and bloat the save.
  `Unrecognised` catches anything unmatched (including modded pickables) — check
  the log to see what those are.

## Console commands

- `carturpins_clear` — removes every pin this mod created; hand-placed pins are
  left alone.
- `carturpins_count` — how many pins are on record.
- `carturpins_reicon` — sets every pin this mod placed to the icon its category
  and kind use today. Run it after changing icon settings, or if pins are still
  showing artwork from an older sheet.
- `carturpins_forget_missing` — forgets records whose pin is no longer on the
  map, so those places can pin again. Also brings back pins you deleted on purpose.
- `carturpins_dedupe` — removes leftover duplicate pins that no record points at.
  Counts only, unless you pass `yes`.
- `carturpins_audit` — how many records sit on their pin, how many near one, and
  how many describe nothing.
- `carturpins_zoom` — dumps the zoom and sizing numbers behind pin scaling, plus
  a sample of pins as drawn. Run it once zoomed in and once zoomed out.

A further six `carturpins_*` dump commands exist behind the `DIAGNOSTICS` compile
flag and are not in a release build.

Pins the mod places are tracked in a side-car file next to the config, **one file
per world per character** — `…carturmappins.<world>-<uid>.<character>.pins.txt`.
That file is also the dedupe set across sessions, which is why it cannot be shared:
one file for everything told a second character that a world the first had explored
was already pinned, on a map that was empty. The old single
`…carturmappins.pins.txt` is adopted once, by the first world and character to ask
for a record, and then left alone.

The obvious alternative — tagging pins via `PinData.m_ownerID`, one of the few
fields Valheim persists — **must not be used**: the pin render loop skips any pin
whose `m_ownerID != 0` unless shared-map fade is active, so tagged pins would be
silently invisible while still accumulating in your save.

## Status

Released. `package/manifest.json` holds the published version; `package/CHANGELOG.md`
is the history. Packed builds are in `dist/`.

After a change, the smoke test is still to launch and read `LogOutput.log` for:

- `Registered … custom pin icons` and `Grew Minimap.m_visibleIconTypes`.
- The catalog counts (how many prefabs matched per category).
- Pins appear with the right icon *and* label — particularly that ore types,
  traders and spawners get their own icons rather than the category fallback.
- Any `No spawner subtype matched '<name>'` lines. Creature prefab names can't be
  read offline, so the spawner table is spelled from the standard names and the
  log is what corrects it.
- Going *inside* a crypt still produces no pins (the interior height gate).

Then on the map screen: the wheel scrolls the icon picker without zooming the map.
