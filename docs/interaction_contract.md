# Interaction Contract — Python ⇄ Unity

The single source of truth for what crosses the wire. **Both sides read this file.**
Change it in a commit that touches both sides, or not at all.

Companion documents: [architecture.md](architecture.md) for the Python pipeline,
[unity_handoff_prompt.md](unity_handoff_prompt.md) for the Unity brief.

---

## 1. The division of ownership

> **Python owns the birth certificate. Unity owns the life.**

| | Python | Unity |
|---|---|---|
| Reads camera, segments the drawing | yes | no |
| Derives shape / colour / complexity features | yes | no |
| Derives personality traits from those features | yes | no |
| Classifies what was drawn (fish / plant / rock …) | yes | no |
| Decides what a trait *means* in motion | no | yes |
| Steering, boids, food, currents, mood, VFX | no | yes |
| Position, velocity, hunger, age, affection, pairs | no | yes |
| Persists runtime state across restarts | no | yes |
| Persists the artwork + birth certificate | yes | no |

Two consequences worth stating outright, because they are the mistakes this split exists
to prevent:

1. **Unity must never re-derive traits from the PNG.** Python has the segmentation mask
   and the contours in memory; Unity has a flattened image. Recomputing in Unity is
   slower, less accurate, and would drift out of agreement with the archived metadata.
2. **Python must never send behaviour.** It sends `speed: 0.81`, never `4.2 m/s`. Units,
   curves and tuning live in the Unity Inspector where an artist can see them change.

---

## 2. Transport

Python runs the **server**. Unity connects as a **client**.

| | Value | Config key |
|---|---|---|
| Protocol | Socket.IO (WebSocket, falls back to long polling) | |
| URL | `http://<host>:8765/socket.io/` | `publish.host`, `publish.port` |
| Namespace | `/` | `publish.namespace` |
| Event (Python to Unity) | `fish_captured` | `publish.event` |
| Payload | one JSON object, PNG inline as base64 | |

`publish.host: 0.0.0.0` allows Unity on another machine. **There is no authentication on
this server** — anything that can reach the port receives every drawing. Trusted network
only; set it back to `127.0.0.1` when Unity runs on the same machine.

The Python startup log prints the exact URLs to point Unity at. Use those; do not guess
the LAN address.

---

## 3. Payload — schema version 2

```jsonc
{
  // ---- identity -------------------------------------------------------
  "id": "fish_20260901_182346_001",   // primary key. Unique, sortable, stable forever
  "timestamp": "2026-09-01T18:23:46", // local time, ISO 8601, no timezone suffix
  "sequence": 1,                      // monotonic counter within the output directory
  "schema_version": 2,

  // ---- artwork --------------------------------------------------------
  "png_base64": "iVBORw0…",           // transparent PNG, no data: prefix, no newlines
  "format": "png",
  "width": 983,                       // pixels actually encoded in png_base64
  "height": 686,
  "file_resolution": [983, 686],      // the full-resolution file on disk

  // ---- provenance (rarely needed by Unity) ----------------------------
  "bounding_box":       {"x": 0, "y": 112, "width": 983, "height": 686},
  "bounding_box_frame": {"x": 0, "y": 112, "width": 983, "height": 686},
  "source_resolution": [1280, 960],
  "foreground_area": 311227,
  "processing_method": "opencv",
  "confidence": 0.0932,               // segmenter self-assessment, NOT a classifier score

  // ---- NEW in v2: what was drawn --------------------------------------
  "object_type": "fish",              // see section 5
  "object_confidence": 0.81,

  "features": {                       // see section 4
    "aspect_ratio": 1.86,             // bbox width / height, > 1 = wide
    "circularity": 0.41,              // 4πA/P², 1.0 = perfect circle
    "solidity": 0.78,                 // area / convex-hull area, 1.0 = no concavities
    "extent": 0.62,                   // area / bbox area
    "elongation": 0.71,               // 1 - (minor/major PCA axis), 0..1
    "orientation_deg": -12.4,         // major axis vs +x, -90..90, image coords
    "perimeter": 2840.5,              // pixels
    "complexity": 34.2,               // P²/(4πA), 1.0 = circle, grows with raggedness
    "vertex_count": 46,               // approxPolyDP at 1% of perimeter
    "edge_density": 0.19,             // Canny-positive fraction inside the mask, 0..1
    "symmetry": 0.72,                 // IoU of the mask against its own mirror
    "ink_coverage": 0.31,             // mask fraction darker than the paper model
    "stroke_width": 4.2,              // pixels, mean distance-transform ridge value
    "spikiness": 0.55,                // convexity-defect count × depth, 0..1. Fin proxy
    "color_count": 3,                 // Lab k-means clusters above 5% coverage
    "dominant_colors": [              // descending by ratio, at most 5
      {"hex": "#2f6fb8", "ratio": 0.44},
      {"hex": "#e8d24a", "ratio": 0.21},
      {"hex": "#1b1b1b", "ratio": 0.09}
    ],
    "mean_hue": 208,                  // degrees, 0..359. Undefined when mean_sat is low
    "mean_sat": 0.61,                 // 0..1
    "mean_val": 0.48,                 // 0..1
    "colorfulness": 38.4              // Hasler-Süsstrunk metric, roughly 0..110
  },

  "personality": {                    // see section 6. EVERY field is 0..1 inclusive
    "speed": 0.81,
    "curiosity": 0.90,
    "fear": 0.22,
    "social": 0.68,
    "aggression": 0.14,
    "grace": 0.55,
    "preferred_depth": 0.40,          // 0 = surface, 1 = floor
    "rarity_score": 0.12,
    "rarity_tier": "common"           // common | uncommon | rare | legendary
  },

  "anatomy": null,                    // see section 7. null until phase 2. May stay null

  // ---- replay flag ----------------------------------------------------
  "replay": true                      // present ONLY on catch-up sends. See section 8
}
```

### Rules Unity must follow

1. **Ignore unknown fields.** New keys will be added without a version bump.
2. **Tolerate missing optional blocks.** `features`, `personality`, `anatomy` and
   `object_type` may be absent or `null` — see section 9 for the fallback.
3. **Do not parse `id` for meaning.** It is opaque. Use `timestamp` and `sequence`.
4. `schema_version` increments only on a **breaking** change. Unity should log loudly and
   keep working if it sees a version above the one it knows.

---

## 4. Feature semantics

All features are measured on the **segmentation mask and the masked RGBA**, in image
coordinates (origin top-left, +y down), before any Unity-side scaling.

| Feature | Range | Reads high when… |
|---|---|---|
| `aspect_ratio` | > 0 | drawing is wide (a long eel-like fish) |
| `circularity` | 0..1 | outline is smooth and round (a pufferfish, a rock) |
| `solidity` | 0..1 | no deep notches — a blob rather than a fin-covered silhouette |
| `extent` | 0..1 | the shape fills its bounding box |
| `elongation` | 0..1 | one axis dominates |
| `orientation_deg` | -90..90 | — |
| `complexity` | >= 1 | outline is long relative to area: ragged, spiky, detailed |
| `vertex_count` | >= 3 | many distinct corners after simplification |
| `edge_density` | 0..1 | lots of internal detail — scales, patterns, stripes |
| `symmetry` | 0..1 | the two halves match about the long axis |
| `ink_coverage` | 0..1 | heavily filled in rather than outline-only |
| `stroke_width` | px | thick marker rather than fine pencil |
| `spikiness` | 0..1 | many deep concavities — large fins, spines, tentacles |
| `color_count` | 1..5 | several distinct colours used |
| `colorfulness` | ~0..110 | saturated and varied, not a graphite sketch |

`mean_hue` is circular and **undefined when `mean_sat < 0.15`**. Guard before using it.

---

## 5. `object_type`

| Value | Unity should spawn |
|---|---|
| `fish` | swimmer, full behaviour set |
| `plant` | anchored to the floor, sways, no steering |
| `rock` | anchored to the floor, static, becomes an obstacle |
| `jellyfish` | slow drifter, vertical pulse, ignores flocking |
| `creature` | swimmer, but eligible to be a predator |
| `unknown` | treat as `fish` |

`object_confidence` below **0.5** means the classifier is guessing. Unity should still
spawn the named type — a wrong plant is a better outcome than a rejected drawing — but
may skip type-specific VFX.

Version 1 of the classifier is a **heuristic** over aspect ratio, verticality, green-hue
fraction, solidity and position on the sheet. It will be wrong sometimes. Design the
Unity side so a misclassification is survivable, never fatal.

---

## 6. Personality semantics

Every value is `0..1`, clamped, and **deterministic**: derived from the capture's
192-bit perceptual hash together with its features. The same drawing photographed twice
produces the same personality. **Unity must not add its own randomness on top** — that
would break the promise the whole feature rests on, that *your* drawing behaves like
*your* drawing.

| Trait | 0.0 means | 1.0 means | Suggested Unity use |
|---|---|---|---|
| `speed` | sluggish | darting | max velocity, acceleration multiplier |
| `curiosity` | ignores the pointer | investigates everything | attraction weight to pointer, food, new arrivals |
| `fear` | unflappable | panics easily | repulsion weight, flee radius, startle threshold |
| `social` | solitary | schools tightly | boid cohesion + alignment weight |
| `aggression` | never chases | chases and nips | food competition, predator eligibility |
| `grace` | jerky, twitchy | smooth, gliding | steering damping, turn-rate limit, tail amplitude |
| `preferred_depth` | hugs the surface | hugs the floor | vertical home band |
| `rarity_score` | ordinary | extraordinary | gate for glow, trails, a birth fanfare |

`curiosity` and `fear` come from different features and are **not** complements. A fish
can be both curious and fearful — it approaches, then bolts. That is a good fish.

`rarity_tier` is a pre-bucketed convenience so Unity does not hardcode thresholds:

| Tier | `rarity_score` |
|---|---|
| `common` | < 0.55 |
| `uncommon` | 0.55 – 0.79 |
| `rare` | 0.80 – 0.94 |
| `legendary` | >= 0.95 |

---

## 7. Anatomy landmarks (phase 2 — may remain `null`)

```jsonc
"anatomy": {
  "confidence": 0.6,                                  // 0..1, overall
  "eyes": [{"x": 0.21, "y": 0.34, "r": 0.04}],        // 0..2 entries
  "head": {"x": 0.12, "y": 0.40},
  "tail": {"x": 0.93, "y": 0.46},
  "fins": [{"x": 0.48, "y": 0.78}]                    // 0..4 entries
}
```

**Coordinate convention — read this twice.** All values are normalised `0..1` against the
PNG, **origin top-left, +x right, +y down** (OpenCV convention).

Unity textures have their origin at the **bottom-left**. Converting to a UV or to a local
sprite offset requires flipping y:

```csharp
Vector2 uv = new Vector2(a.x, 1f - a.y);
```

Getting this wrong puts every eye on the fish's belly, and the symptom looks like a
detection failure rather than a coordinate bug.

**Treat every landmark as optional.** Children's drawings defeat landmark detection
routinely. When `anatomy` is `null`, or `confidence < 0.4`, or a list is empty, Unity
falls back to geometric defaults (head = leading end of the major axis, eye at 20% along
it). The fallback must look acceptable, because it will be used often.

---

## 8. Replay on connect

When Unity connects, Python re-sends the most recent `publish.replay_last` captures
(default 10), rebuilt from the metadata sidecars on disk, each carrying `"replay": true`.

This exists because a socket cannot cover the one case that matters in an unattended
installation: Unity was down when someone drew a fish.

Unity must:

- **deduplicate by `id`** — a replayed fish it already holds is discarded silently;
- **suppress the birth moment** for `replay: true` payloads, or a restart fires ten
  celebration animations at once;
- accept them in the order received (oldest first).

---

## 9. Backwards compatibility and fallbacks

`captures/processed/*.json` sidecars written before v2 exist on disk and **will** be
replayed. Unity must handle a payload with `schema_version: 1` and no `features` or
`personality` block.

Fallback rule, implemented identically on both sides so an archived fish behaves the same
as a live one:

```
seed              = stable 64-bit hash of `id`
each missing trait = deterministic uniform(0,1) drawn from that seed
object_type       = "fish"
rarity_tier       = "common"
```

Never fall back to a constant — every legacy fish would become an identical clone. Never
fall back to `Random.value` — the fish would change personality on every restart.

---

## 10. Reverse channel — reserved, not yet implemented

Unity to Python events. **Do not build against these yet.** Listed so the names are
claimed and neither side invents a conflicting one.

| Event | Direction | Purpose |
|---|---|---|
| `unity_hello` | U to P | `{client, version, schema_max}` — lets Python log what is attached and down-convert if ever needed |
| `fish_state` | U to P | periodic roster snapshot, if the archive is ever moved to Python |
| `challenge_set` | U to P | announce the active drawing challenge |
| `challenge_result` | P to U | Python's verdict on whether the last capture met it |

Until these ship the socket is one-way, and Unity needs no send path beyond the Socket.IO
handshake.

---

## 11. Testing without a camera

```bash
python main.py --source image --input samples/fish.jpg
```

Serves the socket and republishes the same drawing on a loop, which is enough to bring up
the Unity connection path. For personality *variety*, the mock publisher (planned — see
[tasks.md](tasks.md)) emits a spread of synthetic v2 payloads on a timer without needing
any image at all.

[tools/socketio_test_client.py](../tools/socketio_test_client.py) is a *Python client*.
Use it to confirm the server emits what this document says, and to prove whether a
delivery problem sits in Python or in Unity.
