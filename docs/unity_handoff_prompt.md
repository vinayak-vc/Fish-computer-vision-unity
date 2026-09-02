# Unity Handoff — Brief for the Aquarium Side

Paste the whole of this file to whoever (or whatever) builds the Unity half. It is
written to be self-contained.

The authoritative wire format lives in [interaction_contract.md](interaction_contract.md).
Where this brief and the contract disagree, **the contract wins**.

---

## The prompt

> You are building the Unity half of an interactive art installation.
>
> **What already exists (do not rebuild it).** A Python application watches a sheet of
> paper through an overhead camera. When someone draws a fish and steps back, it detects
> the drawing, cuts it out of the paper, and pushes a transparent PNG plus a metadata
> block to Unity over Socket.IO. That side is finished and is not yours to change. Unity
> already connects, receives the PNG, and spawns a swimming sprite.
>
> **What you are building.** Turning that fish slideshow into an ecosystem. Three layers
> of interaction, in this order of importance:
>
> 1. the fish reacts to the person (via the **mouse pointer**),
> 2. the fish reacts to other fish,
> 3. the aquarium reacts to its environment.
>
> **Two hard constraints, from the client.**
>
> - **There is no person tracking and no hand tracking.** Anywhere a design calls for a
>   hand, a body, or an audience position, use the **mouse pointer** instead. Build every
>   interaction through one `IPointerSource` interface so that a future hand tracker can
>   be substituted without touching any behaviour code.
> - **You do not do computer vision.** Every property of a drawing — its shape, colour,
>   complexity, personality, what kind of creature it is — arrives from Python in the
>   payload, already computed. Never re-derive them from the texture. Python holds the
>   segmentation mask; you only have a flattened image, so anything you computed would be
>   both worse and inconsistent with the archive.
>
> **The contract you code against** is `docs/interaction_contract.md`. Read sections 3, 6,
> 8 and 9 before writing a line. The short version:
>
> - one event, `fish_captured`, on namespace `/`, at `http://<host>:8765/socket.io/`;
> - the payload carries `id`, `png_base64`, a `features` block of raw measurements, and a
>   `personality` block of eight traits, each normalised `0..1`;
> - Python sends **traits, never behaviour**. `speed: 0.81` is your input; what that means
>   in metres per second is your decision and belongs in an Inspector-exposed curve;
> - traits are **deterministic** — the same drawing always yields the same personality.
>   Never add randomness on top of a trait. That determinism is the whole point: a child
>   must be able to say "that one is mine, it swims like that because of how I drew it".
>
> Build in the milestone order below. Every milestone must end with something runnable on
> screen.

---

## Deserialisation

`JsonUtility` cannot handle the nested objects, the optional/nullable blocks, or the
`dominant_colors` list cleanly. Use **Newtonsoft.Json** (`com.unity.nuget.newtonsoft-json`,
available in the Unity registry).

```csharp
[Serializable]
public sealed class FishPayload
{
    public string id;
    public string timestamp;
    public int    sequence;
    public int    schema_version;

    public string png_base64;
    public int    width, height;

    public string      object_type;        // may be null -> "fish"
    public float       object_confidence;
    public Features    features;           // may be null -> see fallback
    public Personality personality;        // may be null -> see fallback
    public Anatomy     anatomy;            // null until Python phase 2
    public bool        replay;             // absent on live captures -> false
}

[Serializable]
public sealed class Personality
{
    public float speed, curiosity, fear, social;
    public float aggression, grace, preferred_depth, rarity_score;
    public string rarity_tier;             // common | uncommon | rare | legendary
}
```

`Features` and `Anatomy` mirror sections 4 and 7 of the contract.

**Fallback (contract section 9).** When `personality` is null — legacy captures replayed
from disk will do this — derive every trait from a stable 64-bit hash of `id`, not from
`Random.value` and not from a constant. A constant clones every legacy fish; `Random`
gives the same fish a new character on every restart.

---

## Texture handling

- `png_base64` is raw base64, **no `data:` prefix and no newlines**.
- Typical size is around 983 × 686. Decode the base64 on a worker thread;
  `Texture2D.LoadImage` must run on the main thread and will hitch if you do several in
  one frame. Queue arrivals and decode at most one per frame.
- Set the sprite pivot to the **alpha centroid**, not the bounding-box centre, or the fish
  will rotate about a point outside its own body.
- Cap the on-screen size from `foreground_area`, not from the texture dimensions — a fish
  drawn small on the paper should stay small in the tank. That is design point 6 and it is
  free.

---

## Milestones

Each milestone lists the original design points it covers, and what must be true before
it counts as done.

### M0 — Harden the ingest *(prerequisite, small)*

- Deduplicate by `id`; a repeat arrival is discarded silently.
- Honour `replay: true`: accept the fish, **suppress the birth animation**. Without this a
  restart fires ten celebrations at once.
- Survive a null `features` / `personality` / `anatomy` and an unknown `schema_version`.
- Reconnect automatically with backoff when Python restarts.

**Done when** killing and restarting Python leaves the tank unchanged, with no duplicates
and no animation storm.

### M1 — Trait-driven locomotion — *points 3, 6, 23*

One `FishAgent` reading `Personality` into Inspector-exposed curves: `speed` to max
velocity and acceleration, `grace` to turn-rate limit and steering damping,
`preferred_depth` to a vertical home band, `speed` and `grace` together to tail-beat
amplitude and frequency.

**Done when** twenty fish drawn by twenty people are visibly distinguishable in motion
with the sprites hidden — silhouettes only.

### M2 — Pointer interaction — *points 1, 8, 12, 13, 19*

All of these behind `IPointerSource` so a hand tracker can replace the mouse later.

- Approach or flee the pointer, weighted by `curiosity` against `fear`.
- **Fast pointer movement startles**; the threshold scales with `fear`.
- Click emits a ripple — a radial force field, `F = strength / distance`, decaying over a
  couple of seconds — and a temporary danger zone the fish route around.
- Slow, sustained proximity raises a per-fish `affection` value that persists (M5). High
  affection inverts the flee response into an approach.

**Done when** a first-time visitor works out cause and effect within about five seconds of
moving the mouse, without being told.

### M3 — Flocking and recognition — *points 4, 16*

Standard boids — separation, alignment, cohesion — with cohesion and alignment weighted by
`social`. Spatial hash for neighbour queries; do not do an O(n²) sweep, the tank is meant
to hold hundreds. A newly spawned fish draws curious neighbours (weighted by `curiosity`)
for a few seconds.

**Done when** high-`social` fish form visible schools while low-`social` fish stay at the
edges, with 200 agents holding 60 fps.

### M4 — Feeding — *point 2*

Pointer gesture near the surface drops food particles; particles sink; fish steer toward
them weighted by `curiosity` and compete for them weighted by `aggression`.

**Done when** dropping food reliably pulls a crowd, and the aggressive fish visibly win.

### M5 — Persistence — *points 24, 28, 29*

**Unity owns all runtime state.** Python owns only the artwork and the birth certificate.
Save to `Application.persistentDataPath`: id, traits, position, affection, relationships,
age. Cache the decoded PNG locally so a restart does not depend on Python's replay window.

Also: a one-to-two-second birth moment on live arrival (never on replay), and a
"created today" counter driven by `sequence` and `timestamp`.

**Done when** the tank comes back exactly as it was after a full machine restart, with
Python switched off.

### M6 — World systems — *points 11, 14, 15, 17, 18, 20, 25*

A single global `aquariumEnergy` (0..1) driven by pointer activity and recent capture
rate, feeding fish speed, bubble density, lighting and particle count. Then a flow field
for currents and migration; a day/night and storm cycle; a rare procedural predator; and
a lifecycle where the oldest fish eventually swim off and fade when the population cap is
reached.

Microphone is optional and entirely yours — Unity's `Microphone` API, RMS to
`aquariumEnergy`, an onset detector for a clap that scatters everything. This needs
nothing from Python.

**Done when** the tank is worth watching for two minutes with nobody touching it.

### M7 — Social bonds — *points 5, 8*

Fish that spend sustained time near each other form a pair; a pair eventually produces an
egg; the egg hatches a small fish whose colour and scale are blended from the parents'
sprites and whose traits are a noisy average of theirs. All of this is Unity-side — Python
never sees a baby, and babies are not archived captures.

### M8 — Non-fish drawings — *points 21, 22*

Read `object_type`: `fish`, `plant`, `rock`, `jellyfish`, `creature`, `unknown`. Plants and
rocks anchor to the floor; jellyfish drift and pulse and ignore flocking; creatures are
eligible to be predators. `object_confidence < 0.5` means a guess — still spawn the named
type, but skip type-specific VFX. **A misclassification must be survivable, never fatal.**

### M9 — Rarity and anatomy — *points 7, 26*

`rarity_tier` gates glow, trails and a louder birth moment. When Python's `anatomy` block
ships, pin eye highlights and tail-bone deformation to the landmarks.

**Coordinate warning.** Landmarks are normalised `0..1` with the origin at the **top-left**
(OpenCV). Unity textures start at the **bottom-left**. Flip: `uv.y = 1f - a.y`. Getting
this wrong puts every eye on the fish's belly and looks like a detection failure rather
than a coordinate bug. Landmarks are optional and often absent — the geometric fallback
(head at the leading end of the major axis, eye 20% along) must look acceptable, because
it will be used often.

### M10 — Drawing challenges — *point 27*

Blocked on the reverse channel (contract section 10). Unity displays the prompt and emits
`challenge_set`; Python measures the next capture against it and returns
`challenge_result`. Do not build against these event names until the contract says they
are live.

---

## Explicit non-goals

- No image analysis in Unity. Ever. It comes from Python.
- No re-randomising of traits.
- No person, hand, face or pose tracking — the pointer is the only input.
- Do not treat the socket as required: if Python is unreachable, the tank must still run
  from its local cache and reconnect quietly when Python returns.
- Do not write to `captures/` — that directory is Python's, and a consumer writing into it
  will collide with the capture loop.

---

## Running Python for development

No camera needed:

```bash
python main.py --source image --input samples/fish.jpg
```

That serves the socket and republishes the same drawing on a loop — enough to bring up the
connection path. For a spread of *different* personalities, use the mock publisher (see
[tasks.md](tasks.md)) once it lands.

The startup log prints the exact URL to point Unity at. Use it rather than guessing the
LAN address. If `publish.host` is `0.0.0.0`, Windows will raise a firewall prompt on the
first run — allow it for Private networks, or remote machines get a connection timeout.
