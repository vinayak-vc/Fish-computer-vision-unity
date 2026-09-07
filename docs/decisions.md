# Decisions

Newest first. Each entry records what was decided, why, and what it costs.

---

## D-007 — Aggression needs a channel that always applies, not just contested flakes

**Decision.** Aggression decides two things about feeding, not one: who wins a flake two fish both
reach, *and* how long a fish waits between mouthfuls (`aggressionEatCooldownRange`, 1.5 s at
aggression 0 down to 0.3 s at aggression 1).

**Why.** Winning contests alone did not work, and the way it failed is worth recording because it
looked fine from the outside.

Built as the brief literally describes it — curiosity steers, aggression breaks ties — the first
measurement came out **backwards**: twelve fish identical but for aggression, and the timid ones ate
8.5 flakes each against the aggressive ones' 0.5. The claim logic was correct and its unit tests
passed. The real cause was that **contests almost never happened.** Each fish independently targets
its own nearest flake, so a shoal spreads itself across a pile rather than converging on one crumb.
With no contest, aggression never entered the calculation at all, and the outcome fell to arrival
order — three lucky fish ate 53 of the 54 flakes between them.

So the trait had no observable effect, and the milestone's criterion — "the aggressive fish visibly
win" — could not be met by the literal reading. A per-fish cooldown scaled by aggression fixes both
problems at once: it stops any single fish hoovering a pile, and it gives aggression a channel that
applies on every mouthful rather than only in the rare contested case. Re-measured: **6.2 flakes per
aggressive fish against 1.5 per timid one**, and every timid fish still ate at least one, so they lose
out rather than starve.

**Cost.** A reading of "compete for them weighted by aggression" slightly broader than the brief's
wording. It is still competition — a pushy fish taking more turns at a shared pile is what
competition looks like when the pile is big enough that nobody has to fight over a single crumb.

**The general lesson**, worth keeping for M6 and M7: a trait wired only into a rare branch is
indistinguishable from a trait wired into nothing. Measure whether the branch is actually taken.

---

## D-006 — One neighbour query per fish, serving separation, alignment, cohesion and recognition

**Decision.** `AquariumManager.CalculateSeparation` is gone, replaced by `CalculateFlocking`, which runs
a single `FishSpatialHash` query per fish and derives all three boid rules from it. New-arrival
recognition is deliberately *not* part of that query.

**Why one query.** The separation radius (1.0) is much smaller than the neighbour radius (2.6), so a
query at the wider of the two already contains every fish either rule needs. Filtering by squared
distance inside the loop costs one comparison; a second query would cost another grid traversal.

**Why the grid at all.** The old sweep was O(n²) across a frame. At the authored ceiling of thirty
fish that was genuinely cheaper than a grid, and the old comment said so honestly. The milestone asks
for two hundred, where it is not.

Worth being precise about the size of the win, because it is smaller than "O(n²) to O(n)" suggests:
the tank is small relative to the neighbour radius, so a 3×3 cell block already covers a good fraction
of it. The real property is that cost now scales with *local density* rather than with total
population, and `maxNeighboursConsidered` caps it outright when a shoal clumps.

Two measurements, and they answer different questions. Both were taken under Mono, so a player build
on IL2CPP should do better.

**Scaling — the query itself against the sweep it replaced**, uniformly distributed fish in a 20×7.5
tank, broad phase plus the squared-distance test and nothing else:

| population | hash | sweep | speedup |
|---|---|---|---|
| 30 | 0.006 ms | 0.007 ms | 1.1× |
| 100 | 0.036 ms | 0.081 ms | 2.3× |
| 200 | 0.089 ms | 0.338 ms | 3.8× |
| 400 | 0.186 ms | 1.366 ms | 7.3× |

The hash roughly doubles as the population doubles while the sweep roughly quadruples, which is the
scaling this change was for. Note the first row: at thirty fish the difference is within noise, so the
old code's comment claiming a sweep beat a grid at that ceiling was accurate. This only earns its
place at the populations the milestone asks for.

**Actual cost — the whole `CalculateFlocking` pass**, measured in the running scene with 200 real
fish at their real clustered positions:

> **0.671 ms per frame, 4.0% of a 60 fps budget, 3.4 µs per fish.**

That is the number to quote. It is 7½ times the 0.089 ms above because the real pass also evaluates
two `AnimationCurve`s per fish, accumulates alignment and cohesion, calls
`FishSteering.SeparationContribution`, walks the novel list, and works on clumped rather than uniform
positions — clumping means more candidates survive to the narrow phase. The micro-benchmark isolates
the data structure; this measures the feature.

**Why recognition is separate.** Design point 16 wants a new arrival noticed from further away than a
schoolmate — the novelty radius is 5.0 against a neighbour radius of 2.6. Routing it through the same
query would force the grid's cell size up to 5.0, which on a 20×7.5 tank leaves about ten cells and
makes every ordinary neighbour query nearly a full sweep. Instead, novel fish are collected into a
small list during the snapshot rebuild and walked directly. There are usually none, and never more
than a handful, so the list is cheaper than any index over it would be.

**Cost.** `CalculateSeparation` was public API, so this breaks AGENTS.md rule 10. Its only caller was
`FishMovement`, which this milestone changes anyway, and leaving a wrapper would have left an unused
abstraction — rule 11. The rebuild also allocates on the first few frames, then never again.

---

## D-005 — `preferred_depth` and parallax `Depth` are kept as separate axes

**Decision.** The contract's `preferred_depth` (0 surface, 1 floor) becomes `FishTraits.PreferredDepth`
and drives a **vertical home band** in world space. The pre-existing `FishData.Depth` keeps its
meaning: **parallax**, 0 nearest the viewer and 1 furthest, driving scale, opacity, sorting order
and a small speed multiplier.

**Why.** They are different axes and mapping one onto the other would produce an aquarium where
every surface-dwelling fish is also drawn in front, which is both wrong and conspicuous. The names
are close enough that this needed writing down.

**Cost.** Two similarly named values on `FishData`. Mitigated by doc comments on both.

---

## D-004 — Parallax depth, Perlin seed and animation phase are derived from the identity hash

**Decision.** `FishData` no longer calls `UnityEngine.Random` for anything. `Depth`, `NoiseSeed` and
`AnimationPhase` come from the same deterministic stream as the fallback traits.

**Why.** Contract section 6: the same drawing must behave the same way on every restart. A fish
whose wander pattern or draw order was re-rolled at startup would break that promise just as
visibly as a re-rolled trait, because the wander pattern *is* how a fish reads on screen.

**Cost.** Removes `FishData.ApplyRandomisedProfile`, which was public API. Replaced by
`ApplyTraitProfile(AquariumConfig, FishTraits)`. The handoff brief's explicit non-goal "no
re-randomising of traits" is the instruction that authorises this break.

---

## D-003 — Duplicate captures are rejected on the worker thread, before the base64 decode

**Decision.** `FishCaptureRegistry` is backed by a `ConcurrentDictionary` and `TryClaim(id)` is
called from the decode worker, immediately after the JSON parse and before
`Convert.FromBase64String`.

**Why.** Contract section 8 requires dedupe by `id`. The id is only known after parsing, so the
check cannot happen when the raw text arrives. Doing it on the worker means a duplicate costs one
parse instead of a parse plus a megabyte of base64 decode plus a texture upload.

**A claim is permanent for the session, even if the fish is then refused.** If the aquarium is at
its cap and `FishOverflowStrategy.RejectNew` turns the fish away, the id stays claimed. Dedupe
answers "have I already processed this capture", not "is this fish currently alive"; re-admitting a
capture the tank already considered and declined would make the replay burst behave differently
depending on how full the tank happened to be.

**Cost.** The registry grows for the lifetime of the process. At one entry per capture and an id of
roughly 30 characters, an installation running for a month at a hundred drawings a day costs a few
hundred kilobytes. `ResetProcessedFileCache` clears it for development.

---

## D-002 — Traits and the deterministic fallback: FNV-1a 64 into SplitMix64

**Decision.** When `personality` is absent or null, every missing trait is drawn from a
deterministic stream seeded by the capture id. Specified here exactly, because
[interaction_contract.md](interaction_contract.md) section 9 states the rule ("stable 64-bit hash of
`id`", "deterministic uniform(0,1) drawn from that seed") but not the algorithm, and both sides must
agree for an archived fish to behave the same as a live one.

**Seed — FNV-1a, 64 bit, over the UTF-8 bytes of `id`:**

```
hash = 0xCBF29CE484222325
for each byte b of utf8(id):
    hash = hash XOR b
    hash = hash * 0x100000001B3        (unchecked, wrapping)
```

**Stream — SplitMix64, one draw per value:**

```
state = seed
next():
    state = state + 0x9E3779B97F4A7C15         (wrapping)
    z = state
    z = (z XOR (z >> 30)) * 0xBF58476D1CE4E5B9 (wrapping)
    z = (z XOR (z >> 27)) * 0x94D049BB133111EB (wrapping)
    z = z XOR (z >> 31)
    return z

uniform01():  return (next() >> 11) * (1.0 / 9007199254740992.0)      // 2^-53
```

**Draw order is fixed and must not be reordered**, because changing it changes every legacy fish:

```
speed, curiosity, fear, social, aggression, grace, preferred_depth, rarity_score
```

Values the contract does not cover but which must also be stable — parallax depth, Perlin seed,
animation phase, size roll, in that order — come from a **separate, domain-separated stream** seeded
by `id + "|presentation"` rather than from a continuation of the one above. Two reasons:

- **No correlation.** Sharing one stream would tie a fish's swimming depth to its speed, because both
  would come off adjacent draws of the same generator. Deterministic, but it would look like a bug.
- **Python never needs it.** These are Unity's values under the ownership split, so keeping them off
  the shared stream means the Python side only has to reproduce the eight traits above.

**Why not a constant.** Every legacy fish would become an identical clone.
**Why not `Random.value`.** The same fish would have a new character on every restart.
**Why not `string.GetHashCode()`.** It is not stable across .NET versions, across processes with
randomised hashing, or across languages. Python could never reproduce it.

**Cost.** Python must implement the same two functions to reproduce Unity's fallback. It only has
to for captures where it did not send a `personality` block, which is exactly the legacy case.

---

## D-001 — Payload parsing stays on BestHTTP's LitJson rather than moving to Newtonsoft.Json

**Decision.** Keep `BestHTTP.JSON.LitJson.JsonMapper` for schema v2, including the nested `features`,
`personality` and `anatomy` blocks and the `dominant_colors` list.

**Why.** [unity_handoff_prompt.md](unity_handoff_prompt.md) recommends Newtonsoft, but its stated
reason is that *`JsonUtility`* cannot handle nested objects, nullable blocks or the colour list. That
is true of `JsonUtility` and not of LitJson. Checked against the bundled source
(`Assets/Best HTTP/Source/JSON/LitJson/`):

- `JsonReader.skip_non_members` defaults to `true`, so unknown keys are skipped, not thrown on —
  which contract section 3 rule 1 requires.
- `ReadValue` returns `null` for a JSON null against any class type, so `"anatomy": null` and a
  missing `personality` both deserialise cleanly.
- Base importers cover `int -> float` and `double -> float`, so the numeric fields map without a
  custom converter.
- Nested objects, `T[]` and `List<T>` are all handled.

Against that, moving to Newtonsoft would mean adding a second JSON stack and an asmdef reference,
and would gain nothing: the reason parsing is fast here is `RawFishPayloadParser`, which lifts the
event argument out as raw text so a worker can parse it, and that mechanism is library-agnostic.
AGENTS.md rule 8 ("reuse existing systems instead of creating duplicates") and rule 14's priority
order settle it.

**Cost.** LitJson maps by exact member name, so the wire types must keep snake_case fields. That is
already the established pattern in `FishCaptureMessage` and is contained entirely within
`Scripts/Network/`. `com.unity.nuget.newtonsoft-json` remains in the manifest for other packages.

**Revisit if.** The contract grows a field LitJson cannot express — a dictionary with arbitrary keys,
or polymorphism.
