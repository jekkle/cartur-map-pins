# Cartur's Map Pins v2.2 - code audit (Stage A, read-only)

Labels: **[read]** read from this repo's source or from the decompiled game; **[measured]** measured at runtime (none made in this audit); **[belief]** inferred, not verified.
No production file was touched. Nothing was built or run in this stage.

## 1. Repository state

| Item | Value |
|---|---|
| Path | `D:\Ai\Modding\Valheim\cartur-map-pins` |
| Branch / HEAD | `master`, `8203543dd23d160cb7b24452d8f1a012e42b2185` ("2.1.3: totya's second report") |
| Latest version | 2.1.3 (`src/Plugin.cs:19`, `package/manifest.json`) |
| Remote tracking | `master...origin/master`, no divergence shown |
| Uncommitted | Untracked only: `docs/default-colours-proposed.png`, `media/picker/`, `media/setup/`, `media/zoom/`, `research/`. No modified tracked files. |
| Target | `net472`, BepInEx.Core 5.4.21, HarmonyX 2.10.1, BepInEx.Analyzers 1.0.8 (`src/CarturMapPins.csproj`) |
| Game refs | `assembly_valheim`, `assembly_utils`, `assembly_guiutils`, UnityEngine.*, Unity.TextMeshPro, from `$(VALHEIM_INSTALL)` |
| Game build used for decompile | Steam build id `26249040`, `assembly_valheim.dll` dated 2026-10-28, ILSpy 8.2 output |
| Build prerequisites | .NET SDK, Steam Valheim install, r2modman `Dev` profile (`R2MODMAN_PROFILE`). **Not run in this stage.** |
| Tests in repo | **None.** No test project, no CI workflow. In-game checks go through the sibling `cartur-testpilot` (`mapsweep`, `fps`, `near`). |
| Environment note | `valheim.exe` was running during the audit (PID 59440). Stage B must not deploy while it runs (memory: no-deploy-during-game). |

Source is 17,491 lines in 60+ files. The task prompt's named files all exist. `src/Plugin.cs` (1121), `Patches.cs` (714), `Clusters.cs` (299), `ClusterPopup.cs` (113), `Crowding.cs` (451), `LabelCrowding.cs` (621), `PinFade.cs` (216), `PinHover.cs` (134), `PriorityZoom.cs` (65), `PinFilter.cs` (30), `PinHiding.cs` (254), `Reflection.cs` (116) were read in full or in the relevant ranges. `PinRecord.cs`, `PinEditor.cs`, `PinStyles.cs`, `MapIconPicker.cs` were read only where they touch zoom, clustering or labels.

## 2. Existing feature inventory (what v2.2 must not duplicate)

- **Clustering exists.** `ClusterPins` (default **false**, `Plugin.cs:211`). Same-icon, mod-placed pins within `IconMetres*1.1` world metres, min 3 pins, power-of-two radius with hysteresis 0.17 steps, count badge (2-9 / 10-99 / 100+), members fade via `Crowding.NotDrawn`. `Clusters.cs`.
- **Cluster hover popup exists.** Read-only, names grouped, top 8 rows with distance. `ClusterPopup.cs`, called from `PinHover.SetTag` (`PinHover.cs:103-107`).
- **Cluster click = zoom in.** Prefix on `Minimap.OnMapLeftClick`, Priority.First: `LargeZoom *= 0.5`, `m_mapOffset = pin.pos - player.pos` via reflection (`Clusters.cs:279-298`). Shift bypasses it.
- **Search exclusion exists.** A pin the search matches leaves its group (`Clusters.cs:114`). Non-matching names are hidden (`LabelCrowding.cs:154`).
- **Merge repeats** (same name+type per grid cell, "Tin x15") and **shrink crowded pins** (`Crowding.cs`), **priority zoom-out LOD** (`PriorityZoom.cs`, default off), **hide by range**, **hide list**, **pin fade** (`PinFade.cs`).
- **Label system exists.** Rarity ranking, TMP preferred-size rectangles, overlap hiding with 12% hysteresis margin, direction-aware (zoom in/out) hold, 0.2 s smoothstep fades, hover reveal, grouped-label suppression, search dimming. `LabelCrowding.cs`.
- **Scroll-wheel patch exists.** `Patch_ZInput_GetMouseScrollWheel` zeroes the wheel while the pointer is over the icon picker or pin editor (`MapIconPicker.cs:851-863`). Any zoom work has to go through or around this.
- **No map zoom smoothing exists in the mod.** The `media/zoom` videos and "zoom" in the changelog are about pin *size* during zoom (`Crowding.ZoomScale`, `PinFade`), not about the map's own zoom. [read]

## 3. Actual zoom call path (vanilla, decompiled) [read]

All line numbers below are in the ILSpy output of `Minimap` (scratchpad `dec/Minimap.cs`), build 26249040.

1. `Minimap.Update()` (L640) calls `UpdateMap(localPlayer, dt, takeInput)` at L754, then `UpdatePins()` if `m_pinUpdateRequired`.
2. `UpdateMap` (private, L932). Large map, mouse:
   - L939-943: `num = ZInput.GetMouseScrollWheel()` (only when `!IsGamepadActive()`), then `num = Clamp(num, -0.05, 0.05) * LargeZoom * 2`. **Any wheel event, of any magnitude, becomes a fixed step of 10% of the current zoom.** The input action scales raw scroll by 0.15 (`ZInput.SetUpAnalogHandlers`), `GetScrollModifier()` returns 1.
   - L953-960: gamepad `JoyMapZoomIn/Out` adds `LargeZoom * dt * 2` per frame (continuous, already smooth).
   - L1021-1028: keys `MapZoomOut/In` add or subtract 50% of `LargeZoom` per press.
   - L1044: `LargeZoom -= num` (additive on the *zoom value*, so one notch in is x0.9 and one notch out is x1.1; they are not inverses).
3. `LargeZoom` setter (L433-443): stores `m_lastLargeZoom = m_largeZoom`, then clamps to `[m_minZoom, m_maxZoom]`. The component's runtime limits are 0.015 and 1 (mod comment `Crowding.cs:107`, "read from the live component"); the assembly initialisers say 0.01 / 1.
4. **Cursor-anchored zoom exists in vanilla but is dead code.** L1031: `if (false && !ZInput.IsGamepadActive()) { ... Vector2.LerpUnclamped(pointer, screenCentre, LargeZoom/m_lastLargeZoom) ... CenterMap(); m_mapOffset = ... }`. The `else` branch (L1044) is what runs. So in the shipping game zoom is **always about the view centre**. [read] Why Iron Gate disabled it is not in the code. [belief] the lerp formula is only an approximation.
5. Pan: `m_mapOffset` (private `Vector3`, L349) is "view centre minus player position". Drag (L1155-1162) adds the world-space delta between pointer positions. Gamepad stick adds to it (L1152). `CenterMap(player.pos + m_mapOffset)` is called at L1165 every frame in Large mode.
6. `CenterMap` (L1251) sets `m_mapImageLarge.uvRect`: `height = LargeZoom`, `width = LargeZoom * rect.w/rect.h`, `center = WorldToMapPoint(centre)`. It also writes shader `_zoom`, `_pixelSize`, `_mapCenter`, and `UpdatedMap()` sets `m_pinUpdateRequired` when the centre or `LargeZoom` changed (L1292-1305).
7. Transform: `WorldToMapPoint` (L1792): `uv = (p / m_pixelSize + m_textureSize/2) / m_textureSize`. `ScreenToWorldPoint` (L2370): `ScreenPointToLocalPointInRectangle(m_mapImageLarge rect, screenPoint, null camera)`, normalise, `uv = uvRect.min + n * uvRect.size`, then `MapPointToWorld`. The whole thing is **linear**: `world = centre + (n - 0.5) * (aspect*Z, Z) * (m_textureSize * m_pixelSize)`. That gives an exact closed form for cursor anchoring (see plan, section 1).
8. **No pan bounds exist.** `m_mapOffset` and the uv centre are never clamped in `UpdateMap` or `CenterMap`. [read] The prompt's "clamp to the map's valid pan bounds" has nothing to clamp to; adding a clamp would be a new behaviour, not a preservation.
9. `SetMapMode(Large)` (L1209) sets `m_mapOffset = Vector3.zero` and `m_dragView = false` (L1244). `LargeZoom` persists across opens. A pin-click recentre writes `m_mapOffset` at L2275 (ping/recenter helper).
10. Touch pinch zoom (L1073-1141) has its own anchored path (`TouchZoom`). Leave alone.
11. Small map: `SmallZoom` +-50% by key only (L1048-1070). Out of scope.

### Harmony patches already touching this path

| Patch | File | Effect on zoom |
|---|---|---|
| `ZInput.GetMouseScrollWheel` Prefix | `MapIconPicker.cs:851` | Returns 0 over picker/editor. Every caller goes through the public static forwarder (comment says Minimap.UpdateMap, GameCamera x2, Player.UpdatePlacement). |
| `Minimap.OnMapLeftClick` Prefix x2 | `Clusters.cs:279` (First), `MapIconPicker.cs:823` | Cluster zoom-in and pin edit. Both return `false` to skip vanilla. |
| `Minimap.SetMapMode` Postfix x2 | `MapIconPicker.cs:351`, `PinEditor.cs:586` | Panels show/hide. |
| `Minimap.Update` Postfix | `PinFade.cs:160` | PinHover.Find, PinFade.Step, LabelCrowding.Step, hints. |
| `Minimap.UpdatePins` Postfix | `Patches.cs:374` | Crowding.Measure, Clusters.Measure, LabelCrowding.Apply. |
| `Minimap.GetClosestPin` Postfix | `PinFade.cs:183` | Pick nearest *drawn* pin. |
| `Minimap.InTextInput` Postfix | `TextFocus.cs:84` | Text focus. |

`Minimap.UpdateMap` is **not patched by anyone in this mod**. Other installed map mods are not surveyed (not in this repo); WorldAtlas does not exist yet. [belief] Mods that patch `UpdateMap` or write `LargeZoom` exist; the plan detects external changes instead of assuming exclusivity.

## 4. Actual cluster update path [read]

`Minimap.UpdatePins` (vanilla; runs only when `m_pinUpdateRequired`, i.e. centre moved >0.01, zoom changed, a pin was added/removed, or a mod requested it) -> Postfix `Patches.cs:374`:

1. `Crowding.Measure(pins)` (`Crowding.cs:167`): clears 6 collections, `PinHiding.Apply`, builds the `NotDrawn` set (hide list, looted chests, spawn duplicates, priority zoom, range), reads the smallest live icon width, picks power-of-two cell, then 2-3 passes over all pins for merge survivors, crowd counts, winners.
2. `Clusters.Measure(pins)` (`Clusters.cs:60`): computes a signature **string**; rebuilds only if it changed; then `MarkNotDrawn(member)` for every member.
3. `LabelTemplates.Apply`, `LabelCrowding.Apply(pins)`.
4. Per-pin colour/style loop.

Every frame (`Minimap.Update` postfix): `PinHover.Find` (O(n) over pins with an enabled icon), `PinFade.Step` (O(n), eases alpha/scale, calls `ClusterBadge.Sync` per pin), `LabelCrowding.Step`.

## 5. Existing label algorithm [read]

- **Rank:** rarity = number of pins sharing a name among labels considered this pass; ties by world position (`LabelCrowding.cs:44-51`). Rarest placed first.
- **Rectangle:** `RectOf(name)` (L577): the text child's `preferredWidth` (capped to the 150-wide rect) by `preferredHeight`, positioned from the label root's `anchoredPosition` + child local offset. The text hangs *below* the pin (top-centre pivot, 16 below). It is TMP's cached preferred size, so no layout cost. Font scale = prefab min/max auto-size x `LabelScale` (`ScaleLabels`, L336).
- **Collision:** `Collides(test)` tests the test rectangle against the list `Shown` (O(shown) per label, so O(n*shown) per pass).
- **Hysteresis:** test rect grown/shrunk by `Margin = 0.12` of its size (shrunk if already shown, grown if hidden); zoom-direction hold (`ZoomDirection`, 0.3 s `HoldSeconds`); at rest everything is decided again in rarity order.
- **Fade:** `Fades` ConditionalWeakTable, `CanvasGroup`, 0.2 s smoothstep, stepping at most `PinFade.MaxStep` (30 fps equivalent).
- **Hover reveal:** the single pin nearest the pointer within `RevealRadius = 60 px` is revealed and wins over any overlapping label (L211-254).
- **Grouped suppression:** `HideGroupedLabels` hides names of cluster/merged pins from half the `HideLabelsFromZoom` point out (L111-150).
- **Search:** non-matching names hidden (L154); matching pins leave clusters.
- **Non-Cartur pins:** `ShowPinLabels=false` hides only mod-placed pins' names (`oursOnly`); vanilla/hand pins still go through collision.
- **Zoom-out rule:** vanilla's `m_showNamesZoom` rule is dead (value 2.0 on the live component), so the mod owns it with `HideLabelsFromZoom` and a 10% band (L82-94).

## 6. Existing performance measures [read]

- Reused collections in `Crowding` and `LabelCrowding` (no per-pass dictionaries there).
- `PinRecord.ByPosition` index, rebuilt per pass rather than kept in sync (comment `PinRecord.cs:1521`).
- Cluster rebuild skipped when the signature is unchanged.
- Fade writes guarded by `Approximately` to avoid canvas dirtying.
- Grid snapped to powers of two to stop re-cutting on every zoom frame (measured earlier: 590 of 715 pins flickered; zero when snapped, per comment `Crowding.cs:351`).
- `mapsweep` pilot command exists to count flicker, alpha steps and scale steps.

## 7. Findings

### Confirmed by reading the code

**C1. Cluster cache signature misses most relevant mutations.** `Clusters.cs:84` hashes `pins.Count`, zoom level, search text, hide cycle, three config values, `IncludeAll`. It does **not** include any pin's position, type, name, checked flag, owner, or identity. Same-count changes therefore reuse stale `Leaders`/`Members`/`Groups`. Cases the signature cannot see: re-icon via the editor (type change, the grouping key), rename, tick/untick, a delete followed by an add in the same frame, `IncludeAll` toggled by the pilot only, `HideGroupedLabels`/`ShrinkCrowdedPins`-style settings (not read by Rebuild, so fine), `PinRecord` changes that flip `IsIndexedOurs` (ours vs hand pin), and `Plugin.ClusterPins` toggling (handled: `Active` false clears). **Whether a user can hit this in practice is a hypothesis**: most mutations also set `m_pinUpdateRequired`, but the *count* is what guards the rebuild. Needs a runtime test (rename a pin that belongs to a cluster).

**C2. The signature is a heap-allocated interpolated string on every `UpdatePins`** (`Clusters.cs:84`). It runs each frame the map is dragged or zoomed. Small, constant, avoidable garbage. [read; magnitude not measured]

**C3. `PinRecord.IndexByPosition()` is called up to four times per pass**: `Crowding.cs:180, 190, 208`, `Clusters.cs:101` (on rebuild), and again in `LabelCrowding.HideAll` (`:383`). Each is a clear + O(records) refill. Redundant work, no stale-data risk. [read]

**C4. Cluster click is zoom-only and writes private state by reflection.** `Clusters.cs:293-295` halves `LargeZoom` and writes `m_mapOffset`. It does not look at whether the cluster would open at that zoom (it may not: members split only when the power-of-two level changes). `Rebuild` groups by world distance only (`<= radius`, radius = icon width in metres), so pins closer together than one icon at the zoom limit stay grouped at every zoom. [read; whether any real world has such pins is a hypothesis, e.g. a dense ore vein]. For those, clicking can never expand the cluster. This is the strongest justification for the inspector.

**C5. Hover text of a cluster is built only when hover target changes** (`PinHover.cs:79-88`, `SetTag`). It is not rebuilt per frame (good, matches the prompt's requirement) but is also not refreshed if the group changes while the pointer rests on the badge. Stale until the pointer leaves. [read]

**C6. Two `OnMapLeftClick` prefixes both return `false` to skip vanilla.** HarmonyX ANDs prefix results. They are ordered (Cluster First), so only one acts, but a third prefix (the inspector) must be added with explicit priority and `__runOriginal` awareness. [read]

**C7. Smooth zoom raises `UpdatePins` frequency.** Vanilla changes `LargeZoom` once per wheel event, i.e. one `UpdatedMap()` pass per notch. A 160 ms animation is ~10-20 passes per notch at 60-120 fps, each running the 3-4 pin passes above plus label work. Not measured; the cost scales with pin count. [read for mechanism; magnitude is a hypothesis]

### Suspected, needs runtime test

**S1.** Label candidate positions: moving the TMP child's rect may be re-laid by the game on `UpdatePins`/font change. The mod's own comment says the prefab sets `fontSize` on every layout (`LabelCrowding.cs:331-335`); whether position is also rewritten is unknown. Verify by writing an offset and re-reading it two passes later.

**S2.** Wheel notch magnitude: raw scroll x 0.15 with Unity Input System `<Mouse>/scroll/y`. Notch size on Windows is typically +-120 raw (so +-18 after the scale) but this is not read from the game or measured. Needs a one-shot log of `ZInput.GetMouseScrollWheel()` for one notch, a free-spinning wheel and a trackpad. Vanilla's +-0.05 clamp hides it, so no vanilla behaviour depends on it.

**S3.** Cluster state across worlds: `Clusters` statics are cleared whenever `Measure` runs in a non-Large mode, and a new session starts in Small mode, so a leak requires `UpdatePins` not to run between sessions. Probably safe. Not tested.

**S4.** `Minimap.UpdateMap` private signature `(Player, float, bool)` as seen in the current assembly. `tools/patched-bodies.json` stores IL hashes for methods the mod patches; `UpdateMap` is not in it. A new patch needs a hash entry so `releasecheck.ps1` catches game updates. (Check that file's role before relying on it; not read in detail.)

### Not a bug (checked)

- Cluster members are faded, not removed from `m_pins`; saved data is never mutated by clustering. [read]
- `GetClosestPin` Postfix prevents clicks on hidden members. [read]
- Fog of war: pins are created by discovery code; clustering only hides, never reveals. [read]

## 8. Compatibility concerns

- Any mod that patches `Minimap.UpdateMap`, writes `LargeZoom`, or replaces the wheel read can fight a zoom controller. Mitigation: detect an external zoom change and resync the target; leave a setting to disable.
- Other pin mods that add `PinData` with no `PinRecord` entry are never clustered (only `IsIndexedOurs`). Keep that; mixed-category must not widen it.
- Valheim updates: `UpdateMap`, `m_mapOffset`, `m_mapImageLarge`, `m_textureSize`, `m_pixelSize` are all private/serialised members looked up by name. Cache `FieldInfo` once, `?.` every use.
- `Minimap.UpdateMap` changed shape before? Unknown. Copy the vanilla argument list exactly and guard the patch with a try/log (CLAUDE.md: a throwing patch takes the loader down).

## 9. Technical debt

- `Clusters.cs` reflects on `m_mapOffset` locally; `Reflection.cs` (`MinimapAccess`) already centralises such lookups. Move it there when a second user appears (it will: zoom anchor).
- `PinRecord.IsOurs` is an O(records) walk; fine for hover, wrong for per-frame use.
- `LabelCrowding.Collides` is linear in shown labels (O(n^2) worst case).
- No automated tests anywhere; every check is a manual pilot run.
- `Plugin.cs` is 1121 lines of `Config.Bind` plus UI; new settings should go through the existing `Sections.Of` mapping (`Sections.cs`), which also needs a display-section entry.

## 10. Recommended changes (summary)

1. Zoom: new `SmoothZoom` controller, one `UpdateMap` Prefix (+ Finalizer) and one flag in the existing scroll patch. Closed-form cursor anchor. No pan clamp.
2. Clusters: replace string signature with a revision counter plus a cheap allocation-free content checksum fallback; add inspector; keep hover popup.
3. Labels: candidate-position placement inside the existing rank loop, after the "hide on collision" decision.
4. Perf: measure first (testpilot), then dedupe `IndexByPosition`, add a grid only if numbers justify it.
5. API: read-only snapshot class, soft, no dependency.
