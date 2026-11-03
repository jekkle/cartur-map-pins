# v2.2 decisions that need the owner

Each row: recommendation first. "Complexity" is a rough size of the code involved (S under ~50 lines, M ~50-200, L 200+), an estimate, not a measurement.

## D1. Default wheel step: 6% (spec) vs vanilla 10%
- **Recommend:** 6% as specified, setting range 1-25.
- Alternatives: 10% (vanilla feel, 44 notches across the range); 4%.
- Benefit: finer control, matches the brief. Risk: 6% needs ~72 notches across 0.015..1 (vs ~44 at 10%), so crossing the whole map feels slower; M/Z keys still jump 50%.
- Compat: none (setting). Complexity: S.

## D2. Smoothing definition and anchor tolerance
- **Recommend:** exponential approach in log-zoom; "160 ms" = 98% of the way (tau 41 ms); finite stop at 0.15%; anchor tolerance < 1 px at rest.
- Alternatives: fixed-duration cubic ease (exact end time, but speed jumps when a notch retargets); critically damped spring (smoother, heavier, can overshoot without extra care).
- Benefit: continuous velocity, any frame rate, trivially interruptible. Risk: "feel" is subjective; the duration setting is the tuning knob.
- Complexity: S (ZoomMath).

## D3. Cluster click default: Inspector vs today's zoom-in
- **Recommend:** `ClusterClickAction = Inspector` (as the brief says) **and** keep `Zoom` selectable; call it out in the changelog.
- Alternatives: default `Zoom` to leave existing `ClusterPins` users unchanged.
- Benefit of Inspector: fixes the "cluster never opens" case (audit C4). Risk: changes behaviour for current users of an off-by-default setting (small audience).
- Complexity: M-L (inspector panel, pooled rows, filter, input focus).

## D4. `SmartLabelPlacement` default ON
- **Recommend:** ON, per the brief, but only after the S1 probe passes; priority classes (portals beat chest spam) ship regardless.
- Alternatives: default OFF for one release.
- Risk: every user's labels move on update. If candidate offsets cannot be made to stick (S1), the placement half is dropped, priority half stays.
- Complexity: M.

## D5. Mixed-category clustering: how "compatible" is defined
- **Recommend:** reuse `PriorityZoom` tiers (resources / landmarks / pickables are separate classes; tier 0 never joins). No exclusion config until asked.
- Alternatives: one bucket for all non-tier-0; user-editable category groups; per-category exclusion list now.
- Benefit: no hard-coded icon IDs, already reviewed grouping. Risk: tiers were designed for zoom LOD, not clustering, so e.g. ore+chest in one badge may surprise.
- Complexity: S-M.

## D6. Mixed-cluster badge look
- **Recommend:** owner picks. I will render 3 options (tinted ring, small stacked pips, "+") beside the current one for a screenshot choice, per the standing asset rule.
- Complexity: S.

## D7. Spiderfy: ship or defer
- **Recommend:** ship as specified (off by default, max 12), mouse-only; large clusters use the inspector.
- Alternatives: defer to 2.3 and ship inspector first (spiderfy is the largest single risk for hit-testing and delete conflicts).
- Complexity: M-L.

## D8. Badge count-change animation
- **Recommend:** skip unless asked (ponytail). Fade of members already exists.
- Complexity: S if wanted.

## D9. Performance bars
- **Recommend:** 5k pins keeps worst-5% fps within 10% of 2.1.3; zero steady-state allocations in the three passes; smooth zoom costs no more than 10% worst-5% fps vs instant on 5k.
- Alternatives: tighter, or no bar until the first measurement.
- Complexity: n/a.

## D10. API surface and delivery
- **Recommend:** the small `MapPinsApi` in section 2 of `WORLDATLAS_INTEGRATION.md` (snapshot, `Changed`, `Reset`), built after phases 1-4, no sprites in the API.
- Alternatives: defer entirely until WorldAtlas exists (cheapest; recommended if WorldAtlas is months away); include sprites via a separate call.
- Risk: designing against a consumer that does not exist yet. Complexity: S-M.

## D11. Unit-test project for the pure math
- **Recommend:** yes, a tiny source-linked xunit project for `ZoomMath` and `LabelGeometry` only.
- Alternatives: in-game checks only (the repo's current practice).
- Benefit: drift and convergence bugs are found without a relaunch. Risk: a second csproj to maintain; kept out of the release pack. Complexity: S.

## D12. Version and release
- **Recommend:** work on branch `v2.2`; code version stays 2.1.3 until an in-game-tested build exists; release candidate packed locally, not uploaded; PR opened for review, not merged. The Discord/changelog step (memory: release includes Discord) is yours at release time.
- Complexity: n/a.

## D13. Order of phases
- **Recommend:** 1 (zoom) -> 2a (checksum, small) -> 3-priority (small) -> 2b inspector -> 4 measure -> 3-placement -> 2c spiderfy -> 5. Differs from the brief only by pulling the two small safe fixes forward and keeping the riskiest items last.
- Alternative: the brief's strict 1-2-3-4-5 order.

## D14. Testing logistics
- **Need from you:** a gamepad test pass (none available here), a second client or server for multiplayer, and the Dev profile free for pilot runs. Without these those gates stay NOT VERIFIED.
