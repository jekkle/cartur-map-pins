# 2.0 changelog draft (goes into package/CHANGELOG.md only when every item is tested and the owner says ship)

## 2.0.0

**Hugin sets it up with you.** The first time you load a world after installing or updating, Hugin lands and offers to choose what gets pinned. Talk to him and a menu opens: one page per kind of place (ores, dungeons, camps, bosses, berries, landmarks, spawners, the rest), with a tick, an icon and a colour for every item, **Select all / Deselect all / Undo** on each page, and a hover description with where it is found. It opens on your own settings and writes back only what you change, so an update does not touch yours. Choose who sees your pins at the cartography table, and turn on **New default colours**. `carturpins_setup` opens it any time.

**Pin colours.**
- A **colour wheel** with your last five colours, in the shift-click editor and in Hugin's menu.
- **New default colours:** each new pin takes the colour of the thing it marks (copper brown, a Fuling village orange, each berry its own). Only new pins; a button in Hugin's menu (and `carturpins_applycolours`) gives the pins already on your map the same. A colour you set by hand always wins. One setting per kind under *Kind Colours*.

**A better picker on the map screen.**
- A top bar you can drag by anywhere, with **Visible to other players** in it.
- The game's pin buttons and Epic Loot's moved into the picker (the old columns are gone).
- Icons without boxes, the same pop on hover as the inventory, smoother scrolling.
- **Favourites and recent icons** on top: star any icon.
- The old icon set folds away until you open it.
- Hint bar: **[H] Hide Pins** and **[Shift]+click Change Icon**.

**Hide pins with H.** First press hides every pin, second shows only your own, third shows all again. Names go with the pins. Nothing is deleted.

**Shift-click works on your spawn point and death marker too** (colour, size and opacity).

**Smooth zoom.** Pins and names no longer flicker, jump or pop as you zoom; they fade. (Measured on 715 test pins: flickering pins 590 to 0, sudden size jumps 2,051 to 0.)

**Portals** are pinned with the tag you gave them. New category *Portal*.

**Clusters (off by default, `General / ClusterPins`).** Crowded pins with the same icon collapse into one pin with a count badge; hover it for the list, click it to zoom in. Nothing moves.

**Priority zoom-out (off by default, `General / PriorityZoomOut`).** Far out, pickables then landmarks then ore drop first; bosses, dungeons, camps, portals, traders and home stay.

**From 1.8.9:**
- Right-click deletes the pin that is lit up, **Ctrl+Z** brings back what you deleted, optional confirm (`ConfirmDelete`), and pointing at a pin says Auto pin / Your pin / Shared pin.
- `ShowPinLabels` off hides only this mod's names. Pin names no longer flash when zooming out.
- Berry bushes in their berry's colour; tamed hatchlings tracked; the giant sword and helmet icon; icons come back after a reinstall.

Needs checking before this ships: the numbers above; the Epic Loot and multiplayer parts on a real server; README screenshots (picker, Hugin menu pages, badges); Thunderstore / Nexus / Hexium / Discord posts and the replies in docs/replies-1.8.9.md rewritten for 2.0.
