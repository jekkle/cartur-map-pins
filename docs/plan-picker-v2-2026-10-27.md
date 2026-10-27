# Map Pins: picker v2, colours and Hugin (owner request 2026-10-27)

Every step is its own commit. Nothing is published; everything ships together as Map Pins 2.0.
"Measure" steps read the real game before any code is written (CLAUDE.md: never guess).

## A. Icon picker on the map screen

1. **Pop matches Epic Loot's.** Measured before building: log each cell's scale every 4 frames while hovered (ours,
   vanilla button, Epic Loot button) plus every component on it and whether it is enabled. Today Epic Loot's reads 1.14 and
   ours/vanilla read 1.05-1.09, so something else is writing scale on ours (suspect Cartur UI's HoverFx re-enabling, or the
   moved button's own script). Fix the cause, then all three read the same.
2. **Top bar.** The header becomes: grip, "PINS", then on the right "Visible to other players" with a checkbox in our style
   (same bronze box as the Hugin menu). It drives vanilla's own `Minimap.m_publicPosition` toggle, so the game's behaviour is
   unchanged; the vanilla toggle and its label are hidden from the bottom of the map. The "shift-click" hint leaves the header (see 5).
3. **Whole header drags the panel**, not just the corner grip. The checkbox sits on top of it and keeps its own click.
4. **Bottom hint bar** (Add pin / Cross off pin / Remove pin / Ping), measured first, then two entries added in the same style:
   - `[H] Hide Pins` and `[Shift] + click` "Change Icon" as keyboard-key caps (drawn rounded caps with the letter).
   - The H entry names what the **next press** does, so it is always true: all pins showing -> "Hide Pins";
     all hidden -> "My Pins Only"; only my pins showing -> "Show All Pins". (Answer to "is that the proper settings?": yes, that is
     the cycle H already does: press 1 hides all, press 2 shows only your own pins, press 3 shows everything.)
5. **Shift-click on the game's own pins (spawn, death, shared).** Today they are refused on purpose: vanilla rebuilds the spawn
   and death markers from your profile every few seconds (they are not saved pins), so a name or icon would vanish. Plan:
   colour, size and opacity are already stored by position, which survives the rebuild, so allow those at once; icon override
   is done the same way if the rebuild can be hooked after it writes the type (Minimap.UpdateProfilePins - read it first).
   Name stays vanilla's for those two.

## B. Colours

6. **One colour control everywhere** (the shift-click editor and the Hugin menu): a colour wheel (hue ring + brightness bar)
   and the **last 5 colours used** underneath, plus "Default". The 8-swatch row goes. Recent colours are saved per player
   (`BepInEx/config`, one small file), newest first, no duplicates.
7. **Default colour per pin from the object it stands for**: copper brown, tin pale, silver, flametal orange, each berry
   its berry (already done for ores and berries), then bosses, dungeons, camps, landmarks, spawners, traders. Built as a table
   per kind, **shown to you as a screenshot of the Hugin colour page before it ships** (nothing guessed from names alone;
   for kinds where the real object has no obvious colour the pin stays white). Stored as one setting per kind, so the
   player's change survives updates.
8. **Hugin menu colours** show each kind's colour from the player's own settings (the same entries), and Done writes only the
   ones changed, like the switches and icons now do. Click the small colour dot beside an item's icon to open the wheel.

## C. Order of work

1. Measuring launch (read-only): pop scales and components, bottom hint bar hierarchy, `m_publicPosition` and its label,
   UpdateProfilePins. One launch, queued with the shader session if it is running.
2. Code A1-A5 as small commits; one filming launch for the owner (close-up clip + a screenshot of the whole map screen).
3. Code B6-B8; screenshots of the wheel, the recent row, and the Hugin colour page (all pages).
4. Then the rest of the improvement plan: favourites and recents in the picker (Release 4), portals (5), then clusters,
   hover popup and priorities (6-9), and the 2.0 write-up, store pages and replies.

## D. Not in this plan

The Cartur's UI map and minimap border (knots out of the corners, border sized to hug both maps) is a separate session, "Cartur UI
map border", started from `D:\Ai\Modding\Valheim\handoff-ui-map-border-2026-10-27.md`. It is told not to move or rename the
objects Map Pins reads.
