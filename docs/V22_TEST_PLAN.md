# Cartur's Map Pins v2.2 - test plan (Stage A, nothing run)

Status marks used in reports: **PASS** (command run, output read), **FAIL**, **NOT VERIFIED** (could not be evaluated). A gate with no run is NOT VERIFIED, never PASS.
Provenance marks: **[measured]** in Valheim, **[simulated]** outside the game, **[read]** from code.

## 1. Environment

- Windows 11 dev box, Valheim Steam build id `26249040` (re-read the id at test time and record it), BepInEx 5.4.21, r2modman profile `Dev`.
- Existing in-game harness: sibling `cartur-testpilot` (`tools\run.ps1 -Live`, `tools\live-scripts\t.py`, `COMMANDS.md`), throwaway character `carturtest` only (memory: pilot never loads a real character), flat pad `testpad`/`padcam`, `mapsweep`, `fps`, `near`, `carturpins_zoom`. One game at a time; take the lock, quit afterwards, say "Dev free".
- No game launch while another session holds Dev. No deploy while `valheim.exe` runs (gate `dotnet build` on `tasklist` in the same command).
- **No automated tests exist today.** This plan adds a small xunit project for the pure math (`ZoomMath`, `LabelGeometry`) linked by source; everything else is in-game.
- No gamepad is available on this machine as far as the audit can tell: controller gates are **NOT VERIFIED** until the owner tests (or lends a pad).
- Multiplayer needs a second client or a dedicated server; not available in the pilot. The "no second-player test yet" note on Worldgate in memory suggests the same limit. Mark NOT VERIFIED unless the owner runs it.

## 2. Automated tests (new, Stage B)

`tests/MapPins.Tests` (xunit, net8 or net472, source-linked files with no Unity types). Run: `dotnet test`. All **[simulated]**.

| Test | Pass |
|---|---|
| `StepLog` convergence | Reaches `|ln(t/c)| < 0.0015` and reports Done within `2 * duration` for dt = 4, 8, 16.7, 33, 50 ms |
| Frame-rate independence | Position after 160 ms of simulated time at 30/60/144 fps agrees within 0.2% |
| Retarget mid-flight | Continuous: |z[t+1]-z[t]| bounded; no overshoot past either target |
| Limit clamp | Target requests beyond [min,max] never produce a value outside |
| Reverse direction | One notch in then one notch out returns to the original target (our factors are exact inverses, unlike vanilla's x0.9 / x1.1): assert within 1e-6 |
| Anchor round trip | Random (C, n, aspect, Zold, Znew, span): world point under `n` before equals after, error < 1e-3 m |
| 200 alternating notches | Accumulated anchor error < 1e-3 m (no drift) |
| Label geometry | Candidate rects: no overlap with own icon, inside bounds when requested, deterministic order |
| Checksum | Any single change of pos/type/name/checked/count changes it; identical inputs identical |

## 3. In-game: zoom (Phase 1)

New pilot commands: `zoomlog` (per event: wheel value, target, current, offset), `zoomanchor <notches> [shift]` (inject synthetic wheel events at a fixed pointer, compare the world point under the pointer via the game's own `ScreenToWorldPoint` before/after), `zoomstep`.

| ID | Scenario | Pass |
|---|---|---|
| Z1 | Measure one wheel notch and one trackpad swipe raw value | Recorded in `NotchUnit` comment; **gate to code** |
| Z2 | One notch | Target changes x1/1.06 exactly once; visible zoom reaches 98% in 160 ms +-1 frame |
| Z3 | 10 rapid notches (<100 ms apart) | Target = 1.06^10 clamped; no step skipped, no extra |
| Z4 | Reverse mid-animation | Zoom reverses without snapping; ends at the new target |
| Z5 | Shift notch | Factor 1.02; Shift+click on a pin still opens the editor |
| Z6 | Hold at min and max, 20 more notches | `LargeZoom` stays at the limit; `m_mapOffset` unchanged after the first clamp (no drift) |
| Z7 | Zoom with pointer near each edge and corner | Anchor error < 1 px at rest, 5 pointer positions x 10 notches |
| Z8 | Drag then zoom; zoom then drag; drag during animation | Drag wins, no jump when released |
| Z9 | Close map during animation, reopen | Offset is vanilla zero, zoom = last `LargeZoom`, no residual animation |
| Z10 | Change `MapZoomStepPercent`/`SmoothMapZoom` mid-animation | Next frame obeys; off = vanilla within one frame |
| Z11 | Wheel over icon picker/editor/inspector | Map does not zoom, list scrolls (existing behaviour kept) |
| Z12 | M/Z keys | Vanilla 50% step; target resyncs; no snap-back |
| Z13 | Another mod writing `LargeZoom` (any installed map mod) | No fight (resync), log shows the foreign patch warning |
| Z14 | Gamepad zoom/pan (**manual**) | Unchanged vanilla behaviour |
| Z15 | Resolution/aspect/UI-scale change (1080p, 1440p, ultrawide, UI scale 50/100/150) then Z7 | Same anchor tolerance |
| Z16 | Frame time: 1k, 5k pins, 20 notches zoom sweep | `fps` avg and worst 5% within noise of baseline (baseline recorded in Stage B step 0) |

**Anchor tolerance:** agreed default < 1 screen pixel at rest after the animation ends; < 3 px at any frame during it. (Owner may tighten; D2.)

## 4. In-game: clustering (Phase 2)

Pilot uses `Clusters.IncludeAll` for unsaved test pins; real-record checks use `carturtest` world records.

| ID | Scenario | Pass |
|---|---|---|
| C1 | Rename a member pin (editor) without changing count | Popup/inspector shows the new name next pass; group unchanged otherwise |
| C2 | Re-icon a member to another type | It leaves the group (same-icon mode) next pass |
| C3 | Delete one pin and add one in the same frame (count equal) | Groups rebuilt; no group references a removed `PinData` (assert on `m_pins.Contains`) |
| C4 | Tick a member (gamepad path `JoyTabLeft`) | It leaves the group |
| C5 | Search text on/off; matching pins leave, return on clear | Existing semantics |
| C6 | Switch world / character / logout-login | `Leaders/Groups` empty after; no cross-world badge |
| C7 | Pan across a cluster at fixed zoom | Membership and leader identical every frame (log hash) |
| C8 | Zoom sweep | `mapsweep`: flicker counts not above 2.1.3 baseline; leader changes counted separately and not above baseline |
| C9 | Mixed mode off | Output identical to 2.1.3 on the same scene (diff the Leaders dump) |
| C10 | Mixed mode on, scene with portal/home/boss/dungeon/camp within clusters of ore/chests | Tier-0 never absorbed; hand/shared pins never absorbed |
| C11 | Mixed cluster inspector breakdown | Sums to the total; each row matches its pin's real record category |
| C12 | Inspector: open, scroll 500 rows, filter, select, Escape, click outside, map close | Row pool size stays <= 16 objects; selecting centres/zooms; no pin mutated (compare record file hash before/after) |
| C13 | Inspector over the picker/editor | Only one panel interactive; wheel goes to the panel under the pointer |
| C14 | Spiderfy: 12-pin cluster expand; 13-pin cluster | 12 expands; 13 opens the inspector |
| C15 | Spiderfy collapse on Esc, zoom change, drag start, outside click, map close | Always collapses; real pins untouched |
| C16 | Right-click delete while spiderfied | Cannot delete a spiderfy icon; underlying pins unaffected |
| C17 | Existing click behaviour with `ClusterClickAction=Zoom` | Identical to 2.1.3 |
| C18 | Controller inspector/spiderfy (**manual**) | NOT VERIFIED until tested |

## 5. In-game: labels (Phase 3)

| ID | Scenario | Pass |
|---|---|---|
| L0 | S1 probe: write text-child offset, read back after 2 passes | Offset persists; else Phase 3 candidate placement is dropped |
| L1 | Two colliding names with space above | Below-rejected label goes Above, rect does not overlap any shown label (geometric check from the game's rects) |
| L2 | Label at screen edge | Never placed outside the map image rect |
| L3 | Label beside cluster badge | Does not overlap the badge rect |
| L4 | Zoom sweep | No side flips between adjacent frames beyond the hysteresis band; flip count <= baseline |
| L5 | Localised names (switch to a long-text language, e.g. German/Russian), `LabelScale` 0.5/1.0/1.5, UI scale | Rects use live preferred size; no overlap in L1 scene |
| L6 | Portal among 40 chest labels | Portal label always shown |
| L7 | Search a name | Matches shown, non-matches hidden, no unexplored info |
| L8 | Pin hidden by hide key / out of range | No orphan label (count active label objects without a drawn icon = 0) |
| L9 | `SmartLabelPlacement=false` | Identical to 2.1.3 (diff shown-label set on the same scene) |

## 6. Performance (Phase 4)

`carturpins_bench` as in the plan. Scenes: 100 / 1k / 5k / 10k / 20k x {uniform, clumped, dense-mixed, identical-names, search active}. Per scene record, on the same machine and Valheim build, **[measured]**:

- fps average and worst 5% during a scripted 10 s zoom sweep and 10 s pan sweep (not ms; memory: report fps),
- per-pass ms of `Crowding.Measure`, `Clusters.Measure`, `LabelCrowding.Apply`, `PinFade.Step`, `PinHover.Find`,
- `GC.CollectionCount(0)` delta and total allocated bytes per pass (use `GC.GetTotalMemory(false)` deltas around a window; label as approximate),
- cluster rebuild count and duration, visible marker count.

Pass criteria (proposed; the owner may change them, D9): 5k pins keeps worst-5% fps within 10% of 2.1.3 on the same scene; no per-frame managed allocation in steady state with the map idle (0 bytes/frame in the three passes); smooth zoom does not raise worst-5% fps cost by more than 10% versus instant zoom on 5k. If a bar fails the fix is chosen from the plan's measured-only list.

## 7. Compatibility and regression

| Area | Check |
|---|---|
| Existing save | Open a 2.1.3 world/character, compare `PinRecord` side-car and the game's pin list before and after a session (hash) - identical |
| New save | Fresh world, place, edit, delete, undo (Ctrl+Z) |
| Multiple worlds / characters | Switch; no stale cluster/label state (C6) |
| Multiplayer, dedicated server without the mod | **NOT VERIFIED** unless owner runs: pin sharing modes unchanged; our patches are client-only (no RPC added) |
| Vanilla user pins | Never clustered, never hidden by clustering |
| Custom icons / indices | Icon indices unchanged in records |
| Fog of war | Cluster/label/API expose only pins already on the map; no pin created |
| Minimap vs large map | Small map: no zoom smoothing, no clusters, no spiderfy; `Clusters.Measure` early-return path |
| Other Harmony map mods | At least one other installed map mod enabled with a patch dump (`Harmony.GetAllPatchedMethods`) logged; Z13 |
| `releasecheck.ps1` | Exit 0 against the installed game (**run per build**) |
| Version | Plugin version unchanged in untested builds |

## 8. Reporting
Each phase report lists gates as PASS / FAIL / NOT VERIFIED with the command and its output line, benchmark tables labelled measured or simulated, and the Valheim build id.
