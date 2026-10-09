# Changelog

## 1.8.3

- **Bears get a pin.** Bears roam rather than living somewhere fixed, so there is no spot to
  pin ahead of time. Now a bear is pinned once you come within DiscoveryRadius of one, the pin
  follows it while it is nearby, and stays where you last saw it once it is out of range - so
  you can find it again for the trophy. Killed or gone when you get back, the pin goes too.
  Switch: Tracking / Wild. Icon under Wild Icons. Requested on Nexus.

## 1.8.2

- **Your own short labels.** Write `Pickable:Raspberry=R{qty}` or `Dungeon:Sunken Crypt=SC` in
  `carturpins_labels.txt` and the map draws R5 and SC instead of the full names. `{qty}` is how
  many pins are merged into the marker, `{name}` the normal name. Display only: the saved name,
  search and hand-renamed pins are untouched, and pointing at a pin shows its full name. The file
  is written for you, commented out, the first time you load a world. Requested by a player.
- Dungeon, camp and landmark names now translate. They were saved as plain English, so no
  translation file could reach them; pins already on your map are re-worded the next time the
  game language changes.
- Pointing at a pin shows its name again. The cursor was measured from the wrong corner of the
  map, so the reveal never fired.
- Renamed spawner pins keep their name on maps from older versions. The 1.7.1 fix missed
  pins recorded by older versions of the mod, so those still gained "Spawner" on every load.
- Intact Dvergr guard towers get a pin under Landmarks (the Landmark category is off by
  default, so switch it on to see them). Only the ruined ones were pinned before, through the chest inside them.

## 1.8.1

- Changing a category's icon moves every pin of that kind, not just new ones.
- Looted buried chests switch to the open-chest icon, and unlooted ones keep the dig-here X.
- Pins you placed by hand are never taken over by auto-pinning.
- Migrations and styles are kept per world.
- The hide list works whatever language the game is in.
- Hidden pins no longer catch clicks on the map.
- Only your own boats and carts are pinned.
- A tracked pin is only redrawn when it actually moves.
- Pins are saved atomically, so a crash mid-save can no longer corrupt the file.
- Number settings have sensible ranges in the config manager.

## 1.8.0

- **F8 pauses auto-pinning.** Press it in game to stop and resume placing new pins (shown top
  left) without touching any category switch. Pins already on the map stay, and looted chests and
  mined ore are still tidied up. The key is `General / AutoPinningKey`; the switch itself is
  `General / AutoPinning`.
- **Buried chests are their own kind,** with their own dig-here X icon. Switch them off under
  `Chest Site Kinds / Buried Chest`, or right-click their icon in the grid to hide just those.
  Buried chests already on your map move to the new icon the next time you walk past.
- **Turning off `CustomIcons / MapPicker` hides the icon grid straight away**, and the search bar
  with it. It used to need a restart.
- **The icon grid fits its window at the source.** The grid's scroll area was 100 pixels wider
  than the window, which is what laid out 7 columns where 5 fit. 1.7.1 worked around it; this
  removes the cause.

## 1.7.1

- **Wild beehives get pinned again.** The mod recognised hives by a part only player-built hives
  carry, and built hives are skipped on purpose, so the wild ones in abandoned houses never pinned.
- **A pin group you hid stays hidden.** The game switches a hidden pin type back on whenever a pin
  of that type is added. Right for pins you place by hand, wrong for the ones this mod places - a
  newly found mushroom re-showed every mushroom pin. The mod now puts your filter back.
- **Every icon in the picker is reachable.** On some screens (2560x1440) the grid laid out 7
  columns in a 5-column window and 44 icons were cut off. It is now a fixed 5 columns.
- **ApplyPreset set in the config file works.** A preset set while the game was closed was read
  but never applied. It is now applied at load. Presets change what gets pinned from then on;
  pins already on the map stay.
- **A looted chest fades straight away**, instead of waiting for the map to move.
- **Renamed pins keep their name.** Spawner pins were relabelled at the start of every session
  without checking for a rename, so " Spawner" got added to pins you had named - portals
  included. Pins it already renamed need renaming once more.

## 1.7.0

- **Turning a group off now stops pins that were already queued.** A pickable is not pinned
  when it loads - it waits in a queue until you walk within discovery range, which can be
  minutes. The group switch was only read when it joined the queue, so turning Crops off left
  everything already queued to pin anyway. Five dandelions went onto the map after the switch
  was set to false, which is how this was found.
- **A copper deposit keeps its pin while you are still mining it.** A Black Forest copper
  deposit destroys itself the moment it breaks and leaves a fractured pile behind - and that
  pile is what you actually mine the ore out of. The mod only knew about the original, so nine
  seconds after the first hit it decided the deposit was gone and removed the pin. The debris
  now counts as the deposit still standing.
- **A hide list, with the names picked from your own map.** Type or tick the pins you do not
  want to see. They stay on the map and in your save - they are simply not drawn - so taking a
  name back out brings them all straight back. This is the only control here that reaches pins
  that are already placed: turning a category off has only ever stopped new ones.
  Matching is on what a pin *is*, not what it says, so "copper" and "copper ore" both find
  copper and none of it breaks when you change the game's language.
- **Changing an icon now carries the pins already on the map.** They used to keep the old
  artwork until you found out that `carturpins_reicon` exists and typed it into a console that
  needs a launch argument to open. Pins still on the previous icon follow the change silently;
  if any carry an icon you picked by hand, you are asked about those and only those.
- **Your own farm is not pinned.** Every sapling in the game grows into something this mod
  pins - carrot, turnip, onion, oat, kale, poteitr, magecap, jotun puffs, both vineberries,
  barley and flax - so a real farm with Crops turned on pinned your own base, once every five
  metres. Anything standing on cultivated ground is now left alone. Cultivation is something
  you do with a hoe, so no wild patch is ever mistaken for a field.
- **Wild barley and flax are High Value; the ones you planted are Crops.** A patch out in the
  Plains is worth walking back to. The field behind your house is not.
- **Picked things that never come back lose their pin.** A surtling core stand, a Dyrnwyn
  fragment, a coin pile, amber, pearls - once taken, the pin marked an empty patch of ground
  forever. Berries, mushrooms and crops keep their pins, because those regrow. The game itself
  says which is which and the mod now asks it.
- **Half the game's pickables were filed as "Unrecognised", and the gemstones as junk.** Groups
  were decided by matching letters in a prefab name, and at scale it was wrong more often than
  right: 44 of the 89 pickables fell through to a group that is off by default and means "could
  not classify this", and the junk rule matched the letters *stone*, so Bloodstone and all seven
  Mork Halla gemstones sat with the sticks and rocks. Groups now come from the same table that
  holds the names and icons. High Value went from 7 entries to 28.
- **The settings are in a findable order.** Forty-two sections sorted alphabetically meant the
  first thing you saw was Beehive, the master switches sat fourteenth, and Tracking's boat icons
  were thirty-five sections from Tracking itself. Sections are now numbered and grouped, and
  the mod binds them in that order so it reads correctly whether or not Configuration Manager
  is set to sort by name. **Every existing setting is carried over** - nothing is reset.

## 1.6.0

- **Boats, carts and tames get pins that follow them.** A pin that moves rather than marks a
  spot: your boats, your carts, and any tame you have named or can ride. When something is too
  far away for the game to have it loaded its pin stays where you last saw it, so the map still
  says where you left the boat. Raft, Karve and Longship each get their own icon and every tame
  is drawn as itself, all of them settings under Boat Icons and Tame Icons.
- **Right-clicking one of this mod's icons now hides that kind of pin.** Every cell in the icon
  grid is a copy of one vanilla pin button, and the copy kept vanilla's own right-click wiring -
  so right-clicking any custom icon hid one fixed vanilla pin type instead, and the new icons
  could not be filtered at all.
- **Mushrooms pin when you turn mushrooms on.** The BerriesAndMushrooms switch says it covers
  both and never did: mushrooms fell through to the Unrecognised switch, which is off by default
  and means "the mod could not classify this". Reported as "Common Mushroom No Auto Pin".
- **The five pickable switches are on the plain settings page.** They were all marked advanced,
  so the one switch that turns mushroom pins on was invisible unless you had already found the
  Advanced checkbox.
- **Pin names can be translated.** The names this mod writes itself - Sunken Crypt, Frost Cave,
  Copper - are now translatable, and a template file appears beside the config to translate from.
  Anything read off the game's own components was always in your language already.
- **A cartography table no longer has to publish this mod's pins.** New PinSharing setting:
  share everything as before, share only the pins you placed by hand, or share no pins at all.
  Reading other people's pins is unaffected, and your own map is never changed.

## 1.5.2

- **A pin you placed by hand is no longer at risk.** The repair pass that moves a record back
  onto its pin matched on saved-flag and distance alone, with no check of the pin's type - every
  other matcher in the mod has one. A drifted record could therefore adopt your own pin, and the
  mined-out-ore sweep would then remove it. The type is checked now.
- **Traders and boss altars stop churning.** The mod removed vanilla's dictionary entry as well as
  its pin, so vanilla re-added the marker every five seconds for the rest of the session, each time
  forcing a full map rebuild and writing a log line.
- **That same check stops running every frame.** The game's five-second throttle sits inside the
  method, so a postfix on it fired on every frame regardless.
- **A new world no longer opens with the search filter still applied.** The box was cleared; the
  filter behind it was not, so pins stayed dimmed under an apparently empty box.
## 1.5.1

- **Mining one deposit no longer takes a neighbouring deposit's pin with it.** Reported on
  Nexus as the ore marker disappearing "as soon as even a single piece is mined", and it was
  real. The sweep that forgets a mined-out deposit worked in the Ore category's dedupe radius,
  15 metres, but dedupe itself only ever merges a pin with its own kind - so a copper deposit
  and a tin deposit ten metres apart are two separate pins sitting inside each other's radius.
  The sweep could not tell them apart: it took the first ore record in range, which on a mined
  copper node was sometimes the untouched tin one. The same blindness ran the other way, where
  a live tin node vouched for mined-out copper and kept a dead pin on the map forever.
  Registered nodes now carry which ore they are, and both halves of the sweep ask about one
  ore rather than about ore in general. Records written before subtypes were recorded still
  behave as they did.

## 1.5.0

- **The readability settings 1.4.0's page described now actually exist.** The listing
  documented `ShowPinLabels`, `MergeRepeatedPins`, `ZoomedOutPinScale` and
  `HidePinsBeyond`; the DLL had none of them, and the zoom cutoff was named
  `HideLabelsBeyondZoom` on the page and did not exist either. All six are real
  settings now - the four above plus `HideLabelsFromZoom` and `ShrinkPinsFromZoom` -
  and the table in the README matches what the config file writes.

- **Crowding is measured in world metres, over every pin.** The first version measured
  it between icons where they had landed on screen, which seemed obviously right and
  was not: a cell boundary is fixed to the screen, so panning one pixel slid every pin
  towards a different cell, and pins lose their icons the moment they scroll off, so
  the set being measured changed every time the view moved. No two looks at the same
  map agreed. A patch of ground does not move when you drag the map, so the answers
  hold still now.

- **A death marker goes when its grave is emptied.** Vanilla adds one in
  `Player.OnDeath` and never removes it - the only `RemovePin` for a death pin is for
  `m_deathPin`, a different pin that is switched off by a `const false` - so a long
  save collects markers for graves that are long gone. Hooked on
  `TombStone.GiveBoost`, which `UpdateDespawn` calls only once the grave is both
  unused and empty, immediately before destroying it. Nearest pin only, and only
  within the radius the grave can occupy, so several deaths on one spot stay several
  markers.

- **Player-built beehives and chests are not pinned any more.** A piece you place
  reached the spawn hook with a blank creator: `Player.PlacePiece` instantiates the
  prefab - which runs `ZNetView.Awake`, and this mod's hook with it - and only calls
  `Piece.SetCreator` afterwards. So a hive you had just built looked wild. The creator
  is read again when the pin is actually placed, a tick later, by which time the ZDO
  carries it.

- **Pin names follow a language change.** Each record keeps the label before Localize
  ran on it, so switching the game's language re-words this mod's pins into the new
  one. A pin you renamed by hand is left alone: the record also keeps the exact text
  the mod last wrote, and a pin that no longer reads that way was renamed by you.

- **Custom icon types fall back to a vanilla glyph instead of a white box.** Reserving
  the custom type range leaves those types valid but unknown to `Minimap.GetSprite`,
  which returns a null Sprite, and a Unity Image with a null sprite draws a solid white
  square. Every custom type without a sprite now points at a vanilla one, so a player
  with custom icons switched off sees what they saw before, and the pin keeps the type
  that says which icon it wants back.

- **New command `carturpins_dedupe`.** Removes a leftover second copy of one of this
  mod's pins that no record points at, which nothing else can clean up. Counts by
  default; pass `yes` to actually remove them.

## 1.4.0

- **The map stays readable when it fills up.** An ore field is a dozen separate deposits
  and so was a dozen identical pins, which at map scale is one white blob you can neither
  count nor click. Pins with the same name standing on the same patch of ground now draw
  as one. Zoom in and they separate again - nothing is deleted, and every pin is still
  saved and still searchable.

- **Pins shrink as you zoom out, and names step aside.** Full size until the map is 15%
  zoomed out, then down to 60% at the whole-world view; names stop being drawn past 20%.
  Both are settings. The game has a setting of its own for hiding names and it has never
  worked - it is set to a zoom further out than the map can reach - so this is the first
  time they go away at all.

- **No more flickering when you drag the map.** Crowding used to be measured between icons
  where they landed on screen, so panning a single pixel slid every pin towards a different
  cell and the whole map re-decided what to draw. It is measured in world metres now. Drag
  as much as you like: the only thing that changes what you see is the zoom.

- **Pins fade instead of popping.** Sizes glide and pins fade in and out rather than
  appearing and vanishing between frames.

- **Icons without names.** A switch to draw pins with no label under them, for people who
  want the map to read as icons. The names are still there and still searchable - they are
  simply not drawn, so turning it back on brings all of them back.

- **Hide pins beyond a distance.** Off by default. Set it to a few thousand metres to keep
  the map to the part of the world you are actually in.

- **Emptied chests fade.** A looted chest pin now draws at half opacity, so a chest you
  have been through reads as dealt with without losing its place on the map.

- **Death pins clear themselves.** The game drops a marker where you died and never removes
  it, so a long save collects markers for graves that are long gone. Loot the grave and its
  marker goes with it.

- **Pin names follow the language you play in.** They always came out in your language when
  they were placed, but the text was then written into the save, so switching language left
  every existing pin in the old one. They are re-worded when you change it. A pin you have
  renamed yourself is never touched.

- **Fixed: a second character saw an empty map and nothing was ever pinned again.** The
  record of "we already pinned this" was one file for every world and every character. A
  second character walking into a world the first had explored was told everything in it
  was already pinned, on a map that was empty. Records are kept per world per character
  now, and the old shared file is handed to the first character that loads rather than
  being thrown away.

- **Fixed: turning custom icons off could destroy which icon a pin had.** With them off, or
  if the icon sheet failed to load, the game rewrote every custom-icon pin to a plain
  marker - and then saved it that way, so turning them back on could not recover it. Off
  now means "do not draw them", never "destroy them".

- **Fixed: two icons on your bed, and on traders.** The game draws its own unnamed marker
  on both, underneath ours. It is removed where ours stands, so one place reads as one pin
  instead of two overlapping ones that both shrank for being crowded.

- **A failed patch no longer takes the rest of your mods down.** If a game update moves
  something this mod hooks, it now says so in the log and steps aside instead of stopping
  the whole mod loader.

## 1.3.6

- **No more duplicate pins.** The mod kept its "already pinned here" list in a file beside
  the config, while the pins themselves live in your character save. Reinstalling the mod,
  or anything else that clears the config folder, emptied that list while every pin it
  described was still on your map - so the next time you walked past a deposit it pinned it
  a second time. It now asks the map itself before placing, and takes any pin it finds back
  into the list, so a lost list repairs itself instead of doubling everything up.

## 1.3.5

- **The icon picker can be moved.** There is a small gold square in its top left corner,
  next to the "shift click icon to change" line. Drag it and the panel goes wherever you
  put it, and stays there next time you play. Only that square drags, so clicking an icon
  still picks the icon. It cannot be lost off the edge of the screen: let go past an edge
  and it comes back far enough to grab again, and the same happens if you later play at a
  smaller resolution than the one you positioned it at.

## 1.3.4

- Page only - the plugin is unchanged from 1.3.3, so there is nothing new to see in
  game. The description now covers the things 1.3.3 added but never wrote down:
  resetting a world's pins, why the settings list got shorter, and that icon choices
  from 1.2.2 come back by themselves. Also adds a link to Cartur's Flooring.

## 1.3.3

- **Your icon settings from 1.2.2 come back.** 1.3.0 replaced the icon sheet, and the
  names in your config file - MineFace, CobwebArch, Shrine, Bee - stopped existing, so
  the game logged a warning and quietly used the default instead. If you had picked an
  icon by hand, that choice was lost. It is read back now: most become the same icon
  redrawn, and the handful the new sheet has no answer for keep their original artwork,
  which is still included. Nothing to do; it happens on the next launch.
- **Start a world's pins over.** `General / ResetPins` removes every pin this mod placed
  and forgets them, so each one is pinned again as you rediscover it. Pins you placed by
  hand are untouched, and so are any colours or sizes you set.
- **The settings screen is shorter.** It listed about 360 rows; roughly 290 of those were
  a switch or an icon for one single kind of thing. Those now sit behind the Advanced tick
  box, leaving the seventeen categories and their icons in view. Nothing has been removed
  or renamed, and no setting you have changed is affected.

## 1.3.2

- **Fixes a broken pin UI on some installs.** The icon picker and the pin editor build
  their own text labels, and those labels were created without naming a font, on the
  understanding that the game would supply a default one. On some installs it does not -
  Valheim keeps its fonts in an asset bundle rather than where the text system looks for
  them - and a label with no font throws every frame, which takes the rest of that panel
  down with it. Every label this mod makes now takes its font from the map's own pin-name
  box, which is always there. Nothing to change; if your map looked fine before, it still
  will.

## 1.3.1

- **Renaming a pin now shows the new name straight away.** The map writes a label's text
  once, when it builds the label, and never reads the name again - so a rename from the
  pin editor left the old name on screen until the pin scrolled off and back, and a pin
  that had no name yet never got one at all. The editor now rebuilds the label the way
  vanilla's own naming does. `$`, `<` and `>` are stripped from names, as vanilla does.
- **Enter in the search box or the editor's name field no longer reaches vanilla's
  pin-naming handler.** Both boxes are clones of the map's own name field, which carried
  its handler along; pressing Enter in one could close a pin you were naming and leave it
  blank.
- **Bosses revealed by a vegvisir or a guardian stone get the mod's own altar pin** -
  Eikthyr, The Elder, Bonemass, Moder and the rest - instead of vanilla's generic boss
  marker. Recorded like any other pin, so walking up to the altar later does not add a
  second one. Off when the Boss Altar category or that boss's switch is off; vanilla's
  marker is left alone then.

## 1.3.0

**A new icon set, and the old one kept.** 153 icons, redrawn. Pins this mod placed
move onto the new art by themselves the first time you load. Pins you placed by hand
keep the exact picture you gave them — 1.2.2's sheet is still loaded underneath, and
its icons are still in the picker, listed after the new ones. Nothing picks them for
you.

**Camps pin properly.** A Fuling village used to be invisible: the mod looked for a
dungeon generator, and villages stopped having one. Now Fuling villages, abandoned
villages and farms, Charred fortresses and the Deep North village are all pinned by
name. Greydwarf camps are off by default — they are the most common location in the
Black Forest, 450 attempts a world against 405 for Fuling villages.

**Named minibosses get their own pins.** Lord Reto, Brenna, Geirrhafa, Zil & Thungr
and the Fallen Valkyrie. They used to pin as generic Charred, Skeleton and Fuling
spawners, if at all. On by default, and they do not need the Spawner category on —
one you fight once is not the same as a greydwarf nest.

**Your bed is marked as home.** Set your spawn and the bed gets a house pin that
stays there. Vanilla moves a single marker to whichever bed you slept in last, so
the outpost you used a week ago left nothing behind. Vanilla's marker gets the same
house icon so there is one house, not two markers stacked.

**Mined-out deposits stop lying to you.** Ore never respawns, so a pin on a worked-out
node is a walk across the map to an empty hole. Those pins are removed once the
deposit is gone — only pins this mod placed, and only while the game has that area
loaded, so a node you simply walked away from is never mistaken for a mined one.

**Dungeons tick off when you have emptied them.** Every chest inside empty and every
mud pile mined, and the pin greys out the way one you ticked by hand does. The game
tracks nothing of the sort itself: dungeon spawners respawn on a timer, so what you
took is the only lasting record of having been through. Unticks if a chest refills.
Per kind, so Sunken Crypts can tick and Frost Caves need not.

**The Deep North reads properly.** Winding Tunnels, Gates of Morkhalla, the Aesir
Passage and Kall Fimbulbringer's altar, the Deep North village, the Ancient Altar.
Mork Halla and Bear Cave were pinning as a generic staircase.

**Every pin can be coloured, resized and faded.** Shift-click a pin: eight colours,
a size slider and an opacity slider, alongside the icon grid and the name. Nothing
applies until you confirm, and Cancel throws it away. Works on pins you placed by
hand as well as ones the mod placed.

**Ore pins are coloured by what the ore is.** Copper warm brown, tin pale, iron dull
grey, flametal orange, sulphur yellow. Obsidian, tar and black marble are lifted
towards grey rather than drawn true - the map is dark, and a black pin on it is a
hole rather than a marker. A colour you set yourself always wins.

**Crowded pins shrink, and piled-up names get out of each other's way.** Pins sitting
on top of each other scale down, never below half. Where two labels overlap the rarer
name is kept, because CHEST appears forty times and says less than CRYPT - and a pin
under your cursor always shows its name, so nothing is unreadable for long. Icons are
never moved: a pin stays where the thing is.

**A switch for every kind.** Not just per category: every dungeon, camp, boss,
trader, spawner, miniboss, landmark, plant and ore type has its own on/off switch,
generated from the same tables that do the matching — so the menu can never fall
behind what the mod recognises.

**Maypoles are pinned**, and nothing you built ever is.

**Shift-click a pin to rename it or change its icon.** It always worked; nothing said
so. The map now says so, above the icon grid.

**The icon picker is rebuilt.** It sits beside vanilla's own pin buttons instead of
across the map from them, every cell has a border that turns gold when selected and
lifts under the cursor, and the grid scrolls more than twice as fast. Old pin names
that still read as "$piece_chestwood" are resolved on load.

Fixes: the seven guardian stones at the spawn temple put seven pins on top of each
other and are now left to vanilla's own marker · a vegvisir standing inside a
location no longer outranks the location, so a Morgen Hole is a dungeon rather than a
runestone · pin labels are translated instead of reading "$enemy_eikthyr" ·
spawner pins from older versions repair their own icons when you walk past · the
icon grid scrolls half again as fast · carturpins_reicon resets every pin the mod
placed to its category's current icon, for anyone who changes their icon settings.

## 1.2.2

- Moved the links to my other mods to the top of the page.

## 1.2.1

- Added links to my other mods.

## 1.2.0

**Names.** Spawners said "Skeleton" where they meant "Skeleton Spawner", and the
eleven skeleton variants each said something different - "Skeleton Night Noarcher",
"Skeleton Meadows Night Noarcher". Every spawner now asks what it actually spawns and
says so: "Boar Spawner", "Greydwarf Elite Spawner", and the minibosses by name -
"Brenna Spawner", "Lord Reto Spawner". 99 of 103 spawner labels changed.

The boss stones read "BossStone_Eikthyr" and now read "Eikthyr Guardian stone".
Wisps, the Bog Witch, boss altars and random-loot pickables were showing internal
names or the literal word "None"; all of them now say what they are.

**Ore.** 33 things counted as ore and 16 of them were not - a rusty crypt gate, a
cauldron, a barrel, a weapon rack, a pickaxe hung on a wall, the giants' swords and
helmets. All of them drop scrap when broken, which is not the same as being a
deposit. Now 17, and every one is something you mine.

**Lore runestones**, the eleven story stones, can be pinned. Off by default.

**Settings.** Roughly 100 entries down to about 40. Icons are a dropdown showing the
icons instead of a slider you dragged from -1 to 82. Per-category spacing and the
timing knobs moved to Advanced. Nothing was lost, it is just not all on screen.

Icon settings are stored by name now (`Icon = Pickaxe`), so your previous icon
choices reset to the defaults once.

**Fixes.** Hildir crypts were labelled "Crypt"; every charred ruin claimed to be the
Charred Fortress. Eleven dungeon and camp names that never matched anything were
removed.

## 1.1.1

Rebuilt for Valheim 1.0.7, and the diagnostics no longer ship.

**Console commands work again.** Valheim 1.0.7 added a parameter to
`Terminal.ConsoleCommand`'s constructor. C# resolves optional arguments at compile
time and writes the exact parameter count into the DLL, so the 1.1.0 build referenced
a constructor that no longer exists and threw `MissingMethodException` while
registering commands. That aborted `Awake` before the "loaded" line and took
`carturpins_clear` and `carturpins_count` with it. Pin placement was unaffected,
since the Harmony patches are applied earlier. The source never named those
parameters, so a recompile is the whole fix.

**The log is quiet now.** The probe tooling was being built into release. On a normal
session this mod produced 3,569 of 4,857 log lines - 73% of the file - almost all of
it one `Drain:` counter printed on every queue tick, plus an `AddInstance diag:`
report every 15 seconds and a one-shot node dump that was **on by default**. None of
it was useful to a player, and volume is exactly what makes a log useless to read
when something has gone wrong.

Release builds now contain none of it: no `carturpins_probe`, `carturpins_locations`
or `carturpins_catalog` commands, no `Diagnostics/AutoProbeOnSpawn` option, and no
per-tick logging. What remains is the load line, the catalog summary, pin events, and
warnings.

`carturpins_clear` and `carturpins_count` are unaffected - those are features, not
diagnostics.

## 1.1.0

First public release.

- Automatic labelled pins for ore, dungeons, caves, camps, beehives, runestones,
  chests, spawners, leviathans, traders, wisp fountains and high-value pickables.
- Emptied chests switch to a looted icon and switch back if refilled.
- 83 selectable icons, one per category, on top of the vanilla pin types.
- `carturpins_clear` and `carturpins_count` console commands.
