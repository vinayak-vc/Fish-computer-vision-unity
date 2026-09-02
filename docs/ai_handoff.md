# AI Handoff

Read this, then [architecture.md](architecture.md), then [roadmap.md](roadmap.md). Written on the
assumption another agent continues tomorrow with no memory of this session.

**Last updated:** 2026-09-02
**Branch:** `development`
**State:** M0, M1 and M2 complete. 186 edit-mode tests, all passing. Project compiles clean, and the
whole ingest-to-swimming path has been exercised in play mode.

---

## What changed this session

The aquarium accepted schema v1 payloads and gave every fish a personality rolled from
`UnityEngine.Random`. That directly contradicted the guarantee the whole installation rests on -
section 6 of the interaction contract - so the identity path was rebuilt around a deterministic hash,
the wire types were brought up to schema v2, duplicate captures are now rejected, and the visitor can
interact with the tank through an abstraction that a hand tracker could later replace.

### New files

| File | Why |
|---|---|
| `Scripts/Core/StableHash.cs` | FNV-1a 64 over UTF-8. The seed everything deterministic hangs off. |
| `Scripts/Core/DeterministicRandom.cs` | SplitMix64 stream, with domain separation. |
| `Scripts/Fish/FishTraits.cs` | The immutable personality every behaviour reads. |
| `Scripts/Fish/FishTraitEnums.cs` | `FishObjectType`, `FishRarityTier`, `FishTraitSource`. |
| `Scripts/Fish/FishTraitFallback.cs` | Contract section 9, the deterministic fallback. |
| `Scripts/Network/FishTraitResolver.cs` | Wire payload to `FishTraits`. |
| `Scripts/Network/FishCaptureRegistry.cs` | Contract section 8, dedupe by `id`. |
| `Scripts/Input/IPointerSource.cs` | The visitor, abstracted. |
| `Scripts/Input/MousePointerSource.cs` | The only implementation today. |
| `Scripts/Input/PointerInfluence.cs` | Interaction rules as pure, testable functions. |
| `Scripts/Input/RippleField.cs` | Click shockwaves and their danger zones. |

### Modified files

`Scripts/Network/FishCaptureMessage.cs` (schema v2 blocks), `FishPayloadDecodeQueue.cs` (dedupe and
trait resolution on the worker), `SocketFishIngestService.cs` (new factory call, schema logging),
`Scripts/Core/AquariumConfig.cs` (trait response, preferred depth and pointer blocks),
`AquariumManager.cs` (owns the pointer source and ripple field), `IFishIngestService.cs`
(`DuplicateCount`), `Scripts/Fish/FishData.cs` (trait-driven, no `Random`), `FishFactory.cs`,
`FishMovement.cs` (pointer, startle, affection, depth band), `FishDepthProfile.cs`,
`FishSizeNormalizer.cs`, `Scripts/Input/FishLoadQueue.cs`, `FishIngestService.cs`,
`Scripts/Debug/DebugOverlay.cs`, `SocketPayloadSelfTest.cs` (now emits schema v2).

### Scene

`Scenes/Aquarium.unity`: a `MousePointerSource` was added to the `AquariumManager` GameObject and
assigned to that object's `AquariumManager.pointerSourceBehaviour`, with its camera pointed at
`Main Camera`. **Without that reference the tank runs but reacts to nothing** - the overlay says
`Pointer: NOT ASSIGNED` when it is missing.

### New tests

`DeterministicIdentityTests`, `FishTraitResolutionTests`, `FishCaptureRegistryTests`,
`PointerInfluenceTests`, `RippleFieldTests`, `FishCaptureSchemaV2Tests`, plus additions to
`FishDepthProfileTests` and `FishSizeNormalizerTests`.

---

## The three things most likely to trip you up

1. **`FishData.Depth` and `FishTraits.PreferredDepth` are different axes.** `Depth` is parallax - how
   far into the scene a fish is drawn, driving scale, opacity and sorting order. `PreferredDepth` is
   where in the water column it likes to swim. Mapping one onto the other puts every surface fish in
   front of the glass. See [decisions.md](decisions.md) D-005.

2. **Never reach for `UnityEngine.Random` in anything a fish is.** Spawn position is the single
   documented exception, and `FishFactory.ChooseSpawnPosition` explains why. Everything else comes off
   `DeterministicRandom`, and the draw order in `FishTraitFallback` is part of the contract with the
   Python side - reordering it silently rewrites the personality of every archived drawing.

3. **The payload's optional blocks really are optional.** `features`, `personality`, `anatomy` and
   `object_type` all arrive null from legacy captures, and legacy captures WILL be replayed on any
   installation with history on disk. `FishTraitResolver` is the only place that should have to care.

---

## How to see it working, with no Python and no camera

Open `Scenes/Aquarium.unity` and press Play. The config ships in Development mode, so the debug
overlay is on.

| Key | What it does |
|---|---|
| F5 | One synthetic schema v2 capture. Press it a dozen times to fill the tank with distinct swimmers. |
| F6 | The ten-capture replay burst. **Press it twice.** The first spawns ten fish with no birth animation; the second must spawn nothing at all and step the overlay's `dupes` counter by ten. That is the M0 acceptance check. |
| F7 | A legacy schema v1 capture with no personality block, which exercises the deterministic fallback. |
| F3 | Clear the tank and forget every capture id, so F6 is admitted again. |

The overlay's `Newest:` line shows the spawned fish's traits and marks `(derived)` when they came
from the identity hash rather than from Python. Its `Pointer:` line shows whether the visitor is
being seen at all.

To check M1 without the sprites: the spread is real, not theoretical. Twenty derived fish measured
against the shipped config span speed 0.55 to 1.45 of a 0.5 to 1.5 range, turn rate 37 to 88 of 30 to
90, and preferred depth 0.00 to 0.99 - the full water column.

Measured in play mode, three fish differing only in `preferred_depth` settle into separate strata:

| `preferred_depth` | settled y | its band |
|---|---|---|
| 0.02 | 3.14 | 1.09 to 4.15 |
| 0.50 | 0.74 | -0.78 to 2.28 |
| 0.98 | -1.26 | -2.65 to 0.41 |

**The sample folder is empty**, so F5, F6 and F7 currently log "the sample folder contains no PNGs"
and do nothing. Drop any PNG into `Assets/StreamingAssets/Sample/` to make them work. That gap
predates this session.

---

## Next recommended task

**M3, flocking and recognition.** Start with the spatial hash, because
`AquariumManager.CalculateSeparation` is an O(n²) sweep and the milestone's acceptance criterion is
200 agents at 60 fps. Full breakdown in [tasks.md](tasks.md).

Before writing anything, read AGENTS.md at the repository root. Its rules on style, `var`, expression
bodies and brace placement are strict and are enforced by `.editorconfig`; the existing code follows
them exactly and new code that does not will stand out.
