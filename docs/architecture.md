# Architecture — Unity side

Assembly: `FishAquarium` (`Scripts/FishAquarium.asmdef`), root namespace `ViitorCloud.FishAquarium`,
referencing only `BestHTTP`. Tests live in `FishAquarium.EditModeTests`, Editor-only.

## Layer map

```
Scripts/
  Core/        configuration, app lifecycle, the population manager, shared value types
  Network/     Socket.IO transport, wire types, the off-thread decode queue
  Input/       folder-watching ingest, and the pointer abstraction
  Fish/        one creature: data, traits, steering, movement, animation, texture lifetime
  Aquarium/    the world: bounds, background, decor, bubbles, water
  Debug/       development-only overlay and self-tests
```

Dependencies point inwards: `Aquarium` and `Fish` know `Core`; `Network` and `Input` know `Core`
and `Fish`; nothing in `Core` knows about the socket.

## The ingest pipeline

```
Python ──Socket.IO──> BestHttpSocketIoTransport
                          │  raw JSON string, main thread, unparsed
                          ▼
                      FishPayloadDecodeQueue.Submit
                          │  ThreadPool worker
                          ├─ LitJson parse into FishCaptureMessage
                          ├─ FishCaptureRegistry.TryClaim(id)   ← duplicates die here
                          ├─ FishTraitResolver.Resolve(...)     ← traits, or the hash fallback
                          └─ Convert.FromBase64String(png_base64)
                          ▼  DecodedFish, ConcurrentQueue
                      SocketFishIngestService.Update, main thread
                          ├─ FishSpriteCache.TryAcquireFromBytes  (Texture2D must be main thread)
                          └─ FishFactory.CreateFish(sprite, id, traits, isReplay)
                                 └─ AquariumManager.RegisterFish
```

Three deliberate properties of this pipeline:

- **Nothing expensive happens on the main thread.** A capture carries up to a megabyte of base64,
  and the replay burst delivers ten at once. LitJson costs roughly 24 microseconds per KB, so
  parsing inline would stall the frame for tens of milliseconds per fish. `RawFishPayloadParser`
  exists solely so the payload reaches the worker as unparsed text.
- **Duplicates are rejected before the base64 decode**, which is the only genuinely costly step.
  `FishCaptureRegistry` is backed by a `ConcurrentDictionary`, so `TryClaim` is atomic and safe to
  call from a worker.
- **Spawning is rate limited** to `MaxFishLoadsPerFrame`, so a ten-event replay burst is spread
  over ten frames rather than landing in one.

The folder watcher (`FishIngestService`, `FishFileWatcher`, `FishFileScanner`, `FishLoadQueue`,
`FishFileReadiness`) is a parallel front end onto the same `FishFactory`. It exists for development
and for installations that receive drawings as files rather than over a socket.

## Traits

`FishTraits` is the immutable, normalised (0..1) personality every behaviour reads. It is produced
once, by `FishTraitResolver`, and never mutated.

```
FishCaptureMessage.personality present ──> use it verbatim, clamped to 0..1
                            null or absent ──> FishTraitFallback.FromSeed(StableHash.Of(id))
```

`FishTraitFallback` is deterministic by construction: a 64-bit FNV-1a hash of the identity string
feeds a SplitMix64 stream, and each missing trait consumes one draw in a fixed order. The same id
always produces the same creature, on this machine and on any other, today and after a restart.
The algorithm is specified exactly in [decisions.md](decisions.md) so the Python side can mirror it.

**`Random` is not used anywhere in the trait path.** `FishData` also draws its Perlin seed, its
animation phase and its parallax depth from the same hash stream, because a fish whose wander
pattern changed on every restart would break the same promise as a re-rolled trait.

## Trait to motion

Traits are inputs; what they mean is a Unity decision and lives in the Inspector. `AquariumConfig`
carries a `Trait Response` block of `AnimationCurve`s mapping each 0..1 trait onto a 0..1 position
within an authored range:

| Trait | Drives |
|---|---|
| `speed` | max velocity, acceleration |
| `grace` | turn-rate limit, pitch smoothing, tail amplitude |
| `speed` + `grace` | tail-beat amplitude and frequency |
| `preferred_depth` | vertical home band, and how hard the fish holds it |
| `curiosity` vs `fear` | pointer attraction against repulsion |
| `fear` | startle threshold against pointer speed |
| `rarity_score` / `rarity_tier` | reserved for glow, trails, birth fanfare (M9) |

`preferred_depth` is a **vertical** home band, 0 at the surface and 1 at the floor. It is not the
same axis as `FishData.Depth`, which is **parallax** — how far into the scene a fish is drawn, and
which drives scale, opacity, sorting order and a small speed multiplier. Conflating the two is the
easiest mistake to make here.

The band works in two halves, and both are needed. `FishDepthProfile.EvaluatePreferredBand` limits
where a fish will *choose* a destination; `EvaluatePreferredDepthPull` corrects it when it drifts
out. The corrective force alone is not enough: a fish told every few seconds to swim to the surface
settles at a mid-water compromise however hard it is pulled down, and `preferred_depth` then reads as
though it does nothing. Movement, wall avoidance and clamping still use the whole aquarium, so a fish
fleeing the pointer can leave its band freely — the band is a habit, not a fence.

## Pointer

Everything a visitor does reaches the fish through `IPointerSource`. `MousePointerSource` is the
only implementation today; a hand tracker would be a second one, and no behaviour code would change.

```
IPointerSource ──> PointerInfluence (pure maths, unit tested)
               └─> RippleField      (click shockwaves, F = strength / distance, decaying)
                        │
                        ▼
                 FishMovement.BuildSteeringVector
```

`FishAffection` accumulates while the pointer lingers slowly near a fish, and inverts that fish's
flee response into an approach once it is high enough.

## Simulation loop

`AquariumManager` owns the population and drives a single `Update`. No fish runs an `Update` of its
own: the manager rebuilds the neighbour index once per frame, then ticks every `FishController`,
which forwards to `FishMovement` and `FishAnimator`.

## Neighbours and flocking

```
AquariumManager.Update
  └─ RebuildNeighbourIndex          one linear pass over the population
       ├─ snapshotPositions[]       parallel arrays, grown once then reused
       ├─ snapshotHeadings[]
       ├─ snapshotNovelty[]         how recently each fish arrived
       ├─ novelIndices[]            the few that still draw a crowd; usually empty
       └─ FishSpatialHash.Build     counting sort into cell order

FishMovement.BuildSteeringVector
  └─ AquariumManager.CalculateFlocking     one query per fish
       ├─ FishSpatialHash.Query            candidates from the touched cells
       ├─ separation   FishSteering.SeparationContribution   inside the separation radius
       ├─ alignment    FishFlocking.AlignmentDirection       weighted by social
       ├─ cohesion     FishFlocking.CohesionDirection        weighted by social
       └─ recognition  FishFlocking.NoveltyAttraction        walks novelIndices directly
```

`FishSpatialHash` is a uniform grid, rebuilt every frame and allocation-free after the arrays have
grown to fit. It is a **broad phase only**: `Query` returns the occupants of every cell the query
circle touches, and the caller measures. That split is what lets one query serve both the separation
radius (1.0) and the wider neighbour radius (2.6) — the narrow-phase test is a squared-distance
comparison inside the accumulation loop.

`maxNeighboursConsidered` caps how many candidates one fish will look at. It is a deliberate quality
trade: when the whole shoal piles into one corner, holding the frame matters more than a complete
neighbour list.

Alignment and cohesion return **directions, never magnitudes**. Returning the raw offset to the
centre of the school would pull a fish with forty neighbours forty times harder than one with two,
and dense schools would collapse to a point. Their strength comes entirely from the config weights
and the `social` response curves, so a solitary fish gets neither term and keeps to the edges.

Recognition — design point 16, a new drawing drawing a crowd — is kept out of the spatial query on
purpose. See [decisions.md](decisions.md) D-006.

## Feeding

```
IPointerSource.Pressed ──> AquariumManager.HandlePointerPressed
                             ├─ near the surface ──> FoodField.Emit × FoodPerPinch
                             └─ anywhere else ────> RippleField.Emit

AquariumManager.Update
  ├─ FoodField.Tick            sink towards the floor, retire stale flakes
  ├─ fish tick ──> CalculateFoodAttraction ──> FoodField.SubmitClaim   (claims gather)
  └─ ResolveFoodClaims ──────> FoodField.ResolveClaims                 (winners eat)

FoodRenderer.LateUpdate ──> FoodField.TryGetParticle    presentation only, never writes
```

A press near the surface feeds and **deliberately does not also ripple**: scattering the shoal away
from food the visitor just dropped would be the opposite of the gesture's purpose.

**Claiming is two-phase and has to be.** Fish stake claims as they tick; winners are resolved only
once every fish has had its say. Consuming a flake the moment the first fish touched it would award it
by list order — an ordering the visitor cannot see — and `aggression`, the trait meant to decide it,
would never be consulted.

Aggression reaches feeding through two channels, and the second is not optional: see
[decisions.md](decisions.md) D-007 for why winning contests alone left the trait doing nothing
measurable.

`FoodField` is a plain class, like `RippleField`, so the whole feeding model is unit-testable without
a scene. `FoodRenderer` is a separate MonoBehaviour that only reads it, so turning the visuals off
could never change how fish behave.

`AquariumBounds` is the logical swimming area in world units. Nothing in the fish code reads screen
pixels, so every aspect ratio and a mid-session window resize all behave identically; a resize
raises `BoundsChanged` and every fish re-aims.

## Texture lifetime

`FishSpriteCache` is a reference-counted store keyed by source path (folder fish) or capture id
(socket fish). Two fish from one drawing share a `Texture2D`; it is destroyed when the last fish
using it is gone. `FishTextureLoader` owns decode and destruction, and destroys the texture behind
a sprite as well as the sprite — skipping that is the classic runtime-loading leak.

## Configuration

`AquariumConfig` is a `ScriptableObject` and the single source of truth for every tunable value.
`AppConfig` optionally overrides it at startup from `aquarium_settings.json` next to the executable.
`GameBootstrap` applies both, validates the camera is orthographic, and switches the debug overlay
off outside Development mode.
