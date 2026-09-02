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
own: the manager rebuilds a position snapshot once per frame, then ticks every `FishController`,
which forwards to `FishMovement` and `FishAnimator`. Neighbour queries read the snapshot, so
separation costs one pass over an array rather than a physics query per fish.

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
