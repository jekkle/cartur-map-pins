# Map Pins 1.8.9 — drafts (nothing posted yet)

## Discord #announcements
**Cartur's Map Pins 1.8.9** is out on Thunderstore, Nexus and Hexium.
- Right-click deletes the pin that's lit up under your cursor, and **Ctrl+Z** on the big map brings back pins you deleted by mistake. Optional confirm-before-delete in the config.
- Pointing at a pin says whether it's an **Auto pin**, **Your pin** or a **Shared pin**.
- Pin names no longer flash when you zoom out, and turning names off only hides Map Pins' own names.
- Berry bushes are coloured by berry, tamed hatchlings are tracked, giant swords/helmets get their own icon, and icons come back after a reinstall.
Thanks to everyone who reported these!

## Discord #changelog
(the full 1.8.9 section of CHANGELOG.md, pasted as is)

## Discord — bkpenther (reply in the thread)
<@410965663444893697> Fixed in **Map Pins 1.8.9**. With `ShowPinLabels` off, Map Pins' own names are hidden and your own pins and the game's keep theirs. The names popping up in waves when you zoom out is fixed too.

## Discord — totya (reply in the thread)
<@totya> It's in **Map Pins 1.8.9**: right-click deletes exactly the pin that's lit up, pointing at a pin says **Auto pin** / **Your pin** / **Shared pin**, **Ctrl+Z** on the large map brings back what you just deleted (no console, no rescan), and `General / ConfirmDelete` can make it ask for a second click (for all pins or only auto pins). Thanks again for the write-up!

## Discord — thor_01 (reply in the Giant Armor thread)
<@thor_01> **Map Pins 1.8.9** adds the icon setting for Giant Armor, and it now draws the giant sword icon instead of the plain ore one.

## Nexus — IllGib81
Hi! Giant swords and helmets are pinned, but the switch is off by default: turn on `Ore Types / Giant Armor` in the config. Giant skulls are pinned as "Black Marble" (that's what they're mined for), and Mistlands ruins are under Landmarks, which are also off by default. Pins only appear once you've been within 60 m of something (`DiscoveryRadius`), so things you've only seen from far away won't be pinned yet. Portals are on the list for a future update.

## Nexus — Gomisan
Thanks, that's great to hear! Thank you for adding the compatibility.

## GitHub #1 (Yumileashen) — reply and close
Fixed in 1.8.9. Pins that turned into the plain circle after a reinstall get their icons back the next time you load the world, no rename needed.

## GitHub #4 (DrGrue-US) — reply and close
Berry pins are coloured by berry in 1.8.9 (raspberry red, blueberry blue, cloudberry yellow, lingonberry pink, vineberry purple), setting `Pickables / TintBerriesByType`. To drop the text too, add a line like `Pickable:Raspberry=` to `BepInEx/config/carturpins_labels.txt`. Red mushrooms do pin with `BerriesAndMushrooms` on: I tested it in 1.8.9. If yours still don't, please tell me your version and I'll look again.

## GitHub #8 (BlackHearthx) — reply, leave open
Dvergr towers pin since 1.8.2. Which tree do you mean? If you can tell me its name or post a screenshot, I'll add it.

## GitHub #9 (datacain) — reply and close
Fixed in 1.8.9: tamed Asksvin hatchlings are pinned before they grow up.
