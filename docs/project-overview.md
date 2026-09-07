# Project Overview — Fish Aquarium (Unity side)

## What this is

The Unity half of an interactive art installation. A visitor draws a fish on paper. A Python
application watching that paper through an overhead camera segments the drawing, computes its
features and personality, and pushes a transparent PNG plus metadata to Unity over Socket.IO.
Unity turns that drawing into a living creature in a shared aquarium.

The wire format between the two halves is defined in [interaction_contract.md](interaction_contract.md),
which is authoritative. The Unity brief is [unity_handoff_prompt.md](unity_handoff_prompt.md).

## Division of ownership

> Python owns the birth certificate. Unity owns the life.

Python: camera, segmentation, shape/colour features, personality traits, object classification,
archival of artwork and metadata.

Unity: what a trait *means* in motion, steering, boids, food, currents, mood, VFX, position,
velocity, age, affection, pairing, and persistence of all runtime state.

Two rules follow, and every design decision in this project defers to them:

1. **Unity never re-derives traits from the PNG.** Python holds the segmentation mask; Unity has a
   flattened image. Anything recomputed here would be worse and would disagree with the archive.
2. **Unity never re-randomises a trait.** The same drawing must always produce the same creature,
   so that a child can say "that one is mine, it swims like that because of how I drew it".

## Hard constraints from the client

- **No person tracking, no hand tracking.** Wherever a design calls for a hand or a body, the
  mouse pointer stands in. Every interaction goes through `IPointerSource` so a hand tracker can
  be substituted later without touching behaviour code.
- **No computer vision in Unity. Ever.**

## Two ingest sources

The aquarium accepts fish from two independent sources, and both can run at once:

| Source | Component | Identity | Traits |
|---|---|---|---|
| Socket.IO capture station | `SocketFishIngestService` | payload `id` | from the `personality` block, or the deterministic fallback |
| Watched folder of PNGs | `FishIngestService` | absolute file path | always the deterministic fallback, seeded from the file name |

Both hand their result to the same `FishFactory`, so population limits, texture lifetime and
swimming behaviour have exactly one implementation.

## Runtime modes

`AquariumConfig.RuntimeMode` selects Development (debug overlay, hotkeys, windowed) or Production
(unattended, fullscreen, no overlay). An installation can override the config without a rebuild by
dropping `aquarium_settings.json` next to the executable — see `AppConfig`.

## Where to start reading

1. [architecture.md](architecture.md) — how the Unity side is put together
2. [interaction_contract.md](interaction_contract.md) — sections 3, 6, 8, 9 above all
3. [roadmap.md](roadmap.md) — the milestone order, and what is built so far
4. [ai_handoff.md](ai_handoff.md) — current state and the next recommended task
