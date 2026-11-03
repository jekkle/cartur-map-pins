# Cartur's Map Pins v2.2 - implementation plan (Stage A, not approved)

Read `V22_CODE_AUDIT.md` first; this plan cites its findings (C1..C7, S1..S4).
Nothing here is built. Every number marked **[to measure]** must be read in game before it is coded.
Ponytail ladder was run per phase: rung reached is printed in each section.

## 0. Architecture decisions

| # | Decision | Why (from the audit) |
|---|---|---|
| A1 | Extend, don't rewrite. Hook points stay `UpdatePins` Postfix and `Minimap.Update` Postfix. | Existing pipeline works and is flicker-tested (`mapsweep`). |
| A2 | Zoom = one new Prefix(+Finalizer) on private `Minimap.UpdateMap(Player, float, bool)` and a re-entrancy flag in the existing `ZInput.GetMouseScrollWheel` patch. No transpiler. | Vanilla reads the wheel *inside* `UpdateMap` (L941). A Prefix can run the animation first, then starve vanilla's wheel read. `UpdateMap` is the only reader of the large-map wheel. |
| A3 | Cursor anchor is a closed form on the vanilla linear map transform, not an iterative solve and not vanilla's disabled lerp. | Audit section 3.7: `world = centre + (n-0.5)*(aspect*Z, Z)*span`. Exact, no drift. |
| A4 | **No pan clamp.** | Vanilla has none (audit 3.8). Adding one is a behaviour change nobody asked for. |
| A5 | Pure math (smoothing step, anchor solve, label candidate geometry) written Unity-free in `ZoomMath.cs` / `LabelGeometry.cs`, so a tiny xunit project can link them. Everything touching `Minimap`/uGUI stays in the Unity-facing files. | Only place drift bugs hide; in-game verification costs a relaunch each. |
| A6 | Cluster cache invalidation = **content checksum** computed in the existing pass, replacing the string signature. No `PinRevision` patch set for Phase 2. | Ponytail rung 5 ("one-line it"): a checksum sees every mutation including other mods' direct field writes, with zero new patches. A revision counter would not. |
| A7 | Inspector reuses the existing UI vocabulary: `PanelDrag`, `IconGrid.Bronze`, `Fonts.AddLabel`, the `PinEditor` text-field pattern + `TextFocus` for input focus. | No second UI style. |
| A8 | API (Phase 5) exposes immutable snapshots from the same checksum pass. | One change detector for clusters and API. |
| A9 | Every feature is behind a setting; disabled path = early return before any allocation. | Rollback, and "disabled features incur minimal overhead". |

## 1. Phase 1 - smooth, precise zoom (highest)

Ladder rung: **7 (write the minimum)** for the controller, because the platform has nothing equivalent. Rungs 1-6 rejected: vanilla's own cursor-zoom is dead code, no zoom setting exists in the mod, no dependency offers it.

### 1.1 Facts the code relies on (all [read], audit section 3)
- Wheel step today = fixed 10% of `LargeZoom` per event, magnitude ignored (clamp +-0.05 x2).
- Keys M/Z = 50%, gamepad = continuous; both stay vanilla (keys are discrete jumps by design; gamepad is already smooth).
- `m_mapOffset` = view centre minus player position. Centre `C = player.pos + m_mapOffset`.
- `CenterMap` runs at the end of `UpdateMap` every Large frame and rebuilds the uv rect from `LargeZoom` and `m_mapOffset`. So both must be written **before** vanilla's tail, i.e. in a Prefix. A Postfix would lag one frame.

### 1.2 New code

**`ZoomMath.cs` (pure, Unity-free):**
```
float StepLog(float current, float target, float dt, float tau)   // log-space exponential
bool  Done(float current, float target)                           // |ln(t/c)| < 0.0015
(float x, float z) Anchor(Cx, Cz, nx, ny, aspect, Zold, Znew, span, out ...)
```
- **Smoothing model (documented in code and README):** exponential approach in log-zoom. `z <- z * (target/z)^(1-exp(-dt/tau))`. Continuous velocity, exact for any `dt` (analytic, not Euler), interruptible (just move `target`), reversal needs no special case. A finite stop: when `|ln(target/z)| < 0.0015` (0.15%) snap to target and mark done, so there is no infinite tail and `UpdatedMap` stops firing. `tau = MapZoomDurationMs / 3.9`, so "duration" means "98% of the way" (160 ms -> tau 41 ms). Uses `Time.unscaledDeltaTime` (the map is open with the game possibly paused-ish; scaled time can be 0). `dt` clamped to 50 ms so a hitch does not teleport. **Decision D2 asks the owner to confirm the 98% definition.**
- **Anchor solve (exact):** with `span = m_textureSize*m_pixelSize` (read live, the assembly initialisers differ at runtime), aspect = `rect.w/rect.h` of `m_mapImageLarge`, cursor normalised `n` from the same `ScreenPointToLocalPointInRectangle(rect, pointer, null)` + `Rect.PointToNormalized` vanilla uses:
  `W = C + (n-0.5) * (aspect*Zc, Zc) * span` (world point under cursor, captured **once** at the notch)
  `C' = W - (n-0.5) * (aspect*Zn, Zn) * span`, then `m_mapOffset = C' - player.pos` (y untouched).
  The anchor world point `W` and `n` are held for the whole animation (not re-read each frame), which is what stops drift. They are re-captured only on a new wheel event or after a drag/external change.
  Why not `ScreenToWorldPoint` after changing zoom: it reads the *current* uv rect, which is stale until `CenterMap` runs (it uses the old zoom). The closed form avoids that.

**`SmoothZoom.cs` (Unity-facing):**
- State: `_target`, `_written` (the last `LargeZoom` this class set), `_anchorW`, `_anchorN`, `_anchored`.
- `[HarmonyPatch(typeof(Minimap), "UpdateMap")] Prefix(Minimap __instance, Player player, float dt, bool takeInput)`:
  1. If disabled, not Large, `!takeInput`, `__instance.InTextInput()`, or `ZInput.IsGamepadActive()`: `_target = LargeZoom`, drop anchor, return.
  2. **External change guard:** if `|ln(LargeZoom/_written)| > eps`, something else moved zoom (M/Z keys last frame, pinch, another mod, the cluster click): `_target = LargeZoom`, drop anchor.
  3. `w = ZInput.GetMouseScrollWheel()` (goes through the picker patch, so it is already 0 over panels). If `w != 0`: `notches = clamp(w / NotchUnit, -4, 4)`; `factor = 1 + percent/100` where `percent` is 6 or, when the precision key is held, 2; `_target = clamp(_target * factor^(-notches), min, max)` (wheel up = zoom in = divide). Capture the anchor if cursor-centred and the mouse is active and the pointer is over the map image.
  4. If `_current != _target`: step, write `LargeZoom` (setter clamps to the game's limits, so overshoot is impossible), write `m_mapOffset` from the anchor solve (skipped when `m_dragView` is true, so a drag in progress wins, anchor dropped).
  5. Set `Suppress = true`.
- `Finalizer` clears `Suppress` (a Finalizer, not a Postfix, so an exception in vanilla cannot leave the wheel dead).
- Existing `Patch_ZInput_GetMouseScrollWheel` gets one line: `if (SmoothZoom.Suppress) { __result = 0f; return false; }`, evaluated **after** our own read in step 3 (the flag is set after the read).
- `Minimap.SetMapMode` Postfix: reset state (map reopen: vanilla zeroes `m_mapOffset`, so a stale anchor must go; keep `_target = LargeZoom`).
- `NotchUnit` is **[to measure]**. First Stage B action: a one-shot log line of `ZInput.GetMouseScrollWheel()` for one notch of a wheel and one trackpad swipe, then the constant is set with a comment quoting the measurement. Until measured, nothing is shipped (S2). If the wheel is fractional (hi-res wheels/trackpads), `notches` scales; if it is exactly one unit per notch, the clamp never bites.

### 1.3 Hotkey and controller behaviour
- Precision key: `PrecisionZoomKey` as `KeyboardShortcut(KeyCode.LeftShift)`, matching the repo's hotkey convention (`Plugin.cs:25`). Right Shift accepted too, as `Clusters.cs:289` already does for Shift-click. Shift + wheel is not used by vanilla map input (checked in `UpdateMap`), and Shift + *click* is a different input.
- Controller: unchanged vanilla path; cursor anchor and smoothing never run when `IsGamepadActive()`. The "centre-anchored fallback" in the brief is simply vanilla.
- Touch: unchanged (`ZInput.IsTouchActive()` branch in vanilla runs its own `TouchZoom`; our Prefix sees the external change and resyncs).
- Setting changed mid-animation: next frame reads the new config; turning smoothing off sets `_target = LargeZoom` (animation stops where it is).

### 1.4 Consequences for the rest of the mod
- `UpdatePins` now runs ~10-20x per notch (C7). Measure `Crowding.Measure + Clusters.Measure + LabelCrowding.Apply` cost per pass **before** shipping; Phase 4 owns the fix if needed. Fall-back lever available early: throttle label/cluster passes to every 2nd frame while `SmoothZoom.Animating`.
- `LabelCrowding.ZoomDirection` sees continuous small deltas; its 0.3 s hold keeps direction non-zero through the animation. Re-run `mapsweep` to check flicker did not increase.
- Cluster click (`Clusters.cs:293`) writes `LargeZoom` directly; the external-change guard resyncs. Phase 2 replaces that code with `SmoothZoom.FocusOn(worldPos, zoom)`, which animates both zoom and offset along the same exponential.

### 1.5 Config (Phase 1)
`SmoothMapZoom` true, `MapZoomStepPercent` 6 (range 1-25), `MapZoomDurationMs` 160 (range 0-600; 0 = instant, still uses the step size), `PrecisionZoomPercent` 2 (1-10), `PrecisionZoomKey` LeftShift, `CursorCenteredZoom` true. No name conflicts in `Plugin.cs` (grepped: smooth, cursor, precision, step). Display page: add `General/SmoothMapZoom` etc. to `Sections.cs` `Display` map under "02. Map Display".

### 1.6 Acceptance
- Visual: one notch = 6% over ~160 ms; Shift notch = 2%.
- Anchor: the world point under the cursor moves < 1 screen pixel at rest over 20 consecutive notches in and 20 out (measured via a pilot command that logs `ScreenToWorldPoint` of the pointer before/after, using the game's own transform as the oracle).
- Never beyond `m_minZoom/m_maxZoom` (assert in pilot log).
- No duplicate: with smoothing on, one notch changes the target exactly once (log `target` per event).
- Frame time: `fps` avg and worst-5% unchanged within noise at the 1k and 5k pin scenes (memory: report fps, not ms).

## 2. Phase 2 - clustering

Ladder rung: mixed rung 2/5 (reuse the existing `Clusters`, one-line checksum) and 7 for the inspector (nothing to reuse but the style).

### 2.1 Cache correctness (C1, C2)
Replace the string at `Clusters.cs:84` with an allocation-free `long`/`int` checksum accumulated while `Rebuild`'s eligibility loop would run:
`h = h*31 + identityHash(pin)`, `h = h*31 + RoundToInt(pos.x)`, `.z`, `(int)m_type`, `m_checked`, `m_ownerID`, `m_worldSize>0`, `name.GetHashCode()`, plus the existing inputs (zoom level, filter text hash, hide cycle, three configs, `IncludeAll`, and the new mixed-mode / exclusion setting hash). Eligibility check is O(1) per pin, no dictionaries. If the checksum is unchanged -> reuse; changed -> rebuild. Cost: one O(n) loop per `UpdatePins` (it already runs ~4). **[to measure]** at 20k pins. `PinRecord` changes that flip `IsIndexedOurs` are caught because the record index is rebuilt before the loop (`IndexByPosition` once, centralised, see 4.2).
Also: world/character change: clear on `Game.Shutdown` Prefix (already patched, `Patches.cs:705`) and on `Minimap.SetMapMode`, cheap and removes S3.
Not done: `PinRevision.cs`. Revision counters only see mutations that go through hooked methods; the checksum sees all.

### 2.2 Keep (do not touch)
Grid radius snapping, `HysteresisSteps`, `MinimumPins = 3`, the `Death/Ping/Bed` and `m_ownerID/m_worldSize/m_checked` exclusions, search exclusion, world-space grouping, `NotDrawn` plumbing, badge tiers. Trade-off recorded in `V22_DECISIONS.md` (world-space grouping holds still under pan; screen-space is used only for the inspector placement, spiderfy spreading and label collision, never for membership).

### 2.3 Leader stability
In `Rebuild`, keep the previous leader if it is still a member of the new group (previous `Groups` map kept for one rebuild). Prevents the leader jumping when a pin is added to the west of the group. Count-badge transitions: `PinFade` already fades the member icons; add a 0.15 s scale pop on the badge when its count changes (optional, only if the owner asks - D8).

### 2.4 Mixed-category clustering (`MixedCategoryClusters`, default false)
- Today the bucket key is `pin.m_type` (icon). Mixed mode changes the bucket key to a **compatibility class**, taken from `PriorityZoom.TierOf` (already the mod's own, reviewed grouping: tier 1 ore/chest/runestone/lorestone/leviathan/wisp; tier 2 landmark/prop/spawner/beehive; tier 3 pickable). No icon IDs are hard-coded; the category comes from the pin's record key via `PinRecord.KeyOfIndexed` (the existing mechanism `PriorityZoom.Hides` uses).
- Tier 0 (bosses, miniboss, dungeons, camps, traders, home, portals) is **never** in a mixed cluster, and hand-placed / shared pins already never cluster (`IsIndexedOurs`). That satisfies "protect portals, user-placed markers".
- Exclusion list: `MixedClusterExclude` (comma-separated `PinCategory` names, default empty; tier 0 is protected regardless). Deferred unless the owner wants it (D5) - ponytail says no config until asked.
- Cluster stores the category histogram; badge ring is tinted differently when mixed (visual **owner picks from options**, memory: assets-owner-chooses).
- With the setting off the bucket key is `pin.m_type` exactly as today, so behaviour is byte-identical.

### 2.5 Cluster inspector (`ClusterInspector.cs`)
- `ClusterClickAction` enum `{ Zoom, Inspector, Spiderfy }`. **Spec default is `Inspector`.** This changes behaviour for people who already enabled `ClusterPins` (today = zoom). Decision D3.
- Open: left click on a badge (the existing First-priority prefix on `OnMapLeftClick`, `Clusters.cs:279`, becomes the dispatcher; it still returns `false` for a cluster so the click neither ticks nor deletes). It keeps the Shift bypass.
- Panel: uGUI, same dark `(0.04,0.035,0.03,0.94)` background and bronze rail as `ClusterPopup`; header "N pins"; a search field; a scroll list. **Virtualised with ~14 pooled rows** (fixed row height), so a 500-pin cluster costs the same as a 14-pin one. Row = icon (the pin's own sprite), name (localised via `Labels.Localize`), category (record key prefix), distance from player (`Utils.DistanceXZ`). Mixed clusters: a one-line category breakdown under the header.
- Select row: `SmoothZoom.FocusOn(pin.m_pos, zoomWhereItSeparates)`. `zoomWhereItSeparates` is computed, not guessed: the zoom at which `Crowding.IconMetres*1.1 < distance to the nearest group member`, clamped to the min zoom. The pin is not ticked, deleted or renamed. Hover popup (lightweight preview) is hidden while the inspector is open.
- Close: Escape, click outside, map close (`SetMapMode` Postfix), a different cluster click. How Escape reaches the map is **to read first** in Stage B (the repo's `SetupMenu.cs:1135` already consumes Escape with `ZInput.GetKeyDown`; copy the pattern and check it does not also close the map).
- Don't rebuild text per frame: rows rewritten only when the filtered list or the group checksum changes; distances refreshed at 2 Hz while open.
- Controller: list rows are `Selectable`s with explicit navigation; B/Cancel closes. **NOT VERIFIABLE on this machine without a pad**; marked as manual test.
- Raycasts: the panel blocks raycasts only over its own rect; `PinHover.Find` already skips when `PointerOverPanel` style checks say so - add the inspector to `PinEditor.PointerOverPanel()`'s sibling check so the wheel is also withheld (extends `Patch_ZInput_GetMouseScrollWheel`'s `||` chain by one term).

### 2.6 Spiderfy (`ClusterSpiderfy.cs`, `EnableClusterSpiderfy` false)
- Only when `EnableClusterSpiderfy` and `ClusterClickAction == Spiderfy` and member count <= `SpiderfyMaxPins` (12). Otherwise the click opens the inspector: **that is the whole switch rule** between list and spiderfy (clear to document).
- Members are drawn as temporary `Image` objects (pooled, 12 max) on a circle of radius `max(1.4*iconPx, n*iconPx/(2*pi))` around the badge; thin stretched `Image` quads (rotated rect) as legs back to each member's real screen position. No LineRenderer, no new shader. Real `PinData` untouched; the temporary icon only forwards a click to `FocusOn`.
- Collapse: Escape, map close, `LargeZoom` change beyond 0.5%, outside click, drag start. Mouse-only at first; controller path is the inspector.
- Interaction guard: the spiderfy icons are `raycastTarget` only while expanded and are skipped by `Patch_Minimap_GetClosestPin`/delete (they are not in `m_pins`, so right-click delete cannot see them).

## 3. Phase 3 - label placement

Ladder: rung 7 inside `LabelCrowding.Apply`; no new class beyond the pure geometry file.

- **Priority (3.3):** `Ranked` gains a `Class` int ahead of rarity: 0 hovered (`pointedAt`, already first-class), 1 inspector-selected, 2 search match, 3 tier-0 category (`PriorityZoom.TierOf`), 4 everything else; then rarity, then position. A portal can no longer lose its label to forty chest labels at rarity-equal ties. Uses `PinRecord.KeyOfIndexed`, valid because `IndexByPosition` is called once at the top of `Crowding.Measure` (4.2).
- **Candidates (3.2):** at the `Hide(name)` decision (`LabelCrowding.cs:286`), instead of hiding, try Above, Right, Left (Below is the default and was just rejected) with `LabelGeometry.Candidate(rect, iconRect, side, gap)`. Accept the first that does not collide with `Shown` (extended to also include the **cluster badge rects** of drawn leaders, and the parent canvas rect for screen bounds). Applied by offsetting the **text child** (`PinNameText.rectTransform.anchoredPosition`), not the label root, because `RectOf` already reads the child's local position (`LabelCrowding.cs:583`). **S1 must be checked first**: write an offset, read it back after two passes; if the game re-lays it out, the fallback is a `Canvas`-level offset on a wrapper object (worse) or dropping the feature.
- **Stability (3.4):** per-label `Placement { Side, Since }` in a `ConditionalWeakTable` (same idiom as `Fades`). A label stays on its side until it collides there by more than `Margin` (the existing 12%), and returns to Below only when Below is free with the margin. Side changes animate the offset over 0.15 s inside `LabelCrowding.Step`. No new text-geometry calls: widths come from TMP's cached `preferredWidth`.
- **Orphans (3.4):** unchanged, `HideOutOfSight` + `Fade` already cover; offset is reset when a label hides.
- **Search (3.5):** matches rank class 2 and are tried against candidates before non-matches hide; non-matches still hide (existing rule). Only pins that exist on the map participate, so no fog information is added.
- Config `SmartLabelPlacement` (spec default true). This changes visuals for every user on update: Decision D4.

## 4. Phase 4 - performance

Ladder: measure first (rung 0 "does it need to exist": only fix what the numbers show).

### 4.1 Benchmarks
- `carturpins_bench <count> <distribution> [secs]` in `Probe.cs` (pilot code; `DIAGNOSTICS` is defined for the dev build, `CarturMapPins.csproj:22`; confirm release builds strip it in Stage B step 0). Generates unsaved pins via `Minimap.AddPin(save:false)` (the existing `Clusters.IncludeAll` pilot flag exists for exactly this), distributions: uniform, 20 tight clumps, dense-mixed (all tiers), 90% identical names, with search text set. Runs a scripted zoom sweep + pan sweep, logs avg/p95 frame time **and fps (avg and worst 5%)**, per-pass ms for Measure/Clusters/Labels (Stopwatch), GC.CollectionCount deltas, visible marker count. Counts 100 / 1k / 5k / 10k / 20k.
- Results are reported as **[measured, Valheim x.y, machine]**. Algorithm-only microbenchmarks (the pure files) are labelled **[simulated]**. Nothing is estimated.

### 4.2 Reduce work (only what the numbers justify)
- Certain, tiny: call `PinRecord.IndexByPosition()` once per pass at the top of `Crowding.Measure`; delete the other four calls (C3).
- Certain, tiny: checksum replaces string signature (C2).
- Conditional on measurements: throttle label pass during animation, cache `PinHover.Find` result when the pointer is still, skip off-screen pins in `PinFade.Step`/`LabelCrowding` by comparing `localPosition` to the parent rect (a viewport cull on *presentation only*; `NotDrawn`, fog and records untouched).
- Spatial index: `Clusters` already uses a uniform grid (`cells` dictionary, 3x3 neighbourhood). Hover uses linear scan over drawn icons. Add a grid to hover/labels **only if** a benchmark shows >1 ms; `MarkerSpatialIndex.cs` is not created by default.
- Pooling: inspector rows and spiderfy icons are pooled by design (2.5/2.6); badges already live on the pin's icon and die with it.

### 4.3 Compatibility regression list
In `V22_TEST_PLAN.md`. No save format change is planned; an audit finding that requires one would be raised separately (spec).

## 5. Phase 5 - API foundation

Ladder: rung 1/2 (skip most of it). Do the read-only snapshot and one event, nothing else, until a real consumer exists.

- `MapPinsApi.cs`: `public static class MapPinsApi { const int Version = 1; static IReadOnlyList<MarkerInfo> GetMarkers(); static event Action<int> Changed; }`. `MarkerInfo` is a `readonly struct`: `string Id; float X, Z; string Name; string Category; int IconIndex; bool Visible; MarkerSource Source; bool Checked`. Details in `WORLDATLAS_INTEGRATION.md`.
- `Changed` fires from the end of the `UpdatePins` Postfix when the content checksum (the same one Clusters uses, widened to all pins) differs, so no polling and no extra patches. Documented limitation: pins that change without an `UpdatePins` pass are seen at the next pass.
- Not a build-time dependency in either direction. WorldAtlas references our DLL (or reflects); Map Pins references nothing of theirs.

## 6. Sequence (Stage B, after approval)

0. Branch `v2.2`. Baseline: pilot `mapsweep` + fps on 1k/5k scenes saved to `docs/v22-baseline.md`. Measure `NotchUnit`. Read Escape handling. Confirm `DIAGNOSTICS` strip.
1. Phase 1 (`ZoomMath`, `SmoothZoom`, config, scroll patch term). Build, `releasecheck.ps1`, pilot zoom checks. One commit.
2. Phase 2a: checksum + leader stability + `IndexByPosition` dedupe (small, safe). Commit.
3. Phase 2b: inspector, then 2c spiderfy. Commits.
4. Phase 3: priority classes (safe), then candidate placement behind S1. Commits.
5. Phase 4: bench command, run, apply only justified optimisations.
6. Phase 5: API.
7. Regression run, README/changelog, pack.ps1 RC **not published**, PR opened for review (no merge). Version stays 2.1.3 in code until a tested build exists (CLAUDE.md "never bump for an untested build").

Dev profile turn-taking: every launch uses the shared pilot with the lock and quits afterwards (memory: close-game-take-turns). No deploy while `valheim.exe` runs.

## 7. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Another mod patches `UpdateMap`/`LargeZoom` | medium | double zoom or fight | External-change guard; disabling setting; log which patches are on `UpdateMap` at load (`Harmony.GetPatchInfo`) as a warning |
| `UpdateMap` signature changes in a game update | low | patch silently missing | Patch by name; `LogWarning` when not applied; add its IL hash to `tools/patched-bodies.json` |
| Label offset overwritten by game layout (S1) | medium | Phase 3 candidate placement unusable | Test first; keep priority classes (independent) |
| Smooth zoom multiplies `UpdatePins` cost (C7) | medium on 10k+ maps | frame drops | Measure; throttle lever ready |
| Inspector input focus / Escape closes map | medium | annoying UX | Read Escape path first; follow `SetupMenu` |
| Default-on `SmartLabelPlacement`, `ClusterClickAction=Inspector` change existing behaviour | certain | complaints | Decisions D3/D4; changelog line |
| No pad to test controller | certain | NOT VERIFIED gate | Mark controller gates NOT VERIFIED, ask owner to test |

## 8. Rollback
Each phase is its own commit(s) on `v2.2`; revert is per phase. Runtime: `SmoothMapZoom=false`, `ClusterClickAction=Zoom`, `MixedCategoryClusters=false`, `EnableClusterSpiderfy=false`, `SmartLabelPlacement=false` each restore the 2.1.3 path. No save data touched, so rolling back the DLL needs no migration.

## 9. Plan changes justified by source (vs the five-phase brief)

1. Cursor-centred zoom: vanilla ships it disabled; ours is new, exact, but there is **no pan boundary to clamp to** (A4).
2. Wheel step: vanilla is already 10% per notch (and x0.9 in / x1.1 out, non-inverse). The 6% default is *slower* than vanilla (~72 notches across the range vs ~44, at limits 0.015..1). Kept as specified; flagged (D1).
3. Phase 2.1 "revision-based" invalidation -> checksum (A6).
4. `MarkerSpatialIndex.cs`, `PinRevision.cs`, `ZoomInput.cs`, `ZoomAnchor.cs` not created: the grid exists, the revision is a checksum, input and anchor live in `SmoothZoom.cs`/`ZoomMath.cs`.
5. Clustering already has hover preview, hysteresis, badge, search exclusion; Phase 2 is inspector, mixed mode, leader stability, cache fix.
6. Added: tests project limited to the pure math (A5), an `NotchUnit` measurement step, and `UpdateMap` IL hash.
7. `ClusterPins` default stays **false**. To use the new features players turn `ClusterPins` on (config; whether the setup menu lists it was not checked); the new settings only have effect while it is on, except zoom and labels which do not depend on it.

## 10. Features already present, not to be duplicated
Cluster badge, hover list, search exclusion, zoom hysteresis, label fade, rarity ranking, collision hysteresis, hover reveal, merge repeats, priority zoom LOD, cursor-safe wheel withholding over panels, `GetClosestPin` fix, per-world record files.
