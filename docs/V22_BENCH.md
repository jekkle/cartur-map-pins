# v2.2 benchmark (Valheim build 26249040, 1920x1080, pilot test world, 2026-11-03)

**[measured]** in the game with `v22b scene <n> <distribution>` then `v22b run 4` (pilot: `cartur-testpilot/src/Pilot.V22Bench.cs`). Release build with the `BENCH` flag (`dotnet build -c Release -p:DefineConstants=BENCH`), which adds the stage timers in `src/Bench.cs`. Pins are unsaved test pins from a fixed random seed, `ClusterPins` on, every other setting at its default, map open at zoom 0.3.
Each scene is run three ways for 4 s: **rest** (nothing moves), **zoom** (a wheel notch every 8 frames, 3 in then 3 out), **pan** (the view circles at 6 m per frame). "fps worst 5%" is 1 / the 95th-percentile frame time. "pass" is one run of the map's pin update (Crowding + Clusters + labels), in ms; it runs on every frame the map moves. "fade" is the per-frame pin easing step. Distributions: uniform (3,000 m disc, 40 names, 5 icons), clumped (60 Gaussian clumps, sigma 60 m), mixed (dense, 600 m disc), names (90% one name and icon). GC gen0 is the count of gen-0 collections in the 4 s window. No managed-allocation figure is reported: the heap readings jump when the collector runs and are not a usable allocation count.

## Before the label-pass fix (HEAD `d1a04ad` plus the BENCH hooks)

| scene | phase | fps avg | fps worst 5% | GC gen0 | pins | pass ms (labels / crowding / clusters) | fade ms | hover ms |
|---|---|---|---|---|---|---|---|---|
| uniform-100 | rest | 74.3 | 24.3 | 1 | 140 | 0.0 | 0.1 | 0.1 |
| uniform-100 | zoom | 68.4 | 23.2 | 0 | 140 | 0.5 (0.4/0.1/0.0) | 0.1 | 0.0 |
| uniform-100 | pan | 63.1 | 22.1 | 1 | 140 | 0.5 (0.4/0.1/0.0) | 0.1 | 0.0 |
| uniform-1000 | rest | 62.4 | 21.0 | 1 | 1040 | 0.0 | 1.2 | 0.4 |
| uniform-1000 | zoom | 30.3 | 14.9 | 0 | 1040 | 6.1 (5.2/0.8/0.0) | 1.2 | 0.3 |
| uniform-1000 | pan | 28.0 | 15.0 | 1 | 1040 | 6.0 (5.2/0.8/0.0) | 1.0 | 0.3 |
| uniform-5000 | rest | 31.3 | 15.2 | 0 | 5040 | 66.6 (62.4/3.9/0.3) | 7.0 | 1.0 |
| uniform-5000 | zoom | 8.6 | 5.0 | 0 | 5040 | 64.6 (60.7/3.7/0.3) | 7.3 | 1.0 |
| uniform-5000 | pan | 6.5 | 5.9 | 1 | 5040 | 68.1 (63.5/4.3/0.3) | 7.0 | 1.0 |
| uniform-10000 | rest | 19.8 | 15.4 | 0 | 10040 | 172.2 (162.8/8.7/0.7) | 13.7 | 1.8 |
| uniform-10000 | zoom | 4.9 | 3.0 | 0 | 10040 | 164.2 (154.9/8.6/0.7) | 13.3 | 1.7 |
| uniform-10000 | pan | 3.2 | 2.8 | 0 | 10040 | 169.6 (159.8/9.1/0.7) | 13.8 | 1.8 |
| uniform-20000 | zoom | 2.7 | 1.4 | 1 | 20040 | 378.8 (360.3/16.7/1.7) | 23.6 | 2.7 |
| uniform-20000 | pan | 1.1 | 0.4 | 0 | 20040 | 381.9 (363.3/16.9/1.7) | 23.4 | 2.8 |
| clumped-100 | rest | 85.8 | 62.1 | 0 | 140 | 0.6 (0.5/0.1/0.0) | 0.1 | 0.1 |
| clumped-1000 | zoom | 32.1 | 24.2 | 0 | 1040 | 6.9 (5.9/1.0/0.1) | 1.8 | 0.3 |
| clumped-5000 | rest | 47.9 | 39.5 | 0 | 5040 | 41.6 (37.3/4.0/0.3) | 5.6 | 0.7 |
| clumped-5000 | zoom | 11.3 | 6.1 | 0 | 5040 | 39.9 (36.0/3.6/0.3) | 6.0 | 0.7 |
| clumped-5000 | pan | 8.8 | 8.2 | 0 | 5040 | 41.7 (37.2/4.2/0.3) | 5.5 | 0.7 |
| clumped-10000 | rest | 29.2 | 24.0 | 0 | 10040 | 91.0 (82.6/7.7/0.7) | 10.2 | 1.2 |
| clumped-10000 | zoom | 6.9 | 4.1 | 0 | 10040 | 87.7 (79.7/7.4/0.6) | 10.7 | 1.2 |
| clumped-20000 | zoom | 3.2 | 2.1 | 0 | 20040 | 204.0 (185.9/16.9/1.2) | 21.4 | 2.4 |
| clumped-20000 | pan | 1.0 | 0.3 | 0 | 20040 | 209.6 (190.7/17.6/1.3) | 21.9 | 2.5 |
| mixed-1000 | zoom | 34.6 | 26.3 | 0 | 1040 | 6.0 (5.1/0.9/0.1) | 1.2 | 0.2 |
| mixed-5000 | zoom | 13.1 | 9.1 | 0 | 5040 | 33.1 (28.5/4.4/0.3) | 5.7 | 0.8 |
| mixed-20000 | rest | 17.3 | 10.6 | 0 | 20040 | 169.0 (150.6/17.1/1.3) | 19.9 | 2.6 |
| mixed-20000 | zoom | 4.4 | 2.4 | 0 | 20040 | 165.9 (147.9/16.8/1.2) | 20.9 | 2.5 |
| names-1000 | zoom | 34.7 | 27.5 | 0 | 1040 | 6.1 (5.2/0.9/0.0) | 1.4 | 0.2 |
| names-5000 | zoom | 11.1 | 7.3 | 0 | 5040 | 39.3 (34.6/4.4/0.3) | 6.9 | 1.1 |
| names-20000 | zoom | 3.1 | 2.0 | 0 | 20040 | 222.1 (201.7/19.1/1.3) | 25.6 | 3.6 |
| uniform-5000 + search "a" | zoom | 10.6 | 6.6 | 0 | 5040 | 32.1 (27.1/4.9/0.2) | 6.5 | 2.8 |

Rows with a pass of 0.0 are "rest" phases where the map did not request a pass in the window, not a free pass. The `rest` rows at 20,000 pins (uniform, clumped, names) read 0.0 fps (no frame completed that the counter could use, cause not investigated), so they are not usable; the zoom and pan rows are.

**Reading it:** the pass is the cost that matters. Crowding grows linearly with the pin count (0.8 ms at 1k, 17 ms at 20k), the cluster pass is negligible (under 2 ms even at 20k), and the label pass grows faster than the pin count: 5 ms, 62 ms and 360 ms at 1k, 5k and 20k (uniform). Cause, read from the code: for each name asked, `Collides` measured the rectangle of every name already shown. Fix below.

## Where the label pass goes (stage timers inside `LabelCrowding.Apply`) **[measured]**

At the bench's default zoom of 0.3 (29% zoomed out, past `HideLabelsFromZoom` 20%) the label pass is the "hide every name" path, not collision: the game re-activates every pin's label on each map pass and the mod switches the ones it does not want back off. At 5,000 pins that stage is about 60 ms; at 20,000 about 390 ms. Collision, grouping and sorting are each under 1 ms at 5,000 pins. That cost is the game's activate / our deactivate churn and the per-label fade lookups; it is **not fixed in this branch** (see "Not done").

Zoom 0.1 (labels shown, collision running), `v22b zoom 0.1`, zoom phase (4 s of wheel notches):

| scene | pins | fps avg | fps worst 5% | pass ms | labels ms | before-the-loop ms (hide out-of-sight) | grouping loop ms | sort ms | crowding ms |
|---|---|---|---|---|---|---|---|---|---|
| mixed-1000 | 1040 | 26.2 | 12.2 | - | 16.2 | 1.5 | 0.08 | 0.04 | 0.7 |
| mixed-5000 | 5040 | 7.5 | 4.3 | - | 87.9 | 60.8 | 0.87 | 0.12 | 3.8 |
| mixed-20000 | 20040 | 1.3 | 1.2 | - | 430.2 | 388.5 | 2.95 | 0.15 | 15.1 |
| uniform-5000 | 5040 | 11.3 | 3.8 | - | 33.6 | 0.25 | 0.25 | 0.15 | 3.4 |

PinFade.Step per frame at those scenes: 1.5 / 6.7 / 22.7 / 2.8 ms.

## Collision test A/B **[measured]**, uniform-5000 at zoom 0.1, zoom phase

`LabelCrowding.CollideMode` (BENCH builds only): 0 = grid over stored rectangles (this branch), 1 = linear over stored rectangles, 2 = linear, measuring every shown name again for every name asked (how 2.1.3 did it).

| mode | label pass mean ms | worst ms | fps avg | fps worst 5% |
|---|---|---|---|---|
| 0 grid | 32.1 and 34.5 (two runs) | 39.9 / 40.9 | 12.3 / 11.5 | 4.2 / 4.6 |
| 1 linear, stored rects | 37.0 | 43.7 | 10.1 | 6.9 |
| 2 as 2.1.3 | 89.0 | 167.5 | 4.5 | 4.1 |

So at 5,000 pins with labels showing, caching each shown name's rectangle takes the pass from 89 ms to 37 ms, and the grid takes a further 10%. The grid is a small win over the cache alone and is kept.

## Placement **[measured]**, 200-group test map, `v22b labels`

Names moved are counted against a snapshot taken with placement off, since the game's own label offsets are not all zero.

| zoom | names shown, off | names shown, on | moved | overlapping pairs off / on | partly outside the map off / on |
|---|---|---|---|---|---|
| 0.12 | 147 | 205 | 8 | 11 / 13 | 0 / 0 |
| 0.06 | 305 | 413 | 44 | 5 / 9 | 11 / 10 |
| 0.20 | 92 | 112 | 18 | 22 / 21 | 0 / 0 |

With placement on, 20-35% more names stay visible instead of being hidden. The overlap count is the pilot's own estimate from text sizes and moves by a few pairs either way.

## Not done
- The hide-everything churn above. Likely routes: patch the game's per-label `SetActive(true)` (inside `UpdatePins`) so labels the mod hides are not woken each pass, or keep hidden labels active at alpha 0. Both change how labels fade and show on hover, so they need a design decision, not a quick fix.
- `PinFade.Step` is linear in pin count (1 ms per 1,000 pins per frame). Skipping off-screen pins would cut it; not done.
- 20,000-pin "rest" rows could not be read (see above). Above 5,000 pins the frame rate is below 10 fps while the map is moving in these scenes (2.2; 2.1.3 was not run on them).
- No 2.1.3 run of these exact scenes: the 2.1.3 comparison is only the 8,357-pin and 755-pin runs in `V22_RESULTS.md`.

## 2.2.0 against 2.1.3, same scenes, same game session type **[measured]** 2026-11-03

Release DLLs, both with `ClusterPins` on (the bench scene turns it on) and 1,000 or 5,000 unsaved uniform pins; "rest" is `fps 5` with the pointer parked, "sweep" is `mapsweep 6` (zoom fully in to fully out and back, set directly, so it is the same for both versions). 2.1.3 is the DLL from `dist/Carturs_Map_Pins-2.1.3.zip`.

| pins | zoom | rest ms/frame 2.1.3 | rest ms/frame 2.2.0 | sweep frames in 6 s 2.1.3 | sweep frames 2.2.0 | icon flicker pins 2.1.3 / 2.2.0 | label flicker pins 2.1.3 / 2.2.0 |
|---|---|---|---|---|---|---|---|
| 1,000 | 0.3 | 16.3 | 17.1 | 191-197 | 254 | 88 / 47 | 21 / 0 |
| 1,000 | 0.1 | 14.4 | 15.4 | 193-201 | 253 | 89 / 47 | 22 / 0 |
| 5,000 | 0.3 | 33.6 | 34.4 | 45-46 | 49 | 243 / 213 | 211 / 0 |
| 5,000 | 0.1 | 23.8 | 22.6 | 43-47 | 56 | 134 / 78 | 241 / 0 |

Before the fade list was added, 2.2.0 idled at 38.6 ms at 5,000 pins and zoom 0.3 (5 ms a frame over 2.1.3), because the per-frame fade step looked at every hidden name that now stays on at no opacity. It now visits only names that are fading.

Frame-rate drops while zooming (pilot-driven wheel notches, 4 s, Debug/BENCH build): eased zoom against instant zoom at the same step: 1,000 pins 58.4 against 65.4 fps (worst 5%: 21.2 against 23.8); 5,000 pins 10.2 against 9.9 fps. Zooming itself is the cost at large pin counts: the game builds and removes the marker of every pin that enters or leaves the view each time the view changes.

Not measured: a real mouse wheel. Real OS wheel and key events did not reach the game from the pilot (`ZInput.GetMouseScrollWheel()` stayed 0 for both `mouse_event` and a posted `WM_MOUSEWHEEL`).
