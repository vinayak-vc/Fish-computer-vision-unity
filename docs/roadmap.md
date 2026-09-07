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

## M5 — Persistence — **not started** — *points 24, 28, 29*

Unity owns all runtime state. Save id, traits, position, affection, relationships and age to
`Application.persistentDataPath`. Cache the decoded PNG locally so a restart does not depend on
Python's replay window. Plus a one-to-two-second birth moment on live arrival, and a "created
today" counter driven by `sequence` and `timestamp`.

**Done when** the tank comes back exactly as it was after a full machine restart, with Python off.

## M6 — World systems — **not started** — *points 11, 14, 15, 17, 18, 20, 25*

A single global `aquariumEnergy` (0..1) from pointer activity and recent capture rate, feeding
speed, bubble density, lighting and particle count. Then a flow field, a day/night and storm cycle,
a rare procedural predator, and a lifecycle where the oldest fish swim off and fade at the
population cap. Microphone input is optional and needs nothing from Python.

**Done when** the tank is worth watching for two minutes with nobody touching it.

## M7 — Social bonds — **not started** — *points 5, 8*

Sustained proximity forms a pair; a pair produces an egg; the egg hatches a small fish whose colour
and scale are blended from the parents and whose traits are a noisy average. Unity-side only —
Python never sees a baby.

## M8 — Non-fish drawings — **not started** — *points 21, 22*

`object_type`: plants and rocks anchor to the floor, jellyfish drift and pulse and ignore flocking,
creatures are predator-eligible. `object_confidence < 0.5` still spawns the named type but skips
type-specific VFX. A misclassification must be survivable, never fatal.

Groundwork done in M1: `FishObjectType` is parsed, carried on `FishTraits` and surfaced in the
debug overlay. Nothing yet branches on it.

## M9 — Rarity and anatomy — **not started** — *points 7, 26*

`rarity_tier` gates glow, trails and a louder birth moment. When Python's `anatomy` block ships,
pin eye highlights and tail-bone deformation to the landmarks.

Groundwork done in M1: `rarity_tier` is parsed onto `FishTraits`, and the `anatomy` block is parsed
and covered by wire tests for both its null and its populated shapes. Nothing consumes either yet —
in particular the top-left to bottom-left y flip is **not** written, and is the first thing M9 needs.

## M10 — Drawing challenges — **blocked** — *point 27*

Blocked on the reverse channel, [interaction_contract.md](interaction_contract.md) section 10.
Do not build against `challenge_set` / `challenge_result` until the contract says they are live.

---

## Standing non-goals

- No image analysis in Unity. Ever.
- No re-randomising of traits.
- No person, hand, face or pose tracking — the pointer is the only input.
- The socket is not required: with Python unreachable the tank must still run and reconnect quietly.
- Never write to `captures/` — that directory belongs to Python.
