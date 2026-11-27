# Quest 3S budget: progress log

Plan: draw calls, rigidbodies and the World Budget engine (handoff plan "Quest 3S draw calls, World Budget engine, asset audit").
Status key: **done** = coded and checked by reading, **coded-unverified** = written but not compiled or run, **blocked** = needs the editor or a device, **deferred** = not started, reason given.

**Verification (2026-11-26, editor over MCP):** all new code compiles with no errors. The ToonNature scene was rebuilt with the `WorldBudget` and run in play mode without a headset (Quest URP asset). Checked: shadow, MSAA and cap cycles change the live values; with the bubble radius at 3 m it froze 10 of 30 tracked bodies and thawed them all when switched off; hands stayed dynamic; AI periods read 0 / 0.1 / 0.5 s at near, mid and far; the perf card shows the rigidbody row; menu rows 10 and 11 exist. URP asset values were unchanged after play. **Not checked:** headset look of the taller menu, creature behaviour (ToonNature has no creature), two players, an Android build, any device numbers. The checks above are the extent of testing.

The rebuild also re-saved 38 materials in `Assets/Makeshift/Materials` (colour and texture slot sync); these were clean before and were restored with git. The previous scene is saved at `Logs/ToonNature.unity.before-worldbudget`.

**Audit results (ToonNature):** 248 meshes, 283 materials, 69 textures. Biggest targets: grass patch 01 (3020 instances), then pine trees 05C, 02C, 03C, 01C and 04C (41 to 96 instances each). 31 of 60 imported textures have mipmaps off and none sets an Android format override. `Toon/TFF_Board_Cutout` is alpha-blended with ZWrite off despite its name, so step 6 means a shader change. Full list: `Logs/AssetAudit_Top30.txt`.

**Grass density and baking:** bake with grass at 100% (or leave grass out of the lightmaps). Also, Merged and Instanced draw modes do not pass lightmaps or probes, so `SceneryInstancer` needs work before baked lighting can be judged.
## Second pass (2026-11-26 evening): measured in the editor at Quest quality

Draw calls from `Dev/DrawBreakdown` (eight directions, two spots; editor numbers, not Quest):

| Setup | Average | Worst |
|---|---|---|
| Before this pass (every point light blocks merging, 25 m shadows) | 277 | 489 |
| Merge lights FIRE (new default), 10 m shadows (Quest profile) | 219 | 377 |
| Merge lights NONE, 10 m shadows | 173 | 295 |

- **Cause found:** scenery within 4.5 m of any point light was never merged (about 1,400 renderers). The pack's three plain point lights covered most of it. New wrist switch "Merge lights: ALL / FIRE / NONE"; FIRE keeps only fire and torch lights. In editor renders at the camp the three look practically the same (dusk only; check the night preset on device). FIRE and NONE were each measured; the FIRE worst view is still over the 300 limit.
- **Standing choppable trees** are now merged and drop out on the first axe bite (checked in editor play: notch appears).
- **Textures:** Android default compression switched from ETC2 to ASTC (`ProjectSettings.asset`, backup `Logs/ProjectSettings.before-astc.asset`). 20 pack textures that would have become ASTC 4x4 (skyboxes, dirt road, lights atlas) overridden to 6x6 (meta backups in `Logs/texture-meta-backup`). Scene image textures about 204 MB to 151 MB in the editor.
- **New switches:** wind on/off (stills the sway; does not remove the noise sample cost), tree cutting on/off, foveation Off/Low/Med/High (OpenXR Foveated Rendering feature enabled for Android in `OpenXR Package Settings.asset`, backup `Logs/OpenXR-settings.before-ffr.asset`; effect unverified, the editor has no headset display). Wrist menu is now rows 0 to 13 and taller again.
- **Bug fixed:** turning shadows off and on again dropped the distance to 10 m instead of restoring it.
- **Build:** Android development APK `Logs/MakeShiftBudget.apk` (ToonNature only) succeeded; the only errors are the unused GLES3 fallback.
- **Still open:** merge pipeline across mesh types per chunk (trees are still about 80 draws), baked lighting (`SceneryInstancer` passes no lightmaps or probes), shared pre-cut tree pieces and chunk pool, mesh bottom-face stripping, wind bend weights, camp objects (about 30 draws, they are grabbable rigidbodies).

## Third pass (2026-11-26): chunked trees and a capped chunk pool

- **Draw mode "Chunked"** (wrist menu Draw button, after Merged; `Rendering/ChunkedScenery.cs`): welds standing trees into one mesh per material per 40 m square, near and far version, built on first use (544 renderers into squares in about 40 ms in the editor). Choppable trees stay individual. Default stays **Merged** until the headset numbers are in.
- **Capped pool** (`Engine/CappedPool.cs`, `PoolExpiry.cs`): all trees share at most 12 wood chunks, reused oldest first, gone after 40 s. Editor play: all four trees felled and landed, 12 chunks pooled, none left after 45 s.

| Editor, Quest quality, 10 m shadows | Draws avg / worst | Triangles | Shadow casters |
|---|---|---|---|
| Merged, lights FIRE | 218 / 375 | 349k | 65 |
| Chunked 24 m, FIRE | 178 / 330 | 420k | 35 |
| **Chunked 40 m, FIRE** | **165 / 310** | **457k** | **30** |
| Merged, lights NONE | 165 / 283 | 360k | 43 |
| Chunked 40 m, NONE | 140 / 267 | 459k | 29 |

- **Costs of Chunked:** about 31% more triangles than Merged, and extra mesh memory (the 24 m build read about 105 MB in the editor; not measured on device). A square swaps near/far as a whole. Wind sway no longer turns with each tree's rotation. Camp renders looked the same as Merged.
- **Checked in editor:** switching Standard, Merged and Chunked hands renderers back correctly; shader buttons change the chunk materials.
- **Build:** `Logs/MakeShiftChunked.apk` succeeded. It is 24 MB larger on disk than the previous APK with identical contents (packaging slack).
- **Still open:** baked lighting, a shared falling-tree prefab per species, mesh bottom-face stripping, wind bend weights, camp object draws.

## Fourth pass (2026-11-26): extra forest for distance testing

- `Editor/ToonForestBuilder.cs` plants 6000 seeded random pines within 320 m of the camp (terrain is 800 x 800 m), in tiers of 1000 / 2000 / 3000. Keeps clear of the camp and start (14 m), road, steep ground, rocks, props, water and other trees (4.5 m).
- Each extra pine has a third level, the pack's flat card, in a copy material (`ToonNature/Forest Tree Card.mat`, dark pine colour). Distances (Mid LOD): full model to 15 m, simpler to 45 m, card to 260 m (new `FarTree` kind in `DetailPresets`). Card casts no shadow.
- Wrist switch "Extra forest: OFF / LOW +1000 / MED +3000 / HIGH +6000" (row 14, `Rendering/ForestDensity.cs`), starts HIGH.
- Not choppable (only the four marked pines are). Not welded in Chunked mode (memory).
- Scene file 19.5 MB to 61.6 MB; APK level data 8.3 MB to 19.2 MB. Build `Logs/MakeShiftForest.apk` succeeded (only GLES3 fallback errors). Previous scene: `Logs/ToonNature.unity.before-forest`.

Editor, Quest quality, 10 m shadows, Merged draw (`Dev/ForestSweep`, camp viewpoints):

| Forest | Draws avg / worst | Triangles avg / worst | Scenery sort per update |
|---|---|---|---|
| OFF | 216 / 363 | 349k / 575k | 1.8 ms |
| LOW +1000 | 222 / 368 | 356k / 578k | 1.9 ms |
| MED +3000 | 221 / 368 | 363k / 583k | 2.0 ms |
| HIGH +6000 | 221 / 368 | 377k / 594k | 2.2 ms |

- **Watch on device:** `SceneryInstancer` sorts all scenery on the CPU whenever the head moves 0.5 m or turns 5 degrees. That is about 2 ms on this PC; Quest CPUs are slower and VR heads turn constantly. Spreading the sort over frames, or sorting by square, is the likely next step. CPU ms, load time and memory are not measured on device.
- The pack's original background tree cards stay pale and now stand out against the darker forest.

## Fifth pass (2026-11-26): every pine choppable, new cut, landing leaves, forest grass

**Every pine can be chopped** (`Nature/TreeCutting.cs`, `Combat/AxeHead.cs`): a swing near any pine adds `ChoppableTree` to that one tree; untouched trees cost nothing. 6276 pines, 16 readable model copies made by the builder. Editor play: 34 of 34 sampled trees (8 types) found, felled and landed. Chunked draw mode rebuilds the affected square; switching the forest off hides cut trees and their logs. Felled trees are not capped: their meshes and objects accumulate.

**Cut look** (`Nature/TreeMesh.cs`): built from the model's own bark, so it joins with no colour seam; ragged hacked edge; bare wood uses the pack's toon shader with a simple painted ring texture (`ToonNature/Trees/WoodGrain.png`); notch ends at 60% of the trunk. Editor, one tree: first chop about 44 ms, later chops about 10 ms. **Quest will be slower; expect a visible hitch on a first chop.**

**Landing:** a burst of 220 to 450 leaves when the crown hits.

**Forest grass** (`Rendering/ForestGrass.cs`): 143,000 patch places (24 per extra tree, 0.6 to 6 m from the trunk, off the road and rocks) worked out at start (about 170 ms in the editor), only nearby ones drawn, instanced, no objects in the scene. Follows the Extra forest, Grass % and LOD buttons. About 10 extra draws in one forest view.

**Not done:** no Android build with the cut, leaf or grass changes (skipped on request). `Logs/MakeShiftAllTrees.apk` has every-tree cutting but the old cut look and no new grass. Nothing checked on a headset.

## Progress log

| Step | Status | Files | Numbers | Next user action |
|---|---|---|---|---|
| 0. Quest dev build baseline | blocked | none | none this session (last recorded: 265 avg / 493 worst draws in the editor at Quest quality) | Capture a Quest development build at 90 Hz with the perf card: draws, tris, CPU ms, GPU ms, rigidbody count |
| 1. Remove outline pass | done in code (earlier); device capture pending | none this session | not re-measured | Compare the "Toon (pack)" and "Toon no outline" sets on device (wrist menu) |
| 2. Terrain instancing | compiled; checked in editor play | `Scripts/Engine/WorldBudget.cs` | builder already sets `drawInstanced` and pixel error 5; WorldBudget also sets detail distance 35 m at run time | Device capture (the scene is rebuilt) |
| 3. Generalise merge pipeline | deferred | none | none | Needs a design call: chunking per area and material family, and where the build-time step runs. `SceneryInstancer` already merges per material and LOD |
| 4. Physics bubble and cap | compiled; single-player editor check | `Scripts/Engine/PhysicsBubble.cs`, `Scripts/Integration/AutoHandBridge.cs`, `Scripts/UI/PerfStats.cs` | cap presets 20 / 40 / 60 / off; rigidbody row on the perf card | Two-player test: rigidbody count stays under cap; held objects never sleep |
| 5. Lighting and short shadows | partly done | `Scripts/Engine/WorldBudget.cs`, `Scripts/Rendering/ToonLookSwitcher.cs` | shadow distance Off / 10 / 15 / 25 m; cascades from profile; values restored when disabled | Bake Subtractive after SceneryInstancer handles lightmaps. Decide whether to freeze one time of day |
| 6. Cutout foliage and billboards | billboards done and built; device check pending | pack `Shaders/TFF_Board_Cutout.shader` (edited), `Assets/Makeshift/Shaders/BoardBlended.shader`, `ToonLookSwitcher.cs`, `ToonNatureSceneBuilder.cs` | 218 billboard renderers now alpha clipped with depth write (queue 2450, was 3000); billboard mipmaps keep coverage; switch "Billboards: CUTOUT/BLEND" (row 11 col 1) tested in editor play. Android dev build succeeded (`Logs/MakeShiftBillboards.apk`, 155 MB, ToonNature only); its 6 errors are the pack shaders and the blended copy failing the unused GLES3 fallback. Editor comparison: `Logs/billboard-cutout-vs-blend.png`. Original shader saved at `Logs/TFF_Board_Cutout.shader.original`; re-importing the pack undoes the edit. Grass was already cutout | Install the APK; compare GPU ms with the switch in both positions and look for edge shimmer |
| 7. Shared tree-cut system | deferred | none | none | Feature is untested in play. Findings: `ChoppableTree` has no per-tree `Update`; its LODGroup is only created on cut pieces (`Group`, about line 261). Standing-tree cost work waits for a play test |
| 8. AI time slicing | compiled; tier periods checked, no creature run | `Scripts/Engine/AiScheduler.cs`, `Scripts/Engine/Viewers.cs`, `Scripts/Creatures/ScavengerCreature.cs` | near under 15 m: every frame; 15–35 m: 10 Hz; beyond: 2 Hz | Watch creature behaviour at range; the wrist menu "AI tiers" switch turns this off |
| 9. Asset audit | done, run on ToonNature | `Editor/AssetAudit.cs` | output: `Logs/AssetAudit_*.csv`, `Logs/AssetAudit_Top30.txt` | Mesh and texture trims are next, from the top-30 list |
| 10. Quest settings | partly done | `Scripts/Engine/WorldBudget.cs` | MSAA 1x / 2x / 4x on the active URP asset (restored when disabled) | Test MSAA 2x against 4x on device. FFR: not wired (no OpenXR foveation call in the project); needs a device check |
| 11. Light tweaks (wind bend weights, grass density, fog and sky cost) | deferred | none | none | Needs device numbers and a mesh check for bend weights |

## Menu switches (wrist menu, ToonLookSwitcher)

| Switch | Where | Status |
|---|---|---|
| Outline look (Toon (pack) / Toon no outline) | existing shader-set buttons, row 0–2 | existing |
| Draw mode (Merged / Instanced / Standard) | row 2 | existing (terrain instancing now also set by WorldBudget) |
| Lighting presets | rows 3–4 | existing (baked lighting not yet) |
| Shadows: Off / 10 / 15 / 25 m | row 5 col 1 (was On/Off) | new; key O still toggles on/off |
| Grass density | row 7 col 0 | existing |
| Bubble: OFF / 20 / 40 / 60 | row 10 col 0 | new |
| AI tiers: ON / OFF | row 10 col 1 | new |
| MSAA: 1x / 2x / 4x | row 11 col 0 | new |
| Rigidbody count on perf card | perf card, row "rigidbodies" | new |
| Wind on/off, tree cutting on/off, FFR | not added | deferred |

The wrist panel now runs down to row 11. Its bottom edge moved from −0.295 to −0.365 (`Scripts/UI/WristMenu.cs`). Check the layout in the headset.

## Needs user / editor

1. **Headset check** of the rebuilt ToonNature scene: menu rows 10 and 11, the new switches, tree cutting.
2. **Test a creature at range** in a scene that has one.
3. **Device captures** at 90 Hz: step 0 baseline, then after each step. Record draws, tris, CPU and GPU ms, and rigidbody count.
4. **Android build check** after the rebuild (the Quest quality level must still be present; see memory `makeshift-quest-quality-level`).
5. **Design calls:**
   - Bake Subtractive lighting and freeze the day cycle at one time (step 5)?
   - Generalise the merge pipeline into the engine (step 3), or keep `SceneryInstancer` as it is?
   - Tree-cut shared pool (step 7): wait for the play test?
   - Particles switch: it is still on the wrist menu (I did not remove it).

## Known gaps in this pass

- The physics bubble only freezes bodies at rest, so a body flying past the bubble edge stays dynamic until it lands.
- The bubble does nothing until the hand bridge (`AutoHandBridge`) has set its exemption hook. A scene without the bridge runs with the bubble off, so hands are never frozen.
- Each client runs the bubble on its own loose scenery. Networked objects are exempt, so nothing is frozen that the network owns. Frozen-by-bubble state is never sent over the network.
- `WorldBudget` changes the URP asset at run time and restores it on disable. It does not save anything to the asset.
- Desktop keeps the PC asset's shadow distance and MSAA unless `applyToDesktop` is set on the profile (`Assets/Makeshift/Settings/BudgetProfile.asset`, created by the builder).
