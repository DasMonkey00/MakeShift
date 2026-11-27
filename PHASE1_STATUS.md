# Phase 1 status (2026-11-24)

## Done and verified
- Project base: `D:\DEV\UnityTemplates\HandGrip-Standalone` copied in (Unity 6000.6.10f1, Auto Hand, OpenXR,
  XR Hands, Meta OpenXR). `Assets/Makeshift` was left as written except where noted below.
- Compiles clean (batch import, zero errors; only warnings are inside Auto Hand).
- `AutoHandBridge` matches this Auto Hand: `Grabbable.GetHeldBy()` -> `List<Hand>`; events `OnGrabEvent`,
  `OnReleaseEvent`, `OnHighlightEvent` exist.
- **Greybox drill scene** `Assets/Scenes/Greybox.unity` (menu `Makeshift > Build Greybox Drill Scene`; rebuilds
  from `Assets/Makeshift/Editor/GreyboxSceneBuilder.cs`): template VR rig + rock, drill frame with motor/handle
  sockets and power port, power drill / toaster / stick parts, car battery, cable plug, poke switch, club.
  Parts table is at x~1.6 (right of spawn); the drill station is behind it at z~1.0.
- **Automated play-mode smoke test** (all 9 checks PASS, no headset needed): parts insert, wrong part lowers
  reliability, plug connects, machine runs, fails at least once, drill digs all 6 ore chunks, backfire jams,
  bonking recovers. Re-run:
  `Unity.exe -batchmode -projectPath <proj> -executeMethod Makeshift.EditorTools.GreyboxSceneBuilder.RunSmokeBatch -makeshiftSmoke -logFile smoke.log`
  (exit code 0 = pass; look for `[SMOKE]` lines).
- Fixed `Machine`: switching on before the last part went in never started it; it now starts once assembled.
- Added `MachineSwitch` (poke button), `Scripts/Platform` (`IPlatformServices`, `OfflinePlatformServices`,
  `Platforms.Current`; Meta/Steam branches behind `MAKESHIFT_META` / `MAKESHIFT_STEAM`).

## Written but NOT tested
- `Scripts/Network/NetworkGrabbable.cs`: Auto Hand grab/highlight -> `RequestStateAuthority`, optional release at
  rest. Wrapped in `#if FUSION_WEAVER`, so it is inert until Fusion 2 is installed. Written without Fusion in the
  project, so expect small API fixes on first compile.

## Not done (needs you or a headset)
- **Hands-on VR feel** of the greybox: grabbing parts, placing them in sockets with real hands, plug range.
  The smoke test drives the logic directly, not through Auto Hand hands.
- **Photon Fusion 2 + XRShared install**: needs your Photon account and App ID (website download). Steps:
  1. photonengine.com dashboard > create a Fusion app > copy App ID (Fusion).
  2. Import Fusion 2 SDK `.unitypackage`; paste App ID in Fusion Hub / `PhotonAppSettings`.
  3. Import Photon's VR Shared sample + XRShared addon.
  4. Photon Voice later (proximity chat).
- **Rig split** (hardware rig with Auto Hand + visual-only network rig), two-headset test, Quest 3 profiling,
  cross-play check: all need Fusion and devices.
- I did not diff the template's Auto Hand against stock, so "what changed vs stock" is unknown.
- Base-project choice: template `HandGrip-Standalone` was assumed. `UnityHandTest` and `Autohandy` also exist.
- Sprints 2-4 (climbing, glide, creatures, fire/shelter, co-op) not started. They depend on hands-on playtests.

## Known rough edges (design-level, for tuning when you playtest)
- A released plug falls and disconnects once it drifts > 0.25 m from the port. Probably wants a plug-in
  socket with a magnet snap.
- Drill/rock/frame use primitives; no cable visual, no sound.

## Greybox review (follow-up)
- **Bug found and fixed:** grabbing a part out of a socket never told the socket, so the machine kept counting a
  removed part. New `PartSocket.Detach()` + `Integration/AutoHandPartHook` (added to every part by
  `AutoHandBridge` at start; add it by hand to parts spawned later).
- **Wrong assumption fixed:** Auto Hand's `OnGrabEvent` etc. are plain C# delegates (`+=` / `-=`), not
  UnityEvents. `NetworkGrabbable` had the same mistake and is corrected.
- Smoke test now 14 checks, all PASS: adds the trigger snap path (with the bridge), layer collision, hook release.
- Test-only gotcha: teleporting an interpolated Rigidbody needs `rb.position` as well as `transform.position`.
- Still unverified with real hands: grab-out ordering (Auto Hand reparents to the hand *before* our hook runs;
  `Detach` deliberately leaves parent/kinematic alone), plug range, switch poke strength, socket sizes.

## Sprint 2 (partial, headset-free parts) — added while you were away
- `Scripts/Combat`: `Damageable` (single `Hit()` entry point), `ImpactDamage` (damage from collision impulse,
  Sharp/Heavy bonuses; any swung or thrown object works), `Harvestable` (hit a bush/crate for loot; preferred
  tool trait doubles it).
- `Scripts/Creatures`: `ScavengerCreature` + `Scavengable`: wanders, steals a loose item, runs to its den and
  stashes it; a hard hit drops the loot and stuns; can be knocked out. The bridge feeds it Auto Hand's held check
  so it never steals from a hand.
- Greybox scene gets a scavenger, den, shiny spoon and berry bush on the left of spawn; club, drill, toaster, stick
  and battery now do thrown/bonk damage.
- Smoke test is now 23 checks, all PASS. Tuning numbers (damage per impulse, stun time, sight range) are guesses.
- Not started (need hands/headset to judge): climbing, umbrella glide, the 6 relics, bonk-a-friend (needs netcode).
- Fusion install still blocked on your Photon account/App ID; I did not download anything.

## Playground (headless-testable building blocks)
Scene `Assets/Scenes/Playground.unity` (menu `Makeshift > Build Playground Scene`; builder in
`Assets/Makeshift/Editor/GreyboxSceneBuilder*.cs`). Greybox drill station at spawn plus four zones:
- **West (-8,0,0) survival camp:** campfire (fuel it with anything Burns, light it with anything burning,
  water douses it), logs, torch, raw meat on a table, water trough.
- **East (8,0,0) crafting bench:** flint, stone, sticks, vines. Vine (`StickyBinder`) dropped across two things
  lashes them with a joint, so flint+stick+vine = spear, no recipes. Dummy to test on.
- **North (0,0,10) combat range:** three knock-about dummies.
- **South (0,0,-10) traversal:** 3.9 m climbing wall (Auto Hand `Climbable` holds) up to a platform with an
  umbrella `Glider` (squeeze trigger to open; it acts on the player body while held).
- Debug HUD (food / warmth / day-night) floats at the bottom of the player's view; eat by bringing held food to
  your face (`Mouth`). Cooking is heat over time: raw -> cooked -> burnt. Night brings scavenger raiders to the
  camp; they leave at dawn. Killed raiders can be butchered for meat.
- New systems: `Survivor`, `Cookable`, `Edible`, `Mouth`, `DayNightCycle`, `NightRaids`, `Campfire`,
  `StickyBinder`, `Glider`, `HandCheck` (shared "is it in a hand" check).
- Run all tests: `... -executeMethod Makeshift.EditorTools.GreyboxSceneBuilder.RunPlaygroundSmokeBatch -makeshiftSmoke`
  (no -nographics; about 5 min). **50 checks, all PASS.**

Bugs the tests caught: UnityEvent fields are null on components added at runtime (all invokes now `?.`);
glider brake was weaker than gravity (now 40); drill test needed to re-seat parts after backfires.

Not verified (needs headset): climbing feel (hold grab settings may need tuning), glider with the real player
body (AutoHandPlayer), eating by mouth trigger, lashing by hand, HUD readability. Joint break force default
(600) held a hard yank at 80, so real lashings may be unbreakable; tune with playtests.

## Fusion 2 imported + netcode seam (2026-11-24, later session)

- **Fusion 2.0.13 is now in the project** (`Assets/Photon`). It was NOT actually imported when you said it was;
  the `.unitypackage` was only in Downloads, so I imported that file in batch mode. Project compiles with zero
  errors; `FUSION_WEAVER` and `FUSION2` defines are set (Standalone only so far; Unity adds them per platform
  when you switch the build target to Android, check the Quest build compiles).
- **Still yours**: Photon App ID in Fusion Hub (`PhotonAppSettings`), and the XRShared addon / VR Shared sample
  (separate download). None of the networking below has run against a real Photon session.
- `NetworkGrabbable` compiles against the real Fusion API. Not run online.
- **Authority seam** (`Core/NetAuthority.cs`): one static gate. Offline it allows everything, so nothing changed
  for the greybox. `Machine` (TurnOn/TurnOff/Douse/Bonk + Update), `Flammable` (Ignite/Extinguish + Update) and
  `Damageable.Hit` now check it; without authority they relay to the authority instead of changing state.
  Mirror methods (`Machine.ApplyRemoteState`, `Flammable.ApplyRemoteBurning`, `Damageable.ApplyRemoteHealth`)
  are the only way remote state lands. `Machine.Backfire()` called directly is not gated (only reached via Bonk).
- **Fusion layer** (`Scripts/Network`, all `#if FUSION_WEAVER`): `FusionAuthorityBridge` (installs the gate,
  objects without a spawned NetworkObject count as local), `NetworkMachineSync`, `NetworkFlammableSync`,
  `NetworkDamageableSync` (networked state + RPC to state authority), `NetworkRig` (visual-only head/hands,
  owner hides own copy), `HardwareRigLink` (marks the local Auto Hand rig; add to the AutoHandPlayer object),
  `MakeshiftNetworkLauncher` (Shared mode, or Single for offline). Compile-checked only.
- **Not done**: no network rig prefab yet (needs a NetworkObject prefab with NetworkRig + three visual children),
  nothing added to the Playground scene, no `NetworkObject` on any scene object. Do that in the editor next to the
  Fusion Hub setup, then test with two instances.
- New headless suite `AuthoritySmokeTest` (11 checks, fake non-authority provider). Full suite: 61/61 on the
  second run. The first run had one failure, "drill dug ore", which depends on random machine failures; it
  passed on rerun. Treat it as flaky, not a regression.

## Render pipeline and Looksmith (2026-11-24)

- **The project was on Built-in RP, not URP** (despite this brief). Migrated: URP 17.6.0 added, two assets in `Assets/Makeshift/Rendering` (`Makeshift_URP_Quest`: no HDR, no depth/opaque texture, 1 cascade, 25 m shadows; `Makeshift_URP_PC`), assigned per quality level by `Makeshift/Setup URP`. 186 Standard materials converted. Backup of the pre-migration project: `D:\DEV\MakeShift_backup_pre_urp`.
- Left alone: Auto Hand example highlight shaders (`Outlined/Silhouette Only`, `Oculus/Hands_Transparent`) are Built-in shaders and may render pink. The Meta Quest build profile may carry its own graphics overrides: check it.
- **Looksmith Lite imported.** Cost: the lighting shader is about URP Lit cost. The expensive parts are full-screen: edge lines (extra depth+normals pass), grain/vignette finish, and the Look's post stack (bloom, fringe, tonemap, grain).
- `RenderStyleSwitcher` (Playground, keys 1-5 or the pedestal at spawn): None / Stylized realism / Cel shading / Foggy, plus Lean/Full. Lean (default on Android) keeps lighting and haze only; Full is the Look as authored (default on PC). None unregisters Looksmith, so it adds no passes.
- Smoke: 78 checks pass (50 old + 28 new incl. render style checks). Not seen visually yet (batch has no screen): look at all four styles in the editor.
- Batch gotcha: build methods do not exit by themselves; pass `-quit`.
- Screenshots (`-makeshiftShots`, `Logs/shots`) confirm: Cel shading draws outlines in Full only; Foggy is hazier and greyer; Stylized realism is a subtle tint; Lean mode has no lines. Style board text layout fixed.
- Smoke is flaky: runs after the first all-pass (78/78) each lost one different physics check (weak lashing snap; stick-into-socket). Not yet investigated; likely frame-timing sensitive (rendering is now on). Re-run before trusting a single failure.

## Environment: ground, grass, sky, time of day (2026-11-24)
- Ground: Looksmith demo `Terrain_Grass.png` tiled on the floor. Grass: `GrassField` scatters Looksmith demo `Grass1` clumps (about 3,800, merged into 49 chunk meshes at start, no shadows). Sky: `LooksmithSky` with the Morning / Golden Hour / Cozy Night presets, blended by `TimeOfDaySky` from the world clock; clouds kept in the day sky; reflection cubemap off (not Quest-friendly).
- Keys: `,` `.` = -/+ one hour, 7 dawn, 8 noon, 9 dusk, 0 midnight, P pause the clock. 1-5 = render style.
- Assets are referenced in place from `Assets/PaleFernStudio/Looksmith/Samples/Demo`: copy them before deleting the Samples folder. Grass1.fbx was set to Read/Write.
- Fixed a flaky smoke check: the stick teleport now sets the Rigidbody position too. "dropped loot is a free object again" also failed once; if it recurs, look at the scavenger recover timing.
- Smoke: 78/78 pass.

## Looksmith removed (2026-11-27; was parked 2026-11-25)

**Status.** Looksmith has been removed from the project (2026-11-27). Everything uses URP/Lit. The sections below are the history
of why, and the original plan. The "Still depends on" list and removal checklist describe what had to change; check the current
code before relying on them.

**Problem.** The Android (Quest) build failed with 28 errors, all one shader error from `Looksmith/Looksmith Lit`:
`D3D11 Internal Compiler Error: Invalid Bytecode ... relative index ... uninitialized` (12 on GLES3, 16 on Vulkan), on every
variant with main-light shadows (`_MAIN_LIGHT_SHADOWS*`, with Quest multiview). PC builds compile it fine. Other build blockers
we hit first (OpenXR validation needing Meta Quest: Session, text-mesh MeshFilter errors) were unrelated and are fixed.
URP asset settings (shadows/cookies off, Android excluded from PC quality levels) did NOT stop those variants compiling.

**Workaround in place (builds work, confirmed).** All Makeshift materials (51), the Playground scene's embedded materials and
Looksmith's own sample materials now use URP/Lit. `RenderSetup.UseLooksmithLit = false` makes the scene builders pick URP Lit.
Backups: `D:\DEV\MakeShift_backup_pre_looksmith_park`, `D:\DEV\MakeShift_Materials_backup_looksmith`,
`D:\DEV\MakeShift_Looksmith_mats_backup`, `D:\DEV\MakeShift_Playground.unity.bak`.

**Why it must be removed or replaced.** The Lit shader was the main reason to have the asset (stylized lighting, cel look), and
that shader does not build for our primary target. It is a closed vendor Shader Graph, so we cannot fix it ourselves. The cel/edge
passes are also full-screen cost we cannot afford at 90 fps on Quest 3S (see the performance goal in CLAUDE.md).

**Still depends on Looksmith (compiles, but dead weight until replaced).**
- Runtime: `RenderStyleSwitcher` (style keys 1-5, pedestal), `TimeOfDaySky` (day/night sky), `RenderSmokeTest` (render style checks).
- Editor: `GreyboxSceneBuilder.Playground` (Look Volume, sky, grass mesh and ground texture from the Looksmith Samples folder),
  `RenderSetup`, `RestyleReport`, `GrassField`/`GrassStats` use `Samples/Demo/Grass1`.
- Scene: Look Volume, `LooksmithSky`, Looksmith grass/foliage meshes in `Playground.unity`.

**Plan (original, 2026-11-25).** Do NOT fix Looksmith and do NOT swap in another asset now (Phase 1 is about fun, not look). Revisit the look as a
spike around weeks 5-6, once we have a Quest 3S frame budget: a custom/own toon-ramp URP shader, a Quest-friendly toon asset, or
plain lighting. Removal checklist when we do: copy the used assets (Grass1 mesh, Terrain_Grass texture) into `Assets/Makeshift`;
replace the sky and `TimeOfDaySky` with a URP skybox + light rotation; delete `RenderStyleSwitcher`, `TimeOfDaySky`,
`RenderSmokeTest`, `RestyleReport`; clean the builders; delete `Assets/PaleFernStudio`; update the smoke checks.

**Rule.** After importing any rendering/shader asset, make an Android build before building on it.

## Playground debug pass (2026-11-25)
Changes (scene rebuilt, 63 smoke checks pass; none verified with real hands/headset yet):
- **Style board removed** (buttons, text, `RenderSmokeTest` suite). Looksmith stays installed; sky, grass and ground still use it. The scripts (`RenderStyleSwitcher` etc.) are still in the project, just not in the scene.
- **Shadows:** Quest URP asset now has main-light shadows ON (1 hard cascade, 1024 map, 25 m); PC asset unchanged. Set in `RenderSetup`. **Android build NOT re-run: do it before trusting this** (Looksmith Lit's shadow variants were what broke Android).
- **Models:** every test object is now a recognisable multi-part stand-in (`GreyboxSceneBuilder.Props.cs`): drumstick meat, logs, torch, flint, stone, stick, vine coil, dummy, climbing hold, umbrella, drill (chuck, spinning bit, MOTOR/HANDLE pads, plug hole, label), toaster, battery, plug, club, ore nugget, boulder, bush, scavenger. Swap real meshes in later; keep root, collider, components.
- **Notes:** pick-up papers on tables (welcome + drill, survival, crafting, combat, climbing). Text is on the top face only (Unity's text shader ignores depth).
- **Particles** (`ParticleFactory.cs`): fire (campfire + smoke, torch, burning items), sparks, water spray and splash. URP Particles/Unlit, no soft particles, no lights, <=40 particles per system.
- **Campfire:** now a real pit (floor + ring of 12 stones) so things stay in; a fuel pile shows while it has fuel and flames follow the fuel. Added **`SparkStriker`**: hit flint against something Heavy near a fuelled fire to light it (there was no way to start a fire before). Flint and stone are on the camp table.
- **Vine (`StickyBinder`)** rewritten: never sticks to hands/head, needs to be held, touching two things and wiggled ~1.2 s; then a joint plus visible wrap rings, and the vine is used up. `HandCheck` gained `IsPlayerBody` and `ForceRelease`.
- **Climbing:** holds were default Grabbables (object floats to hand, parents to hand). Now `HandToGrabbable`, `parentOnGrab=false`, kinematic, so they never move.
- **Umbrella:** fall cap 2 -> 0.8 m/s; has a furled look when closed.
- **Jump:** `PlayerJump`, right controller A (or Space). 3.2 m/s take-off, only when grounded.
- **Labels** 35% smaller.
- **Wrist menu** (`WristMenu`): look at back of left wrist, poke the button with the right index finger. Labels toggle, time slider, clock pause, refill needs, freeze needs, fuel+light fire, douse, raiders arrive/leave, slow-mo, shadows toggle, teleports, reload, FPS in the header. M opens it on a keyboard.
- Tests changed: vine tests now wiggle; meat is dropped inside the pit. `Makeshift > Build Playground Scene` rebuilds everything.
- **Unverified / guesses:** wrist button offset and back-of-hand axis (fields on `WristMenu`, based on RoboPoses axes), poke detection with the real fingertip, jump input binding, hold feel, glider with the real body, text size on notes (checked in screenshots only), shadow cost on Quest.
- `-makeshiftShots` now renders a PNG per area into `Logs/shots` (replaced the render-style shots).

## Poly Pizza models (2026-11-25)
- 22 GLBs downloaded into `Assets/Makeshift/Models` (credits and licences in `CREDITS.md`: CC0 and CC-BY 3.0; keep the credits). glTFast imports them. The critter (frog) failed to import because of its skin (glTFast job error), so its skin and animation were stripped from the file: it is a static T-pose frog. The untouched original is not kept in the project.
- `GreyboxSceneBuilder.Models.cs`: `Model()` loads a GLB, swaps its Shader Graph materials for URP Lit copies (`Assets/Makeshift/Materials/Models`), scales it to a target size and sets the pivot. Every prop tries its model first and falls back to the old primitives if the file is missing.
- Used: campfire, log, torch, meat (cooked = brown tint, burnt = black, via `Cookable`), trough, camp table (also the info, combat and climb tables), stone (also the climbing holds, tinted orange), stick, vine (a long thin plant, wrapped as before), craft table, dummy (a post with a red head; turned to face the player), climb wall (4 tiles, 2x2 m each), drill, toaster, battery, cable (as the plug), club, ore (the drill's rock), ore chunk, switch, critter.
- Not used: paper (the notes need a flat face for text, so they stay my own sheets). Flint is still mine (no model). Spear and hammer are whatever you lash together.
- The drill model is upright, so the old layout changed: the MOTOR and HANDLE sockets and pads moved beside the machine (x = -/+0.38) and the plug hole moved to the back. The bit is still my horizontal chuck and bit in front. If you want the model's own bit to do the drilling, the Drill raycast direction needs to change.
- Cable has no rope physics yet: it is a rigid prop. Separate pass.
- Smoke: 63 checks. The check "dropped loot is a free object again" fails now and then (about 1 run in 3): the scavenger is at its den when the brick lands and picks the spoon straight back up. A test race, not a game bug, but not fixed.

## Playtest fixes (2026-11-26; none verified with real hands/headset yet)
- **Vine:** touch its end to a glued joint, then carry it round the joint twice (move the hand or twist the weapon). Rope winds on, snaps tight, joint is permanent (`StickyBinder`). Reach/turn values are guesses.
- **Glue:** glued-only joints break after 2-3 hits and the head drops as a loose prop (`GlueBond`); vined ones never break.
- **Weapons:** `ImpactDamage` now sweeps every solid part by speed (glued heads included) as well as using collisions.
- **Wrist menu:** opens in front of the player and stays put; closes if you walk 1.5 m away.
- **Bucket:** drops with their own physics (`Bucket`); fuller spills sooner; 12 drops on the campfire put it out.
- **Raiders:** carry loot crosswise in front of the mouth; stashed loot is ignored for 20 s.
- **Fire:** Stylized Fire asset removed; Fully Opaque Fire (`Assets/RomanCHACORNAC`) used for all fires, and smoke, sparks and water use the same solid chunks. `Makeshift > Apply Opaque Fire Style` restyles placed objects. Android build not re-run.
- Backup from before this pass: `D:\DEV\MakeShift_backup_pre_playtest_fixes`.
- **Auto Hand patch:** `CollisionTracker.CheckTrackedObjects` now skips destroyed objects (original in the backup folder).

## Playtest round 3 (2026-11-26; 71 smoke checks pass on a copy; not headset-verified)
- Soft additive glow at every fire (`ParticleFactory.AddGlow`). Trough spout now collides with the bucket and props. Dipping fills in 0.15 s.
- Finished vine wrap is a solid tube just behind the head, fitted to the drawn mesh.
- Glue fix: a piece with dependent scripts (machine parts) merges cleanly; tub also coats on stay.
- Wrist menu 0.68 m away, solid face (Furniture layer) stops the hand, press cooldown.
- Player body/head radius 0.143 / 0.132 (+10%). Backup: `D:\DEV\MakeShift_backup_pre_round3`.
