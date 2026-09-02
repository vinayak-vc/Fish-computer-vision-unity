# Tasks

Live task list. Completed work is kept, briefly, so the next agent can see what was already tried.

---

## Done — M0, harden the ingest

- [x] Deduplicate by `id`. `FishCaptureRegistry`, claimed on the decode worker before the base64 pass.
- [x] Duplicate arrivals are discarded silently, and counted for the debug overlay.
- [x] Survive a null `features` / `personality` / `anatomy` block.
- [x] Schema version tolerance: newer than this build logs loudly, older is the documented legacy case
      and is only noted.
- [x] `expectedSchemaVersion` default moved to 2.
- [x] `replay: true` already suppressed the birth animation; verified, not rewritten.
- [x] Reconnect with backoff already existed in `BestHttpSocketIoTransport`; verified, not rewritten.

## Done — M1, trait-driven locomotion

- [x] Schema v2 wire types: `FishFeatures`, `FishPersonality`, `FishAnatomy`, `FishDominantColor`.
- [x] `FishTraits`, immutable, plus `FishObjectType` and `FishRarityTier`.
- [x] `StableHash` and `DeterministicRandom`, the algorithms Python must mirror.
- [x] `FishTraitFallback` and `FishTraitResolver`.
- [x] Every `UnityEngine.Random` call removed from the fish identity path.
- [x] Trait response curves on `AquariumConfig`, with `OnValidate` repair for curves that deserialise
      empty and a runtime guard for players, where `OnValidate` never runs.
- [x] On-screen size from `foreground_area` rather than texture dimensions.
- [x] Vertical home band from `preferred_depth`, kept distinct from parallax depth.
- [x] `SocketPayloadSelfTest` upgraded to emit schema v2 with a spread of personalities, plus an F7
      key that emits a legacy v1 payload to exercise the fallback.

## Done — M2, pointer interaction

- [x] `IPointerSource`, with `MousePointerSource` as the only implementation.
- [x] `PointerInfluence`, pure and unit tested: falloff, comfort, startle threshold, steering.
- [x] `RippleField`: click shockwaves, `F = strength / distance`, decaying, fixed capacity.
- [x] Approach or flee weighted by curiosity against fear, with personal space on top.
- [x] Startle on fast pointer movement, threshold scaled by fear, with a speed and acceleration boost.
- [x] Click danger zones that fish route around when picking a new swim target.
- [x] Per-fish affection from slow sustained proximity; high affection inverts flee into approach.
- [x] `MousePointerSource` added to the `AquariumManager` object in `Aquarium.unity` and wired.

---

## Next — M3, flocking and recognition

1. **Spatial hash for neighbour queries.** `AquariumManager.CalculateSeparation` is currently an O(n²)
   sweep over the frame snapshot. It is fine at the configured cap of 30 but the milestone calls for
   200 agents at 60 fps, so this is the first thing to change and it should be a pure, testable class
   in `Scripts/Fish/` alongside `FishSteering`.
2. **Boids.** Separation already exists. Add alignment and cohesion, both weighted by `FishTraits.Social`,
   through response curves on `AquariumConfig` in the same style as the M1 block.
3. **Curious neighbours.** A newly spawned fish draws nearby fish for a few seconds, weighted by
   `Curiosity`. `AquariumManager.FishSpawned` already fires the event this needs.

**Acceptance:** high-`social` fish form visible schools while low-`social` fish stay at the edges, with
200 agents holding 60 fps.

## Then — M4 feeding, M5 persistence

M5 has one piece of groundwork already waiting for it: `FishMovement.Affection` is a live per-fish
value with no way to restore a saved one. A `RestoreAffection` setter was deliberately left out rather
than shipped unused, and is the first thing M5 will want.

M5 also needs a decision on where the capture registry lives. Today `FishPayloadDecodeQueue` owns one
for the session; persistence means the registry has to survive a restart, or a restarted Unity will
re-admit every fish in Python's replay window as a brand new arrival on top of the ones it restored
from disk.

---

## Carried over, not blocking

- **`AquariumConfig.asset` has never been re-serialised.** Every field added since it was authored -
  the whole socket block, and now the whole trait, depth and pointer blocks - lives only as a C# field
  initialiser. Verified working, because Unity runs initialisers before applying serialised data. It
  does mean a default changed in code silently changes the installation's behaviour, which is worth
  knowing before someone edits one.
- **The mock publisher** the handoff brief mentions still does not exist on the Python side. Until it
  does, `SocketPayloadSelfTest` (F5 live, F6 replay burst, F7 legacy) is the way to get a spread of
  personalities into the tank without a camera.
- **Anatomy is parsed but unused.** The top-left to bottom-left y flip is not written. See M9.
- **`Assets/StreamingAssets/Sample/` is empty.** `SocketPayloadSelfTest` needs at least one PNG there
  or F5, F6 and F7 log "the sample folder contains no PNGs" and do nothing. Predates this session;
  any transparent PNG will do.
