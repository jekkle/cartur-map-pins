# Location reference

`location-reference.txt` is a verbatim diagnostics dump from a live game, Valheim as of
13 September 2026, written by the Debug build's auto-probe
(`BepInEx/config/carturpins_dump.txt`).

It is here so that questions about the game's locations can be answered without launching
anything. Every one of them used to cost a restart, one field at a time.

What it holds:

- **A summary line per location** - biome, quantity, vanilla's icon flags, the name the game
  gives it, whether it is a dungeon / camp / surface, its generator, what the mod would pin it
  as, and a count of the components inside it.
- **Every field of all 232 definitions** - the `ZoneLocation` and its `Location` component, by
  reflection, in the `=== every field ===` section.
- The catalog, per-prefab map labels, the spawner table, and the game's TMP font names.

Things it settled that guesswork had got wrong:

- Only one location in the game is a "camp" by the old definition of having a dungeon generator
  and no interior. Fuling villages and Greydwarf camps are plain surface locations.
- `StartTemple` is the only location flagged `iconAlways`, which is why the mod leaves it to
  vanilla's own marker.
- The Deep North boss room announces itself as **Aesir Passage**; the Ancient Upgrade Station is
  the **Forge of Potential**; Hildir's fortress is a **Sealed Tower**.
- Nothing in the game is called a church, a maypole, a portal or a Yggdrasil root, so the icons
  drawn under those names have no location to attach to.

Regenerate by running a Debug build and loading a world; the auto-probe rewrites the file on
each load. Copy it back over `location-reference.txt` when a game update changes the answers.
