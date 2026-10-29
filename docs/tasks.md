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

## Done — M5, persistence

- [x] `AquariumPersistenceService`, `AquariumSaveState`, `FishArtworkCache`, `CaptureCalendar`.
- [x] The registry question from the old plan is answered: `FishCaptureRegistry` is now bounded and its
      ids are saved and seeded back into both ingest services. Verified by restart — see the roadmap.
- [x] `FishMovement.RestoreAffection` written, and now has the caller it was waiting for.
- [x] Birth moment on live arrival, never on a replay or a restore.

## Done — M6, world systems

- [x] `AquariumEnergy`, `AquariumFlow`, `AquariumCycle`, `AquariumPredator`, `PredatorRenderer`.
- [x] Energy wired into fish speed, bubble density and the water's tint.
- [x] Departing fish swim off sideways as they fade.

## Done — M7, social bonds

- [x] `FishBondRegistry`, `EggField`, `EggRenderer`, `FishTraitBlend`, `FishFactory.CreateBaby`.

## Done — M8, non-fish drawings

- [x] `FishBehaviourProfile` and the movement branches for anchored, pulsing and predatory types.

## Done — M9, rarity and anatomy

- [x] `FishRarityProfile`, `FishAnatomyLandmarks`, eye highlights, glow, trails, louder births.

---

## What is left

Four things, all deliberate, none of them blocking an installation.

1. **M10, drawing challenges.** Blocked by the contract itself, which says in terms not to build
   against `challenge_set` / `challenge_result` yet. Do not start until section 10 says they are live.
2. **Tail-bone deformation, M9.** Waits on Python's `anatomy` block, which is `null` on every payload
   today. The flip, the fallbacks and the resolved tail point are all in place, so this is a change in
   one place. See [decisions.md](decisions.md) D-010.
3. **The microphone, M6.** The brief marks it optional and it needs nothing from Python: Unity's
   `Microphone` API, RMS into `AquariumEnergy` — which already takes an arbitrary 0..1 stimulus, so
   this is a second caller of `Tick`, not a new system — plus an onset detector for a clap that could
   call `AquariumPredator.Appear` or emit a ripple at the tank's centre.
4. **A player-build frame check at 200 fish.** The flocking pass is measured at 0.671 ms, but the
   editor caps itself at 60 fps with vSync so total headroom could not be measured. Confirm in a build
   before an installation runs with `maxFishCount` raised.

## Also worth knowing

- **`MaxFishCount` is still 30.** Everything scales to 200; how full the tank should be is an
  installation decision, not one to make from here.
- **Babies do not survive a restart**, by design. [decisions.md](decisions.md) D-009.
- **A restored fish's position comes back exactly**, but it then swims off immediately, so watching the
  tank a few seconds after a restart will not look identical. The save is faithful; fish are not static.

## Carried over, not blocking

- **`AquariumConfig.asset` has never been re-serialised.** Every field added since it was authored -
  the socket block, and every block added since - trait response, depth, pointer, feeding,
  energy, currents, weather, predator, persistence, bonds, object types and rarity - lives only as a
  C# field
  initialiser. Verified working, because Unity runs initialisers before applying serialised data. It
  does mean a default changed in code silently changes the installation's behaviour, which is worth
  knowing before someone edits one.
- **The mock publisher** the handoff brief mentions still does not exist on the Python side. Until it
  does, `SocketPayloadSelfTest` (F5 live, F6 replay burst, F7 legacy) is the way to get a spread of
  personalities into the tank without a camera.
- **`Assets/StreamingAssets/Sample/` is empty.** `SocketPayloadSelfTest` needs at least one PNG there
  or F5, F6 and F7 log "the sample folder contains no PNGs" and do nothing. Predates this session;
  any transparent PNG will do.
