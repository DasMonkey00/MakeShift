# Makeshift — Phase 1 foundation scripts

Sprint 1 of the Phase 1 production outline: the systems everything else plugs into.
Drop the `Assets/Makeshift` folder into your Unity project. The scripts don't depend on any hand
system, XR package or netcode library, so they compile on their own.

These haven't been compiled yet: there was no Unity install to test them against. Expect a small fix
or two on first import.

## What's here

| Folder | Scripts | What they do |
| --- | --- | --- |
| Core | `Trait`, `TraitHolder` | Burns, Conducts, Powers, Blows, Floats, Sticks, Sharp, Heavy. Tick them on any object |
| Fire | `Flammable`, `HeatSource`, `WaterVolume` | Heat builds up, things ignite and spread fire; water puts it out and floats things |
| Power | `PowerSource`, `PowerPlug`, `PowerPort`, `CrankGenerator` | Batteries and generators; plugs and ports; bad connections flicker; cranks charge batteries |
| Machines | `MachinePart`, `PartSocket`, `Machine`, `Drill`, `OreNode` | The "it works, badly" framework, and the hand-crank drill |

## How "it works, badly" works

- Any part fits any socket. The wrong part only lowers **reliability** (default ×0.7).
- A machine's reliability is the average of its parts. Low reliability means more failures.
- While running, a machine can **sputter** (weak, erratic output), **jam** (stops), **overheat**
  (stops until cool) or **backfire** (knocks things back, may eject a part, then jams).
- **Bonk** it (any collision over `bonkImpulse`) to fix a jam or a sputter. It usually works.
  Sometimes it backfires. Every bonk wears a part down a little.
- Water instantly cools an overheated machine, and may make a running one sputter.
- Power connections get a random quality each time; bad ones flicker.

All failure rates, chances and timings are exposed in the Inspector for playtest tuning.

## Build the test drill (greybox, about 15 minutes)

1. **Rock:** a cube with an `OreNode`. Give it an ore prefab (a small cube with a Rigidbody).
2. **Drill frame:** an empty object with `Machine` and `Drill`. Keep the frame static or kinematic
   for now. Add a child `BitTip` and place it just *outside* the frame's own colliders, pointing at the rock.
3. **Sockets:** two children with a trigger collider and `PartSocket`:
   - `MotorSocket`: ideal ids `motor`
   - `HandleSocket`: ideal ids `handle`
   Add both to the Machine's socket list.
4. **Parts:** grabbable objects with a Rigidbody and `MachinePart`: a "power drill" (`motor`, 0.7),
   a "toaster" (`toaster`, 0.5) and a "stick" (`handle`, 0.6). Try the toaster in the motor socket.
5. **Power:** a "car battery" with `TraitHolder` (Powers, Heavy) and `PowerSource`.
   A cable end with a trigger collider and `PowerPlug` pointing at the battery.
   On the drill, a child trigger with `PowerPort`, assigned to the Machine.
6. **Switch:** call `Machine.Toggle()` from a lever or button (a UnityEvent is fine for now).
7. **Hand crank (optional):** a hinge with `CrankGenerator` charging a second `PowerSource` with
   `infinite` off. Plug that into the drill instead of the battery.

Test it: turn it on, drill, watch it jam, bonk it, swap in the toaster, pour water on it.

## Hooking up Auto Hand

- Add `Integration/AutoHandBridge` to an object in your first scene. Parts then snap into
  sockets only after the player lets go of them inside the socket.
- It calls Auto Hand's documented `Grabbable.GetHeldBy()`. If your modified version changed that, edit one line.
- Machine parts need both a `Grabbable` (Auto Hand) and a `MachinePart` on the same object.
- Without the bridge, a part snaps in after hovering still in the socket for `snapDelay` seconds.
- Option for later: Auto Hand's own `PlacePoint` could do the snapping instead of `PartSocket`'s,
  calling `PartSocket.Insert` from its place event. Worth deciding once I can see your template.
- Bonks need no hook: a held club with a Rigidbody hitting the machine counts automatically.

## Notes for networking later

Every machine state change goes through one method (`Machine.SetState`), and fire goes through
`Flammable.Ignite` / `Extinguish`. When netcode goes in, the host owns those calls and clients
just display the result.

## Next up (Sprint 2: core verbs)

Bonk damage and hit reactions for creatures, throwing damage from weight and speed, climbing,
the umbrella glide, harvesting and the six relics.
