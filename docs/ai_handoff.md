# AI Handoff

Read this, then [architecture.md](architecture.md), then [roadmap.md](roadmap.md). Written on the
assumption another agent continues tomorrow with no memory of this session.

**Last updated:** 2026-09-02
**Branch:** `development`
**State:** **M0 through M9 complete.** 376 edit-mode tests, all passing. Project compiles clean, and
every milestone's acceptance criterion has been measured in play mode rather than assumed.

**M10 is deliberately not built.** Section 10 of [interaction_contract.md](interaction_contract.md)
lists `challenge_set` / `challenge_result` as reserved and says *"Do not build against these yet."*
Nothing here touches them, and there is no Unity-to-Python send path beyond the Socket.IO handshake.

Two smaller pieces are also outstanding on purpose - the microphone (optional in the brief) and
tail-bone deformation (waits on Python's `anatomy` block, which is null on every payload today). Both
are in [tasks.md](tasks.md) under "What is left", with the reasoning in
[decisions.md](decisions.md) D-010.

> **If the Editor stops compiling.** It wedged once during this work: script compilation was requested
> and never ran, and `Library/ScriptAssemblies` went stale while the Editor stayed otherwise
> responsive. `UnlockReloadAssemblies`, forced synchronous import, and a clean-build-cache compile
> request all failed to shift it; **restarting the Editor fixed it immediately**. Restart first rather
> than spending time on it. If Unity is unavailable altogether, the section at the bottom of this file
> shows how to compile and exercise the code with Unity's own Roslyn and Mono.

## What was built

The aquarium started as a fish slideshow: schema v1 payloads, and a personality rolled from
`UnityEngine.Random` for every fish. That contradicted the one guarantee the installation rests on, so
the identity path was rebuilt around a deterministic hash and the rest of the milestones were built on
top of that.

| Milestone | What it added |
|---|---|
| M0 | Dedupe by `id`, schema-version tolerance, null-block survival |
| M1 | Schema v2 wire types, `FishTraits`, deterministic fallback, trait-driven motion |
| M2 | `IPointerSource`, approach/flee, startle, ripples, affection |
| M3 | `FishSpatialHash`, alignment and cohesion by `social`, new-arrival recognition |
| M4 | `FoodField`, surface-press feeding, `aggression` deciding who eats |
| M5 | Roster and artwork persistence, capture-id seeding, birth moment, created-today tally |
| M6 | Energy, currents, day/night, storms, a procedural predator, departure |
| M7 | Bonds, eggs, babies with blended traits and colour |
| M8 | Per-`object_type` behaviour: anchoring, pulsing, predation, the confidence gate |
| M9 | Rarity glow and trails, the anatomy y flip, eye highlights |

Every acceptance criterion was measured rather than assumed; the figures are in
[roadmap.md](roadmap.md) under each milestone.

### The files most worth knowing about

`Scripts/Core/`: `StableHash` and `DeterministicRandom` (the determinism guarantee),
`AquariumManager` (the hub - population, pointer, ripples, food, world systems, bonds, eggs),
`AquariumPersistenceService`, `AquariumEnergy`, `CaptureCalendar`.

`Scripts/Fish/`: `FishTraits` and `FishTraitFallback`, `FishSpatialHash`, `FishFlocking`,
`FishBondRegistry`, `EggField`, `FishTraitBlend`, `FishBehaviourProfile`, `FishRarityProfile`,
`FishArtworkCache`.

`Scripts/Aquarium/`: `FoodField`, `AquariumFlow`, `AquariumCycle`, `AquariumPredator`, and a renderer
per visible field.

`Scripts/Network/`: `FishTraitResolver` and `FishAnatomyLandmarks` - the two places wire types are
translated into domain types.

### Scene wiring, all of it required

`Scenes/Aquarium.unity` gained: a `MousePointerSource` on the `AquariumManager` object (assigned to
its `pointerSourceBehaviour`), an `AquariumPersistenceService` on `Bootstrap` (with both ingest
services in its array), and `Food`, `Eggs` and `Predator` objects carrying their renderers.
`AquariumBackground` and `BubbleSystem` were also given the manager, which is how they read the mood.

**A missing reference degrades silently rather than erroring.** No pointer means the tank reacts to
nothing; no persistence service means it never saves. The debug overlay reports both.

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

## Reproducing the independent compile and logic check

Useful whenever Unity is unavailable, and the only reason M3 has any verification at all. Unity ships
Roslyn and Mono, and the IDE-generated `.csproj` files already carry the exact reference set and
defines Unity itself uses:

```bash
grep -oE "<HintPath>[^<]+</HintPath>" FishAquarium.csproj | sed -e 's|<HintPath>||' -e 's|</HintPath>||' | sed 's|.*|/r:"&"|' > refs.rsp
```

Add `/r:Library/ScriptAssemblies/BestHTTP.dll` — it is a project reference, so it has no `HintPath` —
then compile with
`dotnet "<UnityEditor>/Data/DotNetSdkRoslyn/csc.dll" @your.rsp`, **run from the project root** because
the reference paths are relative. Run the result with
`<UnityEditor>/Data/MonoBleedingEdge/bin/mono.exe`. A harmless "Can't find custom attr constructor"
warning about `UnityEngine.SharedInternalsModule` is expected and does not affect results.

## Measuring behaviour in play mode

Worth knowing, because it is how M1 to M3 were signed off and the same approach will serve M4.

Drive the running scene with `execute_code` rather than by hand: find the components with
`FindObjectOfType`, inject synthetic captures through `SocketFishIngestService.InjectRawPayload`, and
read back positions and forces. Two traps caught this session:

- **The C# compiler behind `execute_code` is C# 6.** Local functions are a syntax error; inline the
  loop instead.
- **A tool round trip is roughly ten seconds**, which is longer than `noveltySeconds` (5). Anything
  timed shorter than that cannot be observed across two calls. Either create the fish through
  `FishFactory` and read the value in the same call, or widen the window by reflecting on the private
  config field — and put it back afterwards.

To isolate one trait, hold every other trait equal. The schooling measurement used sixteen fish
differing only in `social`, at one `preferred_depth`, with curiosity and fear zeroed, so nothing else
could account for the clustering. The recognition measurement went further and evaluated
`CalculateFlocking` for a curious and an incurious fish at the *same* point, making the difference
between them the recognition term alone.

**`AquariumConfig` is a ScriptableObject, so runtime edits to it persist in the Editor.** Raising
`maxFishCount` to 200 for the performance run meant recording the original, using
`ApplyExternalOverrides` to change it, and restoring it afterwards — then checking the `.asset` on
disk was still clean. Do the same, and check.

## A pattern worth copying, and one worth avoiding

**Copy this.** `RippleField` and `FoodField` are both plain classes ticked from
`AquariumManager.Update`, with a separate MonoBehaviour for the visuals where there are any. That
keeps the whole simulation unit-testable without a scene, keeps the frame order in one place, and
means presentation can never change behaviour. M6's world systems should be built the same way.

**Avoid this.** M4's first build wired `aggression` into contested food only, exactly as the brief
words it, and the trait turned out to have no measurable effect because contests almost never happen —
the tank measured *backwards* on its own acceptance criterion. A trait wired only into a rare branch
is indistinguishable from a trait wired into nothing. When you connect a trait, measure whether the
branch is actually taken before believing it works. [decisions.md](decisions.md) D-007 has the full
account.

## Next recommended task

There is no next milestone that can be started: M10 is blocked by the contract, and the two remaining
pieces both wait on something outside Unity. See "What is left" in [tasks.md](tasks.md).

The most useful things to do instead, in order:

1. **Run a player build with `maxFishCount` raised** and confirm the frame budget at 200 fish. The
   flocking pass is measured at 0.671 ms, but the editor caps itself at 60 fps with vSync so total
   headroom was never measurable.
2. **Put a PNG in `Assets/StreamingAssets/Sample/`** so the F5/F6/F7 self-test keys work.
3. **Tune the response curves on `AquariumConfig`.** Everything trait-driven runs through them, and
   they were given sensible defaults rather than art direction.
4. **Re-serialise `AquariumConfig.asset`.** Every field added across these milestones exists only as a
   C# field initialiser; it works, but a default changed in code silently changes an installation.

Before writing anything, read AGENTS.md at the repository root. Its rules on style, `var`, expression
bodies and brace placement are strict and are enforced by `.editorconfig`; the existing code follows
them exactly and new code that does not will stand out.
