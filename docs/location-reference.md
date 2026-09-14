# Location and prefab reference

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

## prefab-reference.txt

Every prefab `ZNetScene` registers - 5175 of them - with the components that decide whether this
mod can pin one, and whether the catalog took it. The catalog dump only ever reported what it
accepted; this is what exists.

Questions it answers without launching anything:

- **Is there a maypole?** Yes, `piece_maypole`, and it is the only pole-shaped thing in the game
  besides `goblin_totempole`.
- **Is there a church?** No. Zero matches in 5175 prefabs, so the `dungeon_winding_church` icon
  has no subject.
- **Portals** are `portal`, `portal_stone` and `portal_wood`, all player-built pieces - which
  makes the portal icons hand-pin art, not auto-pin art.
- **Asksvin, Moose, Seal, Deer and Neck** all exist as creatures but none of them has a spawner,
  so nothing can auto-pin them. Their icons are hand-pick only.
- **Resin, AncientSeed and BlackMetal** are items, not world nodes. Nothing to pin.
- 26 spawners still resolve to no icon - bats, chickens, hens, blobs, dvergr variants, the Deep
  North jotun line, Frysling, Elaking, Writhan, ShadowPerson, Kvastur, cave leeches and fish.
  The sheet has no art for any of them, and most are ambient fauna.

## Regenerating

Run a Debug build and load a world; the auto-probe rewrites
`BepInEx/config/carturpins_dump.txt` on each load. Split it at the `=== ZNetScene prefabs ===`
line and copy the halves back over these two files when a game update changes the answers.
