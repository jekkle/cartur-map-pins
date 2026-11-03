# v2.2 results so far (branch `v2.2`, not merged, nothing published)

Provenance: **[measured]** in Valheim (Steam build 26249040, Dev profile, pilot character `carturtest`, 1920x1080), **[simulated]** outside the game, **[not verified]** not run.

## Built and checked
| Item | Result |
|---|---|
| `dotnet build` Debug and Release | succeeds, 0 errors |
| Unit tests (`tests/MapPins.Tests`, 12) | pass **[simulated]** |
| `releasecheck.ps1` | **not run** |

## Phase 1, zoom **[measured]**, Debug build with pilot wheel hook
- One notch in: x0.9434 (=1/1.06); out: x1.0600; Shift notch: x0.9804 (=1/1.02). Ten notches: x0.5584 / x1.7908, exactly 1.06^10.
- Anchor, pointer at (1400,400), 3 cycles of 4 notches in and out: world point under the pointer moved 0.00 m (0.00 px) every step.
- Limits: 80 notches in stops at 0.0150 (the game's min), 20 more notches move the centre 0.000 m; out stops at 1.0000 (max). Never outside 0.015..1.
- Settling: a 10-notch change settles in about 1 s including the 0.5 s stillness window the test waits for.
- Not tested: trackpad/hi-res wheel, other resolutions, controller.

## Phase 2, clusters **[measured]** on the pilot's unsaved test pins
- List opens from a badge click, 11 rows from a 47-pin badge (rows are distinct pins; the title count includes merged repeats, rows now show "xN"), search narrows 11 to 3 and back, picking a row glides the centre onto the pin (0.0 m off) and closes the list.
- Spiderfy: fans 5-11 pins with lines, picking one zooms to it and closes; a zoom change collapses it.
- Cache fix **[measured]**: a member re-iconed with the pin count unchanged left its group (7 -> 6 members) on the next pass.
- Reopen **[measured]**: closing and reopening the map mid-zoom stops the animation where it was, offset back to 0, no further movement.
- Click outside the list **[measured]**: with the pointer off the panel, a click (game-side test flag plus a real OS click, window focused) closes the list; the map stays Large.
- Escape **[measured, indirectly]**: the OS Escape key sent with keybd_event did not reach the game (map stayed open). Vanilla's Escape on the large map calls `SetMapMode(Small)`; calling that with the list open closed the list. The key itself is not verified.
- Mixed mode **[measured]** on real records (Ore:Copper, Ore:Tin, Chest:Buried, Runestone:Stone, Portal:Test, 2 m apart): off = no badge; on = one 4-pin badge, mixed, portal left out, list title "4 pins  Ore 2, Chest 1, Runestone 1". Records with a bare key (no "Category:Sub") count as unclassified and never mix.
- Drag during zoom **[measured]**: 8 notches while a drag was held for 40 frames; zoom reached its target, centre finite, largest one-frame centre step 38 m at about 6.7 px per frame of pointer motion (drag, not a jump).
- Log out and back in **[measured]** (same test world; a second world is not available to the pilot): new Minimap object, 0 cluster leaders, list closed, zoom steps correct in the new session.
- Other map mods **[measured]**: Harmony owners on `Minimap.UpdateMap` are only Map Pins. `ZInput.GetMouseScrollWheel` has another mod's auto-named prefix ahead of ours and ConfigurationManager's postfix. WorldAtlas (a prototype in Dev) loaded in the runs and did not interfere. Nothing else patching the map was installed, so a mod that writes `LargeZoom` was not exercised.
- Not tested: list while switching to a genuinely different world, controller, trackpad/hi-res wheel, other resolutions.
- Known limit: a pin with several same-icon pins within one icon's width stays in a badge even at the zoom limit; the list then picks it but the badge remains.

## Phase 3, labels
- Priority classes (portals/camps/bosses/dungeons/traders/home claim label space before rarity): built. No scene tested that has a portal among chest labels.
- Candidate positions (above/left/right): **not built.** It needs a probe of whether the game rewrites the label offset (audit S1).

## Phase 4, performance (`mapsweep`, `fps`) **[measured]**
- 8,357 test pins, map open, at rest: 2.1.3 mean 30.1 ms / median 22.7 / p95 78; 2.2 (Debug build) 28.7 / 22.5 / 78. Same within noise.
- `mapsweep` flicker at 755 pins, label flicker pins: 2.1.3 Release 0 and 6; 2.2 Release 5, 6, 6; 2.2 **Debug** 21-31. The Debug build is slower because of its diagnostics code, so compare Release only. Icon flicker 0 in all Release runs.
- `mapsweep` at 8.4k pins runs about 1 fps for both versions and its flicker counts are not meaningful there.
- Not done: the benchmark command, 100/1k/5k/10k/20k scenes, GC allocation counts, per-pass timing.

## Phase 5
Skipped (decision D10).

## Housekeeping found
- The pilot commands that turn `ClusterPins` on do not turn it back off and the setting persists in the Dev config. Reset to false by hand after the runs.
- New pilot file: `cartur-testpilot/src/Pilot.V22Map.cs` plus one `case "v22"` line in `Pilot.Live.cs` (uncommitted there, in a repo with other sessions' staged changes).
