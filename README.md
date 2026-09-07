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

## Socket.IO capture feed

The aquarium takes fish from two independent sources. The folder watcher above reads PNGs from disk;
this one receives them from the Python capture station over Socket.IO. Both feed the same spawner,
so population limits, texture lifetime and swimming behaviour are shared.

### Running it against the Python app

1. Start the capture station: `python main.py --source image --input samples/real/photo_b.jpg`
2. Open `Scenes/Aquarium.unity` and press Play.

Unity connects on start, and the Console shows the state:

```
SocketFishIngestService: connecting to http://127.0.0.1:8765/socket.io/ (namespace /, event fish_captured)
SocketFishIngestService: connected to http://127.0.0.1:8765/socket.io/. Expecting a replay burst of recent captures.
```

The capture app should show its connected-consumer count go up. Order does not matter: if Unity starts
first, or the capture app is restarted later, the client keeps retrying and reconnects on its own.

### Testing without Python

Press **F5** to inject one synthetic capture, or **F6** for a burst of ten marked as replays. Both build
the contract JSON by hand, hand it to the real decode path, and log the main-thread cost. Useful for
proving the Unity half in isolation: if F5 spawns a fish but a live capture does not, the problem is the
connection, not the decode.

### Inspector settings

All on `Settings/AquariumConfig.asset`, under **Socket.IO Source**:

| Setting | Default | Notes |
| --- | --- | --- |
| `Socket Source Enabled` | on | Turn off to run folder-only |
| `Socket Auto Connect` | on | Off means calling `Connect()` yourself |
| `Socket Host` / `Socket Port` | `127.0.0.1` / `8765` | Builds `http://host:port/socket.io/` |
| `Socket Namespace` | `/` | |
| `Socket Event Name` | `fish_captured` | Also drives the parser fast path |
| `Socket Transport` | `PollingThenUpgradeToWebSocket` | Handshakes over HTTP and upgrades. `WebSocketOnly` skips the upgrade |
| `Socket Reconnection Attempts` | `0` | 0 or below means retry forever |
| `Socket Reconnection Delay` / `Max` | `1s` / `10s` | |
| `Expected Schema Version` | `1` | A different value on the wire is warned about once |

Population cap (`Max Fish Count`), `Sprite Pixels Per Unit` and `Max Fish Loads Per Frame` are in the
sections above and apply to both sources.

### Where things live in the scene

Nothing new to place by hand — `Scenes/Aquarium.unity` is already wired:

- `FishSystem` carries `SocketFishIngestService` alongside the existing `FishFactory`.
- `DebugCanvas` carries `SocketPayloadSelfTest` for the F5/F6 keys.
- `Prefabs/Fish.prefab` is the spawned fish; the sprite is assigned at runtime, so it needs no artwork.

### How a capture becomes a fish

```
BestHTTP SocketIO3            main thread, ~0.14 ms for a 1 MB payload
  -> RawFishPayloadParser     framing scan + one substring, no JSON parsing
  -> FishPayloadDecodeQueue   worker thread: LitJson parse + base64 decode
  -> FishTextureLoader        main thread: RGBA32 texture, clamped, LoadImage return checked
  -> FishSpriteCache          reference-counted, keyed by capture id
  -> FishFactory              size normalisation, behaviour profile, spawn
```

Live arrivals get the entrance animation. Replays skip it and are placed anywhere in the aquarium
rather than swimming in from an edge, because they are restoring fish that were already there.

### Deviations from the brief, and why

**Unity 6000.3.9f1, not 2021.3 LTS.** That is what the project is on. Built-in render pipeline is
active: the URP asset referenced in GraphicsSettings is a dangling GUID, so URP never loads. Sprites
render through `Sprites/Default`, which is correct under either pipeline, so nothing here depends on it.

**BestHTTP 2.7.0 using the `SocketIO3` namespace.** The plugin ships both `Source/SocketIO` (Engine.IO 3)
and `Source/SocketIO.3` (Engine.IO 4). Only the latter can talk to python-socketio 5.x. If the connection
ever fails to negotiate, check that nothing has been switched to the older namespace.

**A custom Socket.IO parser was added.** This is the one significant departure. Subscribing the obvious way,
`socket.On<FishCaptureMessage>(...)`, makes BestHTTP deserialise the payload on the main thread, and its
stock parser does it three times over: the whole payload into a `List<object>`, then the argument
re-serialised back to JSON, then parsed again into the target type. Measured in this project, even the
single-pass case costs about 24 microseconds per KB, so a documented one-megabyte capture blocks the main
thread for roughly 25 ms and the ten-event replay burst freezes it for a quarter of a second. That fails
two acceptance criteria outright. `RawFishPayloadParser` instead does the cheap framing scan, lifts the
argument out as a substring, and lets a worker thread do the parsing: **0.14 ms on the main thread instead
of 25.7 ms** for a 1.1 MB frame. Any frame it does not recognise falls through to the stock parser
untouched, and eleven tests pin down exactly which frames it claims.

**Spawner and fish behaviour were reused, not rewritten.** The brief lists them as deliverables, but this
project already had a tested spawner, population cap, texture-lifetime scheme and swimming behaviour from
the folder-watcher work. A second parallel stack would have meant two population caps and two texture
lifetimes to keep in sync. Agreed before building.

**No duplicate suppression.** Agreed before building. Every event carries a unique `id`, so id-based dedupe
would not stop the repeat-photo case anyway, and content hashing may be handled server-side later. The
population cap bounds the effect: the same drawing simply respawns and old copies are removed.

**Repeated connection errors are collapsed.** A capture station that is not running yet produces the same
error once a second, which is thousands of identical Console lines an hour. The first is logged as an
error, repeats are counted and summarised every 30 seconds, and the counter resets on connect.

**`confidence` is carried but never gates anything**, as instructed. There is a test asserting that a
payload with confidence 0.01 still spawns.
