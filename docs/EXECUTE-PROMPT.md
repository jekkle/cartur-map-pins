# Execute the Map Pins improvement plan

Paste the block below into a fresh session started in D:\Ai\Modding.

```
Ship Cartur's Map Pins 1.8.9, then build the improvement plan release by release, starting with Release 1 (smooth zoom) and Release 2 (Hugin setup menu).

<context>
- Repo: D:\Ai\Modding\Valheim\cartur-map-pins (C#, BepInEx 5, HarmonyX, net472). Rules: D:\Ai\Modding\CLAUDE.md (read the game's real code before patching, root cause, least code, never guess).
- Plan (the spec): docs/improvement-plan-2026-10-27.md. Research it cites: research/pin-clustering/ux_reference.md and valheim_feasibility.md. Read all three before starting a release.
- State: memory note valheim-map-pins-189-pending. 1.8.9 is built in src/, uncommitted, and the pilot passed 9/9 (run.ps1 -Pins -Resolution 1920x1080).
- Owner decisions so far: Giant Armor stays OFF by default. The hover tag shot (tag right of the cursor) was sent; if he hasn't said OK, ask once before shipping.
- Testing: the test pilot D:\Ai\Modding\Valheim\cartur-testpilot. Read TESTING.md and COMMANDS.md, never Pilot.Live.cs. Drive the live game only through tools\live-scripts\t.py with LIVE_OWNER set (the lock). Batch commands. Read 640 px images, not full PNGs. Use the carturtest character only, never a real one.
- Other sessions: modding-00 owns cartur-shaders and often the Dev game. Ask it (SendMessage) before launching Valheim on Dev; never touch its files.
</context>

<instructions>
1. Ship 1.8.9:
   - Commit, then publish to Thunderstore, Nexus, Hexium and Discord (#announcements + #changelog) per memory notes thunderstore-global-rules, nexus-publishing, hexium-publishing, feedback-release-includes-discord.
   - Resolve the built DLL against the installed game with Mono.Cecil first (valheim-mod-release-check).
   - Then draft replies to bkpenther, IllGib81, thor_01, totya, Gomisan, and GitHub #1/#4/#8/#9.
   - Show the owner every reply's exact text before posting anything.
2. Release 1 (1.9.0) smooth zoom, as the plan specifies.
   - Step 0: film a dense-map zoom sweep and list what flickers or jumps.
   - Then build: hysteresis at every threshold, glide instead of snap, label fade, a continuous scale curve.
   - Re-film. The owner sees the before/after clips before shipping.
3. Release 2 (1.9.1) Hugin first-install setup menu, as the plan specifies.
   - One category per page, simple player names, Select all / Deselect all / Undo per page.
   - One shared raven with Cartur's UI Welcome.cs.
   - Respects tutorials-off; no Hugin on a dedicated server.
   - The owner sees a screenshot of every page before shipping. New art goes to him as options.
4. Continue with Releases 3-9 in plan order, one release at a time. Each ships alone, behind a setting, with its pilot checks passing and the owner's screenshot OK.
</instructions>

<rules>
- Never bump a version for an untested build.
- Confirm before publishing or posting anything.
- No decisions or licence notes in shipped files.
- Never edit approved art; offer options.
- Batch in-game checks into one pilot launch per release.
- Keep cartur-ui-hud out of this work: its fixes are held because modding-9b's Path of Valheim code is in that tree.
</rules>

Before finishing each release, verify against the plan's pilot checks and get the owner's OK on the screenshot or clip.
```
