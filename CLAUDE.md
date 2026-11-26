# Makeshift — Claude Code project brief

Read this first. It is the handoff from the planning chat: what the game is, what's decided,
what exists, and what to do next.

## The game in one paragraph

Makeshift is a premium co-op VR survival game (1–4 players, solo playable) for **Meta Quest 3 and
PC VR on Steam, with cross-play from one codebase** (Meta Quest 3S native is the performance target: **90 fps**). A tribe of primitive survivors lives in an overgrown
post-apocalyptic jungle full of old technology they don't understand. They scavenge junk, build
machines from the wrong parts, and slowly re-power a buried AI facility. The comedy comes from
misunderstood tech and physics slapstick; underneath is real survival (food, warmth, shelter,
hunting, crafting). Finished game, not a live service.

## Design rules (apply to every feature)

1. **It works, badly.** The core rule. Every tool, machine and robot is built from wrong parts and
   sputters, jams, overheats or backfires. A good bonk usually fixes it. If something works
   perfectly, it's unfinished.
2. **Use it wrong.** Objects have simple traits, not recipes, so unplanned combinations work.
   If an object has only one use, it's unfinished.
3. **Fun first, story second.** The sandbox must be fun with the story switched off.
4. **Built to be clipped.** Physics slapstick, chain reactions and friends getting flung.
5. **Tone:** funny situations, not goofy visuals. Characters stay believable; danger looks real.
   Avoid cringe.

## Tech stack (decided)

| Area | Choice |
| --- | --- |
| Engine | Unity 6+, URP |
| XR | OpenXR |
| Hands and interaction | **Auto Hand** (Earnest Robot), a modified version in our project template. Read our modifications before changing anything |
| Netcode | **Photon Fusion 2, Shared mode**. Fallback: Normcore (only if Fusion can't make Auto Hand grabbing feel right by end of week 2) |
| Voice | Photon Voice (proximity chat) |
| Starting point for networking | Photon's VR Shared sample and the XRShared addon |

## Platforms and one shared codebase (decided)

Ship on **Meta Quest (Horizon Store)** and **Steam (PC VR)** with **cross-play**, from **one codebase**.
Never build a feature twice for two platforms.

- **One netcode for both:** Photon Fusion 2 runs on Quest and PC, and players on both join the same
  Photon sessions. Don't use platform-specific networking (Steam relay / Steam Networking Sockets,
  Meta-only networking): Steam's relay doesn't cover Quest, so it would mean a second networking path.
- **One input and XR path:** OpenXR + Auto Hand on both. Per-platform differences live in build
  profiles and settings, not separate code.
- **Thin platform layer for store features only.** Put store-specific services behind one interface
  (e.g. `IPlatformServices`): sign-in / user name, entitlement check, friends and invites,
  achievements, rich presence. Two implementations: Meta Platform SDK (Quest) and Steamworks (PC).
  Game code only talks to the interface. Pick the implementation per build with scripting defines.
- **Invites become Photon session names.** A Steam or Meta invite just carries the Photon session
  name to join, so matchmaking logic stays shared.
- **Performance target: 90 fps native on Meta Quest 3S.** This is a hard budget for every feature, shader, asset and effect.
  We mostly develop and test in Windows and the editor/PC VR preview, which is far faster than a Quest, so never judge
  performance from PC: always keep the Quest 3S limit in mind and verify on device (and make an Android build after any
  rendering or shader asset change). PC gets higher settings from the same content (quality tiers).
- **Looksmith (stylized shader asset) is parked.** Its Lit shader does not compile for Android and breaks the Quest build.
  Everything uses URP/Lit for now; it must be removed or replaced. Details and checklist: "Looksmith parked" in PHASE1_STATUS.md.

## Netcode costs (for planning)

Both Photon and Normcore are paid cloud services beyond their free tiers; neither is self-hosted on
standard plans. Fusion is the better fit for small co-op rooms:

| | Photon Fusion 2 (chosen) | Normcore (fallback) |
| --- | --- | --- |
| Develop | Free, capped at 20 CCU | Free tier |
| Launch free | 100 CCU free for one app (stackable to 200), 0.3 TB/month, no bursts | Free tier limits unclear for shipped games |
| Paid | 500 CCU for $125/month (1.5 TB), then larger tiers | $49/month (10,000 room hours, 3 TB), then $0.03/room hour and $0.10/GB |
| Billed by | Peak concurrent players + traffic | Room hours + bandwidth (every small co-op room bills by the hour) |

CCU = players connected at once; 100 CCU is about 25 four-player sessions. Free tiers don't allow
bursts, so plan the upgrade before launch day. Prices checked Nov 2026; recheck before launch.

Normcore's drawbacks as a fallback: smaller community, fewer Auto Hand reports (one "launched into the
air" problem), and room-hour billing that gets relatively expensive for many small co-op rooms.
Its strength is built-in networked physics and voice.

## Networking architecture (decided)

- **Two rigs per player.** The local player runs the full Auto Hand rig (the "hardware rig",
  not networked). Everyone else sees a **network rig**: visual-only copies of head and hands,
  no Auto Hand physics, no joints, no rigidbody hands. Running Auto Hand on remote copies causes
  fights between physics and network sync (players reported "launched into the air").
- **Grab = take authority.** On Auto Hand grab, call `Object.RequestStateAuthority()` on the
  grabbable's NetworkObject. Consider requesting on **highlight** (hover) instead of grab to hide
  latency. On last release, keep authority until the object comes to rest, then optionally release.
- **Shared objects** (machines, creatures, fire, world state) are owned by the Shared-mode master client.
- **Player-vs-player hits** (bonking a friend) are resolved by the attacker: the attacker's client
  detects the hit and sends an RPC; the victim plays the reaction. Remote hands are visual-only,
  so physical contact between players can't be relied on.
- **Two players carrying one object** is the hardest case: the first grabber keeps authority; the
  second player's grip is sent as a target and applied as a pull force by the owner. Phase 1: "Should", not "Must".
- Sync only what matters: held items, machines, creatures, hits. Loose scenery physics stays local.
- All game-state changes go through single methods (e.g. `Machine.SetState`, `Flammable.Ignite`)
  so authority checks can be added in one place.

## Auto Hand + Fusion: what the community found

From the official Auto Hand Discord (support-forum and general, searched Nov 2026):
- One developer (thread "Photon Fusion in Host Mode", Mar 2025) reported Auto Hand + **Fusion Shared
  mode working as expected**, using a NetworkBehaviour on each grabbable that subscribes to
  `Grabbable.OnGrabEvent` / `OnHighlightEvent` and `OnReleaseEvent` (signature `(Hand hand, Grabbable grabbable)`),
  calling `RequestStateAuthority()` / `ReleaseStateAuthority()`. Write our own version of this
  pattern; don't copy their file.
- The same developer found **Host mode much harder**: Auto Hand stays on the player side and sends
  held / released / moved states to the network rig, with the host applying them. Another developer
  trying server-authoritative grabbing with Netcode for GameObjects hit the problem that the grab only
  happens on the client. This is why we use Shared mode.
- One report: hand movement became **jerky** when Fusion's prototype network start was in the scene,
  smooth without it. Watch for timestep / physics simulation conflicts between Fusion and Auto Hand's
  rigidbody hands (check Fusion physics settings, interpolation, and that the local hand isn't being
  driven by a NetworkTransform).
- Many unanswered questions there (spawning Auto Hand player from a prefab, syncing place points).
  Expect to solve these ourselves.

Auto Hand docs: https://earnest-robot.gitbook.io/auto-hand-docs (no official networking page).
Useful documented API: `Grabbable` events On Grab, On Release, On First Grab, On Last Release,
On Highlight; methods `GetHeldBy()`, `HandsRelease()`, `ForceHandsRelease()`; settings Parent On Grab,
Joint Break Force, Custom Grab Joint. Known issue: jitter under heavy physics; tune physics quality early.

## What exists in this package

`Assets/Makeshift/Scripts/` — Sprint 1 foundation, written without a Unity compile. **First job:
import, fix compile errors, and confirm against our modified Auto Hand.**

| Folder | Scripts | Purpose |
| --- | --- | --- |
| Core | `Trait`, `TraitHolder` | Object traits: Burns, Conducts, Powers, Blows, Floats, Sticks, Sharp, Heavy |
| Fire | `Flammable`, `HeatSource`, `WaterVolume` | Heat, ignition, spread, extinguish, buoyancy |
| Power | `PowerSource`, `PowerPlug`, `PowerPort`, `CrankGenerator` | Batteries, cables, flickering bad connections, hand cranks |
| Machines | `MachinePart`, `PartSocket`, `Machine`, `Drill`, `OreNode` | "It works, badly" machine framework + hand-crank drill |
| Integration | `AutoHandBridge` | Parts snap into sockets only after release (uses `GetHeldBy()`) |

`README.md` explains each system and how to build the greybox test drill.

## Known issue: 2-second VR stutter from the MCP for Unity window (check this first)

- **Symptom:** in the editor VR preview, the FPS meter drops hard on a steady 2-second beat.
- **Cause (confirmed by headset recording, 25 Nov 2026):** the MCP For Unity window is open while its local
  server (127.0.0.1:8222) is not running. Every 2 s it probes the server on the main thread and freezes the
  editor for about 250 ms. It is not game code.
- **Fix:** close the MCP For Unity tab. When MCP is needed, click **Start Server** in that window; if the
  server stops while the window is open, expect the stutter again.

**Testing:** test at **90 fps** (set the headset streaming app to 90 Hz). Day-to-day testing uses the PC build;
the Meta Quest Basic (Android) build profile exists for device builds.

## Phase 1 plan (8 weeks, greybox, 2 players online)

Goal: prove the game is fun with the story off. Full outline (shared doc):
https://claude.ai/artifact/LvJ7DmqiL6NHPqznBaUwpw

| Sprint | Weeks | Focus |
| --- | --- | --- |
| 1 | 1–2 | Foundation: physics hands in project, two-headset network test, traits, machine framework |
| 2 | 3–4 | Core verbs: climbing, umbrella glide, bonk/throw/3 weapons, harvesting, 6 relics, scavenger creature |
| 3 | 5–6 | Survive and build: fire, shelter, food, night raids, hand crafting and wiring, hand-crank drill |
| 4 | 7–8 | Co-op interactions (revive, point-and-grunt, voice), tune failure rates, 90 fps on Quest 3S |

Week 8 go/no-go: unprompted laughter in 4 of 5 co-op sessions; an unplanned use in half the
sessions; 3 of 5 pairs ask to keep playing; bonk/climb/build score 4/5+; steady 90 fps on Quest 3S
with 2 players and 3 creatures; held items and hits stay in sync.

## First tasks for Claude Code

1. Read the project: our modified Auto Hand (what changed vs stock), the player rig, scenes, packages.
2. Import `Assets/Makeshift`, fix compile errors, confirm `AutoHandBridge` matches our Auto Hand API.
3. Build the greybox test scene with the drill (README steps).
4. Install Photon Fusion 2 (Shared mode) and the XRShared addon; create the App ID setup notes.
5. Split the player into hardware rig (Auto Hand) + network rig (visual only).
6. Write `NetworkGrabbable`: Auto Hand grab/highlight → `RequestStateAuthority`; release → keep until at rest.
7. Two-headset test: pass a club, bonk each other, throw a toaster. Profile on Quest 3S (90 fps target).
8. Cross-play check: one Quest build and one PC (SteamVR) build in the same Photon session.
9. Stub the `IPlatformServices` interface with a simple editor/offline implementation; real Meta and
   Steamworks versions come later.

## Other docs from planning (shared links)

- Pitch deck: https://claude.ai/artifact/L19Bs1qf9CqczRSusAHYgr
- Phase 1 production outline (includes Netcode section): https://claude.ai/artifact/LvJ7DmqiL6NHPqznBaUwpw
- Art direction: https://claude.ai/artifact/BC3vXHbwNw8wJY5jFajydh

## Story (for context only; not Phase 1 work)

The tribe are descendants of colonists whose memories were wiped by Mother, the colony AI, to hide
them from the Blight (a machine plague). Players mine ore to power relays, opening bunkers
metroidvania-style with wrong-parts machines (toaster furnace melts ice, treadmill pump drains
water). Midpoint twist: the tribe misreads an upside-down cave-painting map and boots **Father**,
the defense AI, instead of Mother. Act 3: survive Father's takeover of their own grid and reach
the real Mother to release the cure.
