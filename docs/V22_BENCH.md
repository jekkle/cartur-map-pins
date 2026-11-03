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

## After the label-pass fix
_(to be filled in by the rerun)_
