# Changelog

## 1.1.1

Rebuilt for Valheim 1.0.7. No behaviour changes.

Valheim 1.0.7 added a parameter to `Terminal.ConsoleCommand`'s constructor. C#
resolves optional arguments at compile time and writes the exact parameter count
into the DLL, so the 1.1.0 build referenced a constructor that no longer exists and
threw `MissingMethodException` while registering commands. That aborted `Awake`
before the "loaded" line, taking all five `carturpins_*` commands with it. Pin
placement itself was unaffected, because the Harmony patches are applied earlier.

The source was already correct - it never named those parameters - so a recompile
against 1.0.7 is the whole fix.

## 1.1.0

First public release.

- Automatic labelled pins for ore, dungeons, caves, camps, beehives, runestones,
  chests, spawners, leviathans, traders, wisp fountains and high-value pickables.
- Emptied chests switch to a looted icon and switch back if refilled.
- 83 selectable icons, one per category, on top of the vanilla pin types.
- `carturpins_clear` and `carturpins_count` console commands.
