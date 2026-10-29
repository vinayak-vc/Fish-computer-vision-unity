# Roadmap

Milestone order and completion criteria come from [unity_handoff_prompt.md](unity_handoff_prompt.md).
Every milestone must end with something runnable on screen.

Status: **done** / **in progress** / **not started**.

---

## Baseline (pre-existing, before this roadmap)

Socket.IO connection, PNG decode off the main thread, sprite creation, a swimming fish with
wander, wall avoidance and separation, aquarium bounds that follow the camera, bubbles, plants,
decor, a debug overlay, and a folder-watching ingest path. Edit-mode tests for the pure logic.

---

## M0 — Harden the ingest — **done**

Prerequisite. Small.

- [x] Deduplicate by `id`; a repeat arrival is discarded silently
- [x] Honour `replay: true`: accept the fish, suppress the birth animation
- [x] Survive a null `features` / `personality` / `anatomy`
- [x] Survive an unknown `schema_version` — log loudly, keep working
- [x] Reconnect automatically with backoff when Python restarts

**Done when** killing and restarting Python leaves the tank unchanged, with no duplicates and no
animation storm.

## M1 — Trait-driven locomotion — **done** — *points 3, 6, 23*

`speed` to max velocity and acceleration, `grace` to turn-rate limit and steering damping,
`preferred_depth` to a vertical home band, `speed` and `grace` together to tail-beat amplitude and
frequency. All through Inspector-exposed curves.

- [x] Schema v2 wire types: `features`, `personality`, `anatomy`, `object_type`
- [x] `FishTraits` + deterministic fallback (see [decisions.md](decisions.md) D-002)
- [x] Every use of `Random` removed from the fish identity path
- [x] Trait response curves on `AquariumConfig`
- [x] On-screen size from `foreground_area`, not from texture dimensions

**Done when** twenty fish drawn by twenty people are visibly distinguishable in motion with the
sprites hidden — silhouettes only.

## M2 — Pointer interaction — **done** — *points 1, 8, 12, 13, 19*

- [x] `IPointerSource` abstraction; mouse is one implementation, a hand tracker would be another
- [x] Approach or flee, weighted by `curiosity` against `fear`
- [x] Fast pointer movement startles; the threshold scales with `fear`
- [x] Click emits a ripple, `F = strength / distance`, decaying over a couple of seconds
- [x] A click also leaves a temporary danger zone the fish route around
- [x] Slow sustained proximity raises a per-fish `affection`; high affection inverts flee to approach

**Done when** a first-time visitor works out cause and effect within about five seconds of moving
the mouse, without being told.

## M3 — Flocking and recognition — **done** — *points 4, 16*

- [x] `FishSpatialHash`, a uniform grid rebuilt each frame, replacing the O(n²) sweep
- [x] Alignment and cohesion in `FishFlocking`, both weighted by `social` through response curves
- [x] Separation kept as it was, and folded into the same single neighbour query
- [x] `maxNeighboursConsidered` caps the per-fish cost when a shoal clumps
- [x] A newly spawned fish draws curious neighbours for a few seconds; replays draw nobody

**Done when** high-`social` fish form visible schools while low-`social` fish stay at the edges,
with 200 agents holding 60 fps.

Measured in play mode. Sixteen fish differing **only** in `social`, all at the same `preferred_depth`
and with curiosity and fear at zero so nothing else could explain the result:

| | `social` 0.98 | `social` 0.00 |
|---|---|---|
| spread about own centroid | 1.33 | 4.23 |
| mean nearest cohort-mate | 0.88 | 1.43 |
| heading agreement (1.0 = unison) | 0.86 | 0.29 |

The social cohort holds together three times tighter and swims near-unison; the solitary cohort
scatters across the tank and each fish goes its own way.

Recognition, measured the same way: a fish with `curiosity` 1.0 placed three units from a newly
arrived fish feels a force of 2.087 aimed exactly at it (dot 1.000), while a fish with `curiosity` 0.0
at the identical point feels nothing at all.

Performance: the flocking pass costs **0.671 ms per frame with 200 live fish**, 4.0% of a 60 fps
budget. Details and the hash-versus-sweep scaling in [decisions.md](decisions.md) D-006.

One caveat on the frame rate: the app caps itself at 60 fps with vSync, and the editor Game view
paces its own repaints, so end-to-end fps in the editor could not be pushed past the cap to reveal
total headroom. What is measured is the cost this milestone *adds*. **Confirm the full frame budget in
a player build before an installation runs at 200.**

Two things left as judgement calls rather than changes:

- A fish only picks swim targets inside its own `preferred_depth` band, so two highly social fish
  with very different preferred depths cannot school tightly. Cohesion pulls them together while
  depth homing pulls back, and depth is the stronger by default. Schools form *within* a water layer.
- `MaxFishCount` is still 30. The system handles 200 — that was measured by raising it temporarily and
  putting it back — but how full the tank should be is an installation decision.

## M4 — Feeding — **done** — *point 2*

- [x] `FoodField`, pooled and fixed-capacity, ticked from `AquariumManager.Update`
- [x] A press near the surface drops a pinch of food; a press lower down still sends a ripple
- [x] Flakes sink, come to rest on the floor of the swimming area, and go stale
- [x] Fish steer towards food weighted by `curiosity`, and food wakes an idle fish
- [x] `aggression` decides who wins a flake, and how fast a fish takes its next mouthful
- [x] `FoodRenderer` draws them, pooled the same way `BubbleSystem` is

**Done when** dropping food reliably pulls a crowd, and the aggressive fish visibly win.

Both measured in play mode, with twelve fish identical in every trait but `aggression`.

**The crowd.** Mean distance from the shoal to a central drop point beforehand: 4.80. After the drop,
mean distance to the food itself: **1.29**, with **9 of 12 fish within 2 units** and 11 of 12 within 4.

**The winners.**

| | aggression 0.95 | aggression 0.05 |
|---|---|---|
| flakes eaten, per fish | **6.2** | **1.5** |
| seconds between mouthfuls | 0.36 | 1.44 |

Timid fish still eat — every one of the six ate at least one flake — so they lose out rather than
starve, which is the outcome that reads as competition rather than as a bug.

The first build of this measured **backwards**, and getting from there to here needed a second channel
for aggression. Worth reading before touching feeding: [decisions.md](decisions.md) D-007.

## M5 — Persistence — **done** — *points 24, 28, 29*

- [x] `AquariumPersistenceService`: the roster, written atomically to `persistentDataPath`
- [x] `FishArtworkCache`: a local copy of every drawing, because Python replays only its ten most recent
- [x] Capture ids seeded back into **both** ingest services on load
- [x] Traits saved rather than re-derived, so a fish does not come back a stranger
- [x] Position, affection, age and food eaten all restored
- [x] Restores are silent: no entrance animation, no birth moment, no crowd
- [x] `CaptureCalendar`: the "created today" tally, with a tested midnight rollover
- [x] A birth moment on live arrival only

**Done when** the tank comes back exactly as it was after a full machine restart, with Python off.

Measured. Five fish with distinct traits were saved, play mode was stopped and restarted, and all five
came back with their speeds (0.10–0.90), aggressions, mask areas and the tally (5 today, sequence 104)
intact, `state=Swimming`, `celebrating=False`, `novelty=0.00`.

Then the part that matters most: Python's replay burst of those same five ids was injected, and **all
five were dropped as duplicates** — 5 fish, not 10, with `spawnedThisSession=0`. That is the M0
regression this milestone could most easily have caused, and it does not.

## M6 — World systems — **done** — *points 11, 14, 15, 17, 18, 20, 25*

- [x] `AquariumEnergy`: one 0..1 mood driving fish speed, bubble density and the water's brightness
- [x] `AquariumFlow`: slow eddies plus a prevailing drift, as a pure function of position and time
- [x] `AquariumCycle`: a compressed day/night cycle and scheduled storms that ramp in and out
- [x] `AquariumPredator`: a rare procedural silhouette that crosses the tank and scatters everything
- [x] Departing fish swim off sideways as they fade, rather than dissolving in place

**Done when** the tank is worth watching for two minutes with nobody touching it.

Measured. Four captures lifted energy 0.35 → 1.00, the fish speed multiplier 1.11 → 1.30 and the
bubble interval 0.282 s → 0.182 s. Currents differ across the tank and roughen under a storm. A
predator mid-crossing produced panic 0.33 in a fearless fish and 0.66 in a fearful one, both fleeing
exactly away from it (dot 1.00), and nothing at all from 30 units off.

**The microphone is not built.** The brief marks it optional and needing nothing from Python; it is the
one piece of M6 left, and it is noted in [tasks.md](tasks.md) rather than silently dropped.

## M7 — Social bonds — **done** — *points 5, 8*

- [x] `FishBondRegistry`: familiarity per pair, tracked lazily and decaying, so company must be sustained
- [x] A fish keeps one partner; a pair broods; a brood lays an egg
- [x] `EggField`: pooled eggs that carry the blend, so neither parent need still exist at hatching
- [x] `FishTraitBlend`: a noisy average, deterministic per brood, with rarity inherited undrifted
- [x] A baby wears a parent's drawing tinted with the blend of both parents' Python-measured colours
- [x] Babies are never saved — they were never drawn, and there is no artwork to rebuild one from

## M8 — Non-fish drawings — **done** — *points 21, 22*

- [x] `FishBehaviourProfile`: what each `object_type` does, as flags rather than five subclasses
- [x] Plants and rocks anchor to the floor; plants sway, rocks do not
- [x] Jellyfish drift slowly, pulse vertically, and ignore flocking
- [x] Creatures are predator-eligible, and frighten nearby fish when aggressive enough
- [x] `object_confidence` below 0.5 keeps the behaviour but skips the type-specific effect

Measured in one tank holding one of every type. Plant and rock sat on the floor at y = −2.15 against a
floor of −3.00; an unrecognised `"kraken"` parsed to `Unknown` and swam normally; a jellyfish glowed and
did not school. The confidence gate: a confident aggressive creature scored threat 0.75, and a
**guessed** one scored 0.00 — a misclassification does not empty the tank.

## M9 — Rarity and anatomy — **done, with one part deferred** — *points 7, 26*

- [x] `FishRarityProfile`: glow from the continuous score, trails gated on the bucketed tier
- [x] A louder birth moment for a rarer drawing
- [x] `FishAnatomyLandmarks`: the top-left to bottom-left **y flip**, in exactly one place, tested
- [x] Eye highlights, from Python's landmark when present and from geometry otherwise
- [x] The tail landmark resolved and carried, ready to pin the existing tail beat to

Measured: a legendary drawing had a trail and glowed (sprite 1.68 against a 1.07 baseline) while every
common one had neither.

**Tail-bone deformation is not built.** The `anatomy` block is `null` on every payload today — Python
phase 2, and the contract says it may stay null — so there is no way to see whether a deformation looks
right, and guessing at it would be work to redo against the first real landmarks. The flip and the
fallbacks it needs are done and tested, and the tail point is already resolved and carried, so this is
a change in one place when the block ships. Noted in [tasks.md](tasks.md).

## M10 — Drawing challenges — **blocked, deliberately not built** — *point 27*

Section 10 of [interaction_contract.md](interaction_contract.md) lists `challenge_set` and
`challenge_result` as **"reserved, not yet implemented"** and says in terms: *"Do not build against
these yet."* The handoff brief repeats it. So nothing here is written, and no Unity-to-Python send path
exists beyond the Socket.IO handshake.

The names are claimed in the contract so neither side invents a conflicting one. When the contract says
they are live, this is the milestone to pick up.

## Standing non-goals

- No image analysis in Unity. Ever.
- No re-randomising of traits.
- No person, hand, face or pose tracking — the pointer is the only input.
- The socket is not required: with Python unreachable the tank must still run and reconnect quietly.
- Never write to `captures/` — that directory belongs to Python.
