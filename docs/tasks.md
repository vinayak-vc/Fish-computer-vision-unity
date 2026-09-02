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

## Done — M3, flocking and recognition

- [x] `FishSpatialHash`: uniform grid, counting sort, rebuilt each frame, allocation-free once warm.
- [x] `FishFlocking`: alignment and cohesion as pure functions returning directions, never magnitudes.
- [x] `AquariumManager.CalculateFlocking` replaces `CalculateSeparation` and serves all three boid
      rules from one query.
- [x] Social response curves on `AquariumConfig`, so a solitary fish gets no alignment or cohesion.
- [x] New-arrival recognition via a per-fish novelty that decays over `noveltySeconds`; replayed
      captures are never novel, for the same reason they skip the birth animation.
- [x] `FishController.AgeSeconds`, which M5 and M6 will both want.

- [x] Verified: 214 edit-mode tests pass, schooling and recognition measured in play mode, and the
      flocking pass costs 0.671 ms at 200 live fish. Figures in [roadmap.md](roadmap.md) under M3.

## Done — M4, feeding

- [x] `FoodField`: pooled, sinking, staling, with two-phase claim resolution.
- [x] `FoodRenderer` and `ProceduralSpriteLibrary.GetPellet`, so food is actually visible.
- [x] Surface press feeds, deeper press ripples, and the two never both fire.
- [x] Food wakes an idle fish, so a resting one does not hover next to a meal it never takes.
- [x] `aggression` gates both who wins a flake and the cadence between mouthfuls — see
      [decisions.md](decisions.md) D-007, which explains why the first version measured backwards.
- [x] `FishController.FoodEaten` and a debug-overlay line, because who is winning is not something
      you can judge by watching a tank.
- [x] Verified: 235 edit-mode tests pass; crowd and competition measured in play mode.

## Next — M5, persistence

Design points 24, 28, 29. Unity owns all runtime state; Python owns only the artwork and the birth
certificate. Save to `Application.persistentDataPath`.

**Acceptance:** the tank comes back exactly as it was after a full machine restart, with Python off.

1. **Decide the capture-registry question first.** It is the one part with a real design choice in it.
   `FishPayloadDecodeQueue` holds a `FishCaptureRegistry` for the session only. If it stays that way, a
   restarted Unity will restore its fish from disk *and then* re-admit every id in Python's replay
   window as a brand new arrival — duplicating the tank on every restart, which is exactly the failure
   M0 exists to prevent. The registry has to be persisted alongside the fish, or keyed off what was
   restored.
2. **Cache the decoded PNG locally.** Contract section 8's replay window is ten captures; anything
   older is gone. Without a local cache, a tank of fifty fish cannot come back.
3. **Persist per fish:** id, traits, position, `Affection`, `AgeSeconds`, `FoodEaten`, and later
   relationships. `FishMovement.Affection` and `FishController.AgeSeconds` are already live values with
   no way to restore them; a `RestoreAffection` setter was deliberately left unwritten rather than
   shipped unused, and this is where it lands.
4. **Restore silently.** A restored fish is exactly the replay case that already exists: no birth
   animation, and no novelty, or a restart will have the whole tank swarm the fish it just reloaded.
5. **Then the birth moment and the "created today" counter,** driven by `sequence` and `timestamp`.

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
