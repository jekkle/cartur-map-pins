# Cartur's Map Pins: improvement plan (2026-10-27)

Labels: **[code]** read from the mod source or the decompiled game (via the two research files), **[research]** from `research/pin-clustering/`, **[feedback]** a player request from the sweep, **[assumption]** my guess, not read or measured. Nothing here was built or run.

Sources: [research] marks come from `research/pin-clustering/ux_reference.md` (games and map apps: clustering, timings, popups) and `research/pin-clustering/valheim_feasibility.md` (how Valheim draws pins, hook points, risks). Read both before building any release below.

Short version: 1.8.9 ships first, unchanged. Then smooth zoom (no flicker or jumping, owner request), then a Hugin first-install setup menu for choosing default pins (owner request), then two cheap releases that answer real player requests (hide-all hotkey, picker favourites). Then grouping into clusters, built in three small steps (badges, motion, popup). Each step is behind a setting, off until you have seen a screenshot.

---

## 1. Today in one page

What a player sees now, one line each, with the weak spot.

**Auto pins**
- Auto-pinning of ore, dungeons, camps, runestones, chests, traders, beehives, pickables etc. within 60 m (`DiscoveryRadius`). Tick on F8 toggles it. [code] Weak: nothing for portals; Mistlands ruins only as Landmarks, off by default [feedback IllGib81].
- Per-kind icons (copper vs tin, Burial Chamber vs Frost Cave) from a 158-icon sheet. [code]
- Ore tints by type, berry tints by type (new 1.8.9). [code] Weak: a player (DrGrue-US) wants colour-coded berries without text; tints exist, but that is not obvious.
- Trackers for boats, carts, tames, wild riders. [code]
- Giant Armor off by default; confused one player. [feedback] Decision 2 below.

**Map drawing**
- Merge repeated pins: same name + same type in one grid cell become one pin with a count ("Tin x15"). [code, Crowding.cs] Weak: only same-name; a copper next to a tin next to a berry still pile up. Fixed world grid, so two pins either side of a grid line never merge.
- Shrink crowded pins and shrink everything when zoomed out (`ShrinkCrowdedPins`, `ShrinkPinsFromZoom` 15%, `ZoomedOutPinScale`). [code]
- Label hiding: hide all names when zoomed out (`HideLabelsFromZoom` 20%), and hide labels that overlap (rarest kept) except near the cursor. [code, LabelCrowding.cs] Weak: hard on/off; the vanilla zoom rule is dead (`m_showNamesZoom` 2.0) so the mod owns it. [code, memory]
- Fade: every pin eases in and out of view, size and transparency (PinFade.cs). [code] This is the engine the new animation can ride on.
- Hide list: names you never want to see, kept in the save but not drawn. `HidePinsBeyond` range limit. [code]
- Search box on the map: typing dims non-matches rather than hiding. [code, PinFilter.cs]
- Hover: the pin under the mouse grows and comes to front; new 1.8.9 tag says "Auto pin / Your pin / Shared pin". [code, PinHover.cs]

**Hand-placed pins**
- Icon picker on the large map: scrollable grid of all 158+ icons next to vanilla's seven buttons; movable panel. [code, MapIconPicker.cs] Weak: flat list, no favourites or recents, shows every unused icon [feedback Skoorb, AnimalCrazy85, DrGrue-US].
- Pin editor: shift-click a pin to rename or re-icon. [code]
- Safer delete: right-click removes the highlighted pin, optional confirm, Ctrl+Z undo (1.8.9). [code, PinDelete.cs]

**Housekeeping**
- Pin sharing modes, presets, `ResetPins`, console commands (`carturpins_*`), per-world side-car record files. [code]

**Biggest weakness overall:** on a dense late-game map there is still no way to see "a lot of stuff here" at a glance, and no quick "hide everything" switch; one player says dense maps lag zoomed out [feedback tazmeister]. Cause of the lag is [assumption]: the pass over all pins runs every drag frame, but cost was never measured.

---

## 2. Conflicts: where the new design clashes with today's systems

The research recommends clustering as a visual layer only: cluster badges are our own pooled UI objects, never real pins, never saved, never shared. [research feasibility 1] That removes most save and multiplayer risk. The clashes are all inside the map UI.

1. **Crowding's same-name merge vs new clusters.**
   - Clash: both decide "which pins are drawn". Two systems collapsing the same pins gives wrong counts or double-hiding.
   - Resolve: run clustering as a second pass after merge, inside the same `Crowding.Measure`. Merge keeps its job (identical stacks, "Tin x15"); the badge counts real pins using the existing merge counts. [research]
   - Later (section 3) merge becomes the "same type" level of clustering. Do not remove it until clusters are proven.
2. **`OutOfSight` / `NotDrawn` plumbing.** Hover, labels, `LabelTemplates` and the patched `GetClosestPin` already skip pins in `NotDrawn`. [code] Resolve: cluster members join that set. Free correct behaviour for labels, hover, delete targeting. Animation needs a second flag ("visually hidden") so a member can still be mid-move; add it in release 5, not before.
3. **LabelCrowding.** Members' labels are hidden automatically (they are out of sight). Badge gets its own count text. Resolve: cluster pass runs before `LabelCrowding.Apply`. [research Phase 1]
4. **PinFade.** Its eased alpha/scale per marker is the animation engine. Clash: it assumes the position is vanilla's. Resolve: capture the true position right after vanilla writes it, then the Update postfix adds the offset toward the cluster centre. Settled pins cost nothing. [research Phase 2]
5. **PinHover.Current + PinDelete + vanilla click routing (the dangerous one).**
   - Clash: right-click on a badge finds no hovered pin, then falls back to vanilla `GetClosestPin`, which could delete a neighbour that happens to sit close. Left click could toggle or claim the wrong pin. [research feasibility 4]
   - Resolve: `PinHover.Current` stays a real pin or null. Add a separate `CurrentCluster`. Block left and right click over a badge (Prefix on `OnMapLeftClick` and the existing `PinDelete` Prefix). Left click zooms in instead. This guard ships in the same release as the first badge, never later.
   - Gamepad: delete and tick act on the screen centre; with a badge there they would hit the nearest drawn pin. Accept and document, or intercept. [assumption: needs an in-game test]
6. **Shared pins.** Other players' pins have an owner id and are claimed by left click. Resolve: never cluster shared pins, area pins (raid circle) or moving pins (boats, pings, death). [research]
7. **Player-placed pins.** The research says never cluster the player's own pins. Today's merge only touches named auto-style pins too. Resolve: cluster only pins the mod placed (Auto pin tag in 1.8.9 already tells them apart [code]) plus an "include my pins" choice later if asked [assumption].
8. **Search / filter.** Dimming non-matches while a cluster hides them defeats the point of searching. Resolve: pins matching the search leave their cluster while a search is active. [research risk 6]
9. **Hide list, range limit, editor, filter changes** must dirty the cluster cache or badges show stale counts. Resolve: one "pins changed" flag set by those code paths. [research risk 5]
10. **Minimap vs large map.** The minimap has separate roots, no hover, no click, and a different icon size. Resolve: clusters on the large map only at first; the minimap keeps today's shrink and merge. Badges on the minimap are a later option (needs a ring that reads at 20 px with no text). [research]
11. **Performance.** `Crowding.Measure` already runs several full passes per drag frame on all pins; cost never measured. Resolve: cluster recompute is event-driven (zoom step changed, pin added/removed, hide set changed), never per frame. First action of release 4 is a one-shot timing log on a dense world. Hide-all (release 1) also answers the lag request directly.
12. **Vanilla duplicates / game updates.** Clustering needs the private `m_pins` list and `OnMapLeftClick`; if a hook is missing, log a warning and run with clustering off. [research risk 9]
13. **Other pin mods.** Not surveyed. Clustering is a switch, so a conflict can be turned off. [assumption]

---

## 3. Replace or upgrade (keep what works)

| Today | Becomes | Why | Keep |
|---|---|---|---|
| Same-name merge, fixed grid | Category clustering with count badge (merge stays as the "same type" step) | Handles mixed piles; rounder groups than a grid [research] | Merge until clusters ship and are trusted |
| Shrink crowded pins | Cluster when 3+ pins crowd; shrink for pairs | A pair is still readable; a pile is not | Shrink for pairs and for free pins |
| Hard label hide at 20% zoom | Priority LOD: bosses, dungeons, then ores, berries last; categories become a cluster before they vanish | Zoomed-out map keeps the useful things [research] | Label-overlap culling (it works) |
| One zoom scale for all pins | Scale curve per zoom (about 0.6x to 1.0x) [research, numbers are proposals] | Legible when zoomed out | `ZoomedOutPinScale` as the floor |
| Hover tag "Auto pin/Your pin" | Hover popup: grouped list for a cluster; same tag stays for single pins | Answers "what is in that blob" | The tag for single pins (new in 1.8.9) |
| Flat icon picker | Favourites + recently used on top; icons nobody uses hidden behind a "show all" | Two requests [feedback] | The grid, drag, search |
| Config-only hide list | Hide/show-all hotkey on the map, plus the list stays | Lag request [feedback tazmeister] | Hide list |
| Fade (alpha) only | Fade + travel to the cluster centre | Split and merge read as one thing, not a pop | PinFade easing |
| No portal / ruin pins | New categories (see 4) | Request [feedback IllGib81] | -- |
| "Giant Armor off" | Decide default (decision 2) | Confused a player | The per-kind switch |

Not replaced: auto-pin detection, icon sheet, trackers, undo/delete, pin sharing, editor. They work; leave them.

---

## 4. New features

Effort S/M/L: S under a day, M a few days, L a week or more [assumption]. Default = what ships.

1. **Hide/show-all hotkey** (large map). Value: high (lag, clean screenshots, one request). Effort S. Risk low. Default on, key unbound or a sensible key (decision 4). Hides drawn pins by fading, keeps player-placed pins optional. Art: none.
2. **Picker favourites + recents + hide unused.** Value: high (3 requests). Effort S-M. Risk low (own UI, own file). Default on. Star on a cell, a "Recent" row, unused icons collapsed. Stored in a small user file next to the config, per profile [assumption]. Art: a small star mark in the Cartur style.
3. **Portal tracking.** Value: medium-high. Effort M. Risk: medium; how portals are found and named (tag text) is **not read yet** [assumption]; read the game code first. Icon `PoiPortal` already exists in the sheet [code]. Default on if the read is clean.
4. **Mistlands ruins.** The Mistlands towers and excavations are already pinned (Subtypes.cs [code]); what the player saw is mostly Landmarks, off by default. Effort S: a clearer explanation first, then maybe a "Ruins" group switch. Wait for the reporter to say which "tree" (#8) before touching code.
5. **Clusters with count badges.** Value: highest. Effort M-L. Risk: click routing (section 2.5); stale caches. Default **off** until you approve a screenshot. Large map only. Starting numbers: radius about 1.1x icon width, minimum 3 pins, no clustering when fully zoomed in [research, all unmeasured, tune in game]. Art: badge ring (below).
6. **Zoom LOD / priority.** Value: high on big maps. Effort M. Risk: medium (hides things people wanted [assumption]; keep player pins and bosses always visible [research]). Default off at first, with the cluster toggle.
7. **Smooth split/merge animation with hysteresis.** Value: medium, makes clusters feel right. Effort M. Risk: flicker at a zoom edge (fix: merge and split at different distances, about 8-15% apart, hold about 0.15 s [research proposal]); frame cost (check with pilot perf mode). Default on whenever clusters are on; no timing knobs.
8. **Hover popup listing grouped pins; click to zoom.** Value: high. Effort M-L. Risk: popup blocking map input [assumption, test]. Rows: icon, name x count, distance; about 8 rows then "+N more". Mouse first; gamepad shows the list when a badge is under the crosshair, no selection in v1 [research]. Default on with clusters.
9. **"Done" pins fade** (mined ore, looted dungeon): Elden Ring lesson [research]. ForgetMined and TickWhenLooted already exist [code]; the new part is dimming ticked pins. Effort S. Default off, only if asked. Do not build now.

**Art needed** (you pick, nothing chosen for you [feedback-assets-owner-chooses]):
- Badge ring: bronze knot ring from the Cartur Discord-logo style [memory cartur-icon-style], three tiers by ring finish (2-9 plain, 10-99 thicker, 100+ studded) [research]; legible at about 20 px. Generate 3 options with Grok/ComfyUI, shown beside the current round pin for comparison.
- Star mark for favourites; popup frame cut from the existing bronze rail art, not generated [memory art direction].

---

## 5. UI polish

- **Popup and badge look.** Reuse the existing panel recipe: black panel at 0.85 alpha, warm text, bronze rail edge from `bar_frame` style, safe TMP font (`Fonts.AddLabel`). [code] Do not prompt diffusion for frames [memory].
- **Picker panel.** Same size and position (263 x 273, movable). Add a small row at the top: Recent, then Favourites, then the rest. A "show unused" toggle. Keep the search bar as is.
- **Consistency with Cartur's UI.** Thin rail for small panels, ornate knots only on big ones [memory art direction]. The popup is small, so thin. The picker is medium; match what exists.
- **Hover tag.** Keep the 1.8.9 tag but make the cluster popup the same family (same colours, same fade-in).
- **Hotkey hint.** One small line on the map ("H: hide pins") only if you want it; assumption that it helps discovery.
- **README and Nexus page.** Real in-game screenshots of each shipped feature, never banners [memory feedback-readme-real-screenshots]. Each release below names its screenshot.

---

## 6. Roadmap (each release works alone, behind a setting)

Order = cheapest and most valuable first. "Pilot" = the test pilot, carturtest character only, one launch per batch [memory].

**Release 0: 1.8.9, unchanged.** Already built and tested 9/9. Needs your OK on the hover shot and the Giant Armor default, then publish (Thunderstore, Nexus, Hexium, Discord announcements and changelog) and show replies before posting. Nothing below blocks it.

**Release 1 (1.9.0): smooth zoom (owner request 2026-10-27).** Today's pins can pop and jump while zooming, even without clusters: the same-name merge (Crowding) and the zoom label hiding (LabelCrowding) switch at hard thresholds, PinFade eases alpha/scale but positions snap, and icon size steps with zoom [code reading, not yet filmed].
- Step 0, measure first: pilot `record` of a zoom-in/zoom-out sweep over a dense test map; list every pin that flickers (toggles at a threshold) or jumps (position snaps). Same recording after, as the before/after for the owner.
- Hysteresis on every threshold: merge/hide at one zoom, split/show at a slightly different one (about 8-15% apart [research]) plus a ~0.15 s hold.
- Glide, don't snap: merging pins travel into the survivor and splitting pins travel out of it over ~0.2 s (ease-out position, ease-in opacity [research: Leaflet 0.3 s]), riding on PinFade. When many pins move at once, stagger each start by ~10 ms, capped at 80 ms total [research: ux_reference.md]. Apply the offset in the Minimap.Update postfix right after vanilla's UpdatePins writes the true position, so a pan mid-animation never snaps; settled pins cost nothing [research: valheim_feasibility.md]. Labels fade instead of popping. Icon scale follows zoom as a continuous curve.
- This motion code is the same engine Release 7 (cluster split/merge) reuses, so it is not throwaway. No conflict with the Hugin setup (Release 2): different systems. One shared rule: the setup menu must not open while the map is open.
- Pilot: zoom sweep, count frames where any pin is neither at home nor on its path (target 0); perf-mode frame time before/after. Owner sees the before/after clips.

**Release 2 (1.9.1): Hugin first-install setup (owner request 2026-10-27).**
- What it is: on first install Hugin lands; talking to him opens a polished setup menu to choose what Map Pins pins by default. One category per page (Ores, Berries & mushrooms, Dungeons, Landmarks, Bosses & altars, Creatures/tames, Giant remains, ...), each item a toggle with its **simple player-facing name** (e.g. "Copper", "Red mushroom", "Giant sword"), never the prefab/asset name. Each page has **Select all**, **Deselect all** and **Undo** (back to how that page was when opened) beside the category title. Next/Back between pages, Done saves.
- Writes the same config entries the settings already use (the KindsOffByDefault / per-kind switches), so it only sets defaults; everything stays editable later in the config. A "Not now" leaves today's defaults; a console command or config button reopens it.
- Shares Hugin with Cartur's UI: Cartur's UI already has a once-per-install Hugin welcome (cartur-ui-hud Welcome.cs, flag `BepInEx/config/cartur.welcome.seen`, respects tutorials-off, approved text). If both mods are installed there must be ONE raven: the welcome first, then the Map Pins setup offered in the same visit. Each mod keeps its own seen-flag; Map Pins works alone when the UI is not installed. Soft-depend, no hard dependency [CLAUDE.md "Other mods"].
- Look: must look super polished: same panel family as Cartur's UI (black panel 0.85, warm text, bronze rail edge from bar_frame, safe TMP font), icons beside each name, the Cartur icon style. Diffusion is not used for frames [memory art direction]; any new art goes to the owner as options.
- Names: a display-name table per kind (most exist in the label templates / PinCatalog; any missing get a plain name the owner reviews before release). Never guess what an item is: read the prefab classification.
- Pilot: fresh profile (delete the seen-flag), check Hugin lands, the menu opens, select all / deselect all / undo change only that page, Done writes the expected config values, second launch has no Hugin. Screenshot per page for the owner before shipping.
- Conflict check: tutorials-off must still be respected (offer the menu from the config instead); no Hugin on a dedicated server; the menu must block map input while open.

**Release 3 (1.9.2): hide/show-all hotkey, and the Giant Armor decision.**
- Pilot: toggle with a few hundred test pins (unsaved), check pins vanish and return, hand-placed pins follow the chosen rule, nothing written to the save.
- Screenshot for you: map with pins on, then the same map hidden.

**Release 4 (1.9.3): picker favourites, recents, hide unused.**
- Pilot: star two icons, restart, check the stars persist; place a pin and check it appears in Recent; unused icons collapsed; the existing icon indexes in saves unchanged.
- Screenshot: the picker showing the new top rows.

**Release 5 (1.9.4): portals (and a ruins group if the reporter confirms).** First a read-only look at how the game names and stores portals. Pilot: place a test portal, check pin, name and dedupe. Screenshot: portal pins on the map.

**Release 6 (1.10.0): clusters with badges, no animation, off by default.** Includes the click guard. First a one-shot log of pin counts and recompute time on a dense world.
- Pilot: fill about 1000 test pins, sweep zoom in ten steps, check badge counts plus free pins equal the total; pan and confirm groups do not change; right-click and left-click on a badge change no pin; quit without saving.
- Screenshots (you pick): three badge ring options; then the winner on a dense map, zoomed out and zoomed in. Flip the default to on only after you approve.

**Release 7 (1.10.1): cluster split/merge animation**, reusing the Release 1 smooth-zoom motion and hysteresis. Pilot: record a zoom in and out, no frame where a pin is neither home nor on its path; compare frame rate before and after in perf mode; a pan mid-animation does not snap. Screenshot: two frames of a split.

**Release 8 (1.10.2): hover popup and click to zoom.** Pilot: needs a way to fake the cursor (pilot has no pointer command yet [research]); check rows equal groups, counts add up, click zoom leaves the cluster open. Screenshot: popup over a dense Meadows ore field.

**Release 9 (1.11.0): priority LOD and zoom scale curve.** Pilot: sweep zoom, check bosses, dungeons and player pins survive zoom-out. Screenshot: world view before and after.

After 7-9 settle, review whether the same-name merge can be removed (it may be redundant) [assumption].

---

## 7. Open decisions for you

1. Clusters off by default for one release after they land, or on straight away once you approve the screenshot?
2. Giant Armor on by default, yes or no (README says off)?
3. Should clusters include hand-placed pins, or only auto pins?
4. Hide/show-all: which key, and should it hide your own hand-placed pins too?
5. Badge ring: OK to generate three Grok/ComfyUI options in the Cartur logo style for you to pick?
6. Clustering on the minimap too, or large map only?
7. Priority order when zoomed out: player pins, bosses, dungeons, ores, berries last. Right for you?
8. Portals: any pin name format you want (e.g. the portal's tag text)?
9. Ask the "tree in Mistlands" reporter (#8) which one before any ruin work?
10. Mark mined or looted pins as done (dim them), or leave as is?
11. Ship 1.9.0 and 1.9.1 as small releases first, or hold everything for the cluster release?
