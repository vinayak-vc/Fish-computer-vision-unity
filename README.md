# Fish Aquarium

A standalone Unity aquarium that watches a folder for transparent fish PNGs and
brings each one to life as a swimming fish. The capture side (camera, OpenCV,
segmentation) is a separate application and is deliberately not part of this
project: Unity's contract starts at "a PNG appeared on disk".

```
fish_001.png -> watcher -> queue -> texture loader -> factory -> aquarium
```

## Running it

Open `Scenes/Aquarium.unity` and press Play. In Development mode the aquarium
loads the sample drawings from `Assets/StreamingAssets/Sample/` so you can work
on it without the capture application running.

Drop a `.png` into the watched folder while it is running and a fish appears
within a second, no restart needed.

## The watched folder

Configured in `Settings/AquariumConfig.asset` under **Fish Input Folder**:

| Setting | Meaning |
| --- | --- |
| `Input Folder Root` | `StreamingAssets`, `PersistentData` or `Absolute` |
| `Input Folder Relative Path` | Appended to the root. Default `FishInput` |
| `Input Folder Absolute Path` | Used only when the root is `Absolute` |

No machine-specific path is compiled in. The default resolves to
`Assets/StreamingAssets/FishInput` in the Editor and
`<Build>_Data/StreamingAssets/FishInput` in a player.

An installed machine can be re-pointed without a rebuild by dropping
`aquarium_settings.json` next to the executable:

```json
{
  "inputFolder": "C:/FishCapture/processed",
  "maxFishCount": 40,
  "runtimeMode": "Production",
  "screenWidth": 1920,
  "screenHeight": 1080
}
```

### Folder contract

- Only `*.png` is read; every other extension is ignored.
- A file is ingested once per session, tracked by path in a `HashSet`.
- A file is left alone until its size stops changing and it can be opened
  exclusively with a valid PNG header, so a half-written capture never becomes
  a broken fish. Files that never settle are abandoned after a timeout with a
  warning rather than retried forever.
- Deleting a PNG does **not** remove the fish it created. Enable
  `Remove Fish When Source File Deleted` if you want deletions mirrored.

## Development and production modes

`Runtime Mode` on the config switches behaviour:

| | Development | Production |
| --- | --- | --- |
| Debug overlay + hotkeys | on | off (component disabled) |
| Sample fish loaded at startup | yes | no |
| Fullscreen | no | yes, borderless |
| Cursor | visible | hidden |

Both modes target 60 FPS with vsync and set `Application.runInBackground`, so
an unattended installation keeps swimming when it loses focus.

### Hotkeys (development only)

| Key | Action |
| --- | --- |
| F1 | Toggle the debug overlay |
| F2 | Spawn one fish from the sample folder |
| F3 | Clear the aquarium and forget which files were ingested |
| F4 | Rescan the input folder |
| ESC | Quit |

F3 followed by F4 reloads every PNG in the folder, which is the quickest way to
see a change to the artwork.

## Architecture

Each stage does one job and hands off to the next, so a change to how files
arrive never touches how fish swim.

```
FishFileWatcher  (background thread, reports paths only)
      |
FishLoadQueue    (thread-safe, dedupe, wait-for-complete-file)
      |
FishSpriteCache  (ref-counted PNG -> Texture2D -> Sprite)
      |
FishFactory      (normalise size, roll behaviour, choose entry point)
      |
AquariumManager  (population, max-count policy, one tick for all fish)
      |
FishController   (lifecycle, spawn and removal animation)
      |
FishMovement / FishAnimator
```

| Folder | Contents |
| --- | --- |
| `Scripts/Core` | `AquariumConfig`, `AppConfig`, `AquariumManager`, `GameBootstrap`, `PathUtility`, `FloatRange`, `IFishIngestService` |
| `Scripts/Fish` | Loading, factory, data model, movement, steering maths, animation, depth, sprite cache |
| `Scripts/Input` | Watcher, scanner, queue, file-readiness checks, ingest service |
| `Scripts/Aquarium` | Bounds, background, decor, plants, bubbles, light rays, procedural sprites |
| `Scripts/Debug` | `DebugOverlay` |
| `Tests/EditMode` | 57 edit-mode tests |

### Notes on a few deliberate choices

**One Update for the whole shoal.** `AquariumManager.Update` ticks every fish.
No `FishController`, `FishMovement` or `FishAnimator` has an `Update` of its
own, which keeps the per-frame cost flat as the population grows.

**Steering maths is separated from the component.** `FishSteering` is pure
static maths with no component state, so turning, target selection, wall
avoidance and separation are all unit tested without a scene.

**Textures are reference counted.** `FishSpriteCache` keys sprites by source
path. Thirty fish spawned from three drawings hold three textures, and a
texture is destroyed only when the last fish using it is gone. This is what
lets the app run for hours without leaking.

**Nothing touches the artwork.** The PNG bytes become an uncompressed RGBA32
texture with a full-rect sprite and a centred pivot. No recolouring, no
filtering, no outline generation, no mipmaps. Scaling is always uniform, so a
800x400 drawing stays 2:1 on screen.

**Never upside down.** Direction is expressed as a horizontal `flipX` plus a
tilt clamped to `Max Pitch Angle`, mirrored when the sprite is flipped, so a
fish swimming up and to the left still points its nose up.

**Which way the drawing faces.** `Artwork Faces Right` on the config describes
the *source PNG*, not the swimming direction. The sample drawings have their
nose pointing left, so it is **off** by default. Tick it only if your capture
starts producing right-facing drawings. Getting it wrong is easy to spot: every
fish swims tail first. `FishSteering.ShouldFlipHorizontally` holds the rule and
is unit tested against both orientations.

**The background stays out of the way.** Water, plants, rocks, bubbles and
light rays are all generated procedurally at runtime, so the scene ships with
no art dependencies, and they are deliberately dark and low contrast so the
user's drawing is the brightest thing on screen.

## Configuration

Everything tunable lives on `Settings/AquariumConfig.asset`: input folder and
file handling, population limits and overflow policy, fish size normalisation,
speed / turn / acceleration / idle ranges, noise, separation, orientation,
swim animation, depth influence and debug defaults. Values that vary per fish
are `FloatRange` pairs, rolled once per fish at spawn, so no two fish move
alike. Nothing is hard-coded in the behaviour scripts.

## Tests

Edit-mode tests live in `Tests/EditMode` and run from Window > General > Test
Runner, covering PNG loading and transparency, folder scanning, duplicate
protection, incomplete-file detection, size normalisation and aspect
preservation, aquarium bounds, depth mapping, and the movement rules.

## Extension points

The structure leaves room for the things that were intentionally left out of
this first version:

- **Mesh deformation.** `FishAnimator` drives the visual child transform only.
  A mesh-based tail wave can replace its body without touching movement.
- **Personality.** `FishData` already carries a per-fish behaviour profile;
  a personality is a different way of filling it in.
- **Fish interaction.** `AquariumManager.CalculateSeparation` is the hook where
  schooling, following or avoidance rules would go.
- **Caustics.** `WaterEffect` owns the light-shaft renderers and is where a
  scrolling caustics material would be layered in.
