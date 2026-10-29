# Architecture — Unity side

Assembly: `FishAquarium` (`Scripts/FishAquarium.asmdef`), root namespace `ViitorCloud.FishAquarium`,
referencing only `BestHTTP`. Tests live in `FishAquarium.EditModeTests`, Editor-only.

## Layer map

```
Scripts/
  Core/        configuration, app lifecycle, the population manager, shared value types
  Network/     Socket.IO transport, wire types, the off-thread decode queue
  Input/       folder-watching ingest, and the pointer abstraction
  Fish/        one creature: data, traits, steering, movement, animation, texture lifetime,
               artwork cache, social bonds, eggs, behaviour and rarity profiles
  Aquarium/    the world: bounds, background, decor, bubbles, water, food, currents,
               weather, the predator, and the renderers for each
  Debug/       development-only overlay and self-tests
```

The dependency rule that matters is the one about the socket: **nothing in `Core` knows the socket
exists.** `Network` and `Input` know `Core` and `Fish`; `Core` reaches an ingest source only through
`IFishIngestService`, which is why persistence can seed capture ids without importing the transport.

`Core` and `Fish` do reference each other - `AquariumManager` drives `FishController`, and
`FishController` reads `AquariumBounds`. That is deliberate: the manager is the hub every fish talks
to, and splitting it would mean an interface per direction for no gain inside a single assembly.

Two placement rules worth knowing, both learned the hard way this session. A type that consumes wire
types belongs in `Network` (so `FishTraitResolver` and `FishAnatomyLandmarks` live there). A type that
needs `FishTraits` belongs in `Fish`, not `Aquarium` - which is why `EggField` sits beside `FoodField`'s
sibling rather than next to it.

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
| `aggression` | who wins a flake, and the cadence between mouthfuls |
| `social` | boid alignment and cohesion weight |
| `rarity_score` | steady glow |
| `rarity_tier` | trails, and how loud the birth moment is |

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

## Persistence

```
AquariumPersistenceService
  |- Start  --> Load: read aquarium_state.json, seed capture ids, rebuild fish
  |- Update --> Save every saveIntervalSeconds
  +- OnApplicationQuit --> Save

persistentDataPath/
  |- aquarium_state.json     roster, known capture ids, the created-today tally
  +- artwork/<id>.png        FishArtworkCache: one file per drawing
```

Three things have to be saved together, and leaving any one out produces a different visible failure:
the **roster** (or fish come back as strangers), the **artwork** (or nothing older than Python's
ten-capture replay window can come back at all), and the **capture ids** (or the aquarium restores its
fish and then admits Python's replay burst as new arrivals, duplicating the tank on every restart).

Both writes are atomic - temporary file, then move - because an installation loses power mid-write
sooner or later, and a half-written save that parses to nothing would lose everything.

`FishCaptureRegistry` is bounded rather than unbounded, which is what makes saving it viable. Eviction
is safe because Python only ever replays its most recent captures; anything evicted is far older than
that. Babies are never saved - see [decisions.md](decisions.md) D-009.

## World systems

```
AquariumManager.Update
  +- TickWorldSystems      before any fish reads them, so the whole tank agrees
       |- AquariumCycle    time of day, scheduled storms
       |- AquariumPredator rare crossing, threat ramping in and out
       +- AquariumEnergy   one 0..1 mood from pointer speed and arriving drawings

read by: FishMovement (speed, current, panic), BubbleSystem (density), AquariumBackground (tint)
```

`AquariumEnergy` is deliberately one number. Design point 11 asks for speed, bubbles, lighting and
particle count to move together, and a single mood is what makes the aquarium swell and settle as one
thing rather than as four unrelated effects.

`AquariumFlow` is a pure function of position and time with no state at all, so every fish samples the
current without anyone owning a grid, nothing is rebuilt per frame, and a current can never be the
reason two runs of the same drawing diverge.

## Social bonds

```
AquariumManager.TickSocialBonds        after the fish have moved
  |- AccrueFamiliarity --> FishBondRegistry.RecordProximity   nearest neighbour only
  |- FishBondRegistry.Tick -----------> pairs ready to lay
  |- TryLayEgg -----> FishTraitBlend -> EggField.TryLay
  +- HatchEggs -----> FishFactory.CreateBaby
```

Company is credited for **nearest neighbours only**. A fish in the middle of a school is not keeping
company with all six of its neighbours, and crediting every pair in a crowd would pair the whole tank
within a minute of the first school forming.

An egg carries the blended traits rather than looking its parents up at hatching time, so a brood is
never orphaned by a parent being removed to make room.

## Object types

`FishBehaviourProfile` maps each `object_type` to flags - anchors, swims, flocks, pulses, sways,
predator-eligible - rather than to a subclass. Every type shares the movement code and differs only in
which parts apply.

The rule underneath every branch is the contract's own: **a misclassification must be survivable,
never fatal.** An unrecognised type behaves as a fish, a plant that was really a fish sits on the floor
and sways, and nothing here can reject a drawing. `object_confidence` below 0.5 keeps the behaviour and
skips the decorative effect, so a wrong guess costs a glow rather than a creature.

## Rarity and anatomy

Both of the contract's rarity values are used, for different things: the continuous `rarity_score`
drives the glow so brightness rises smoothly, and the pre-bucketed `rarity_tier` gates the trail so the
score thresholds stay in Python's hands.

`FishAnatomyLandmarks` holds the **top-left to bottom-left y flip** in exactly one function. That is
the whole reason the class exists: the contract warns twice that getting it wrong puts every eye on the
fish's belly and looks like a detection failure rather than a coordinate bug - so it gets investigated
in Python, where nothing is wrong.

The geometric fallback is not an edge case. `anatomy` is null on every payload today and may stay null,
so the fallback is the path that actually runs, and it has to look acceptable rather than merely not
crash.

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
