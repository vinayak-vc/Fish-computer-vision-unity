using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Saves the tank and puts it back. Design points 24, 28 and 29, and contract section 1's rule that
    /// Unity owns the life while Python owns only the artwork and the birth certificate.
    ///
    /// Three things have to happen together for a restart to be invisible, and leaving any one out
    /// produces a different visible failure:
    ///
    /// 1. **The roster**, so fish come back with their personalities, positions, ages and trust intact.
    /// 2. **The artwork**, cached by FishArtworkCache, because Python replays only its ten most recent
    ///    captures and everything older has no live source.
    /// 3. **The capture ids**, seeded back into the ingest services. Without this the aquarium restores
    ///    its fish from disk and then admits Python's replay burst as brand new arrivals, duplicating
    ///    the tank on every restart - the exact failure M0 exists to prevent.
    ///
    /// Written atomically, via a temporary file and a move. An installation loses power mid-write sooner
    /// or later, and a half-written save that parses to nothing would lose the whole tank.
    /// </summary>
    public sealed class AquariumPersistenceService : MonoBehaviour {
        public const string SaveFileName = "aquarium_state.json";

        [SerializeField] private AquariumConfig config;
        [SerializeField] private AquariumManager aquariumManager;
        [SerializeField] private FishFactory fishFactory;
        [Tooltip("Components implementing IFishIngestService. Every one of them is seeded with the restored capture ids, or that source will re-admit fish the save file already restored.")]
        [SerializeField] private MonoBehaviour[] fishIngestServiceBehaviours;

        private readonly List<IFishIngestService> ingestServices = new List<IFishIngestService>();

        /// <summary> Scratch set used while building the save file, so an id cannot be written twice. </summary>
        private readonly HashSet<string> knownCaptureIds = new HashSet<string>(System.StringComparer.Ordinal);

        private float nextSaveTime;
        private bool loaded;

        /// <summary> Fish restored from disk at startup. Zero on a first run. </summary>
        public int RestoredFishCount { get; private set; }

        /// <summary> Fish in the save file whose artwork was missing, so they could not come back. </summary>
        public int UnrestorableFishCount { get; private set; }

        public static string ResolveSavePath() {
            return PathUtility.Normalize(Path.Combine(Application.persistentDataPath, SaveFileName));
        }

        private void Awake() {
            ResolveIngestServices();
        }

        private void Start() {
            if (config == null || aquariumManager == null || fishFactory == null) {
                Debug.LogError("AquariumPersistenceService: dependencies are missing; the tank will not survive a restart.");
                return;
            }

            if (!config.PersistenceEnabled) {
                Debug.Log("AquariumPersistenceService: persistence is disabled in the config.");
                return;
            }

            Load();
            nextSaveTime = Time.unscaledTime + config.SaveIntervalSeconds;
        }

        private void Update() {
            if (!loaded || Time.unscaledTime < nextSaveTime) {
                return;
            }

            nextSaveTime = Time.unscaledTime + config.SaveIntervalSeconds;
            aquariumManager.Calendar.RefreshDate(DateTime.Now);
            Save();
        }

        /// <summary> Last chance to write. OnApplicationQuit does not fire on a kill, hence the periodic save too. </summary>
        private void OnApplicationQuit() {
            if (loaded) {
                Save();
            }
        }

        /// <summary> Writes the tank to disk. Safe to call at any time. </summary>
        public bool Save() {
            if (config == null || !config.PersistenceEnabled || aquariumManager == null) {
                return false;
            }

            AquariumSaveState state = BuildState();
            string path = ResolveSavePath();

            try {
                Directory.CreateDirectory(Application.persistentDataPath);

                string json = BestHTTP.JSON.LitJson.JsonMapper.ToJson(state);
                string temporaryPath = path + ".tmp";

                File.WriteAllText(temporaryPath, json);

                if (File.Exists(path)) {
                    File.Delete(path);
                }

                File.Move(temporaryPath, path);
            } catch (Exception exception) {
                Debug.LogError("AquariumPersistenceService: could not save to " + path + " - " + exception.Message);
                return false;
            }

            return true;
        }

        private AquariumSaveState BuildState() {
            AquariumSaveState state = new AquariumSaveState();
            state.version = AquariumSaveVersion.Current;

            for (int i = 0; i < aquariumManager.ActiveFish.Count; i++) {
                FishController fish = aquariumManager.ActiveFish[i];
                if (fish == null || fish.Data == null || fish.Data.Traits == null) {
                    continue;
                }

                // A fish already fading out is not part of the tank any more.
                if (fish.State == FishLifecycleState.Removing || fish.State == FishLifecycleState.Destroyed) {
                    continue;
                }

                // Babies are not archived captures - design point 5. Python holds no artwork for one, so
                // there would be nothing to rebuild it from; saving it would only produce an unrestorable
                // record and a warning on every start.
                if (!fish.Data.IsPersistable) {
                    continue;
                }

                state.fish.Add(BuildRecord(fish));
            }

            // Only the fish actually in the save file. Two earlier attempts at this were wrong and both
            // are worth remembering:
            //
            // Asking every ingest service for everything it had ever seen doubled the list on each
            // save-and-load cycle, because loading seeds the combined list into BOTH services and saving
            // then reads it back out of both. It also meant remembering ids for fish that no longer
            // existed - so a drawing whose fish had been evicted to make room could never come back, even
            // though Python still had it.
            //
            // The ids of the saved fish are exactly what the seeding is for: they stop Python's replay
            // burst duplicating the fish this file is about to restore. Anything not in here has no fish
            // to duplicate, so re-admitting it is the right outcome rather than a bug.
            for (int i = 0; i < state.fish.Count; i++) {
                string captureId = state.fish[i].captureId;

                if (!string.IsNullOrEmpty(captureId) && !knownCaptureIds.Contains(captureId)) {
                    knownCaptureIds.Add(captureId);
                    state.knownCaptureIds.Add(captureId);
                }
            }

            knownCaptureIds.Clear();

            CaptureCalendar calendar = aquariumManager.Calendar;
            state.capturesTodayDate = calendar.TrackedDate;
            state.capturesTodayCount = calendar.CapturesToday;
            state.latestSequence = calendar.LatestSequence;

            return state;
        }

        private static FishSaveRecord BuildRecord(FishController fish) {
            FishTraits traits = fish.Data.Traits;
            FishSaveRecord record = new FishSaveRecord();

            record.captureId = fish.Data.SourceFilePath;

            record.speed = traits.Speed;
            record.curiosity = traits.Curiosity;
            record.fear = traits.Fear;
            record.social = traits.Social;
            record.aggression = traits.Aggression;
            record.grace = traits.Grace;
            record.preferredDepth = traits.PreferredDepth;
            record.rarityScore = traits.RarityScore;
            record.rarityTier = (int)traits.RarityTier;
            record.objectType = (int)traits.ObjectType;
            record.objectConfidence = traits.ObjectConfidence;
            record.traitSource = (int)traits.Source;

            record.foregroundArea = fish.Data.ForegroundArea;

            record.positionX = fish.Position.x;
            record.positionY = fish.Position.y;

            record.affection = fish.Movement != null ? fish.Movement.Affection : 0f;
            record.ageSeconds = fish.AgeSeconds;
            record.foodEaten = fish.FoodEaten;

            return record;
        }

        /// <summary> Reads the tank back. Called once, from Start. </summary>
        public void Load() {
            loaded = true;
            string path = ResolveSavePath();

            if (!File.Exists(path)) {
                Debug.Log("AquariumPersistenceService: no save file at " + path + "; starting with an empty tank.");
                return;
            }

            AquariumSaveState state = ReadState(path);
            if (state == null) {
                return;
            }

            if (state.version > AquariumSaveVersion.Current) {
                Debug.LogWarning("AquariumPersistenceService: the save file is version " + state.version +
                                 " but this build understands " + AquariumSaveVersion.Current +
                                 ". Ignoring it rather than misreading it; the tank starts empty.");
                return;
            }

            // Seeded BEFORE any fish are restored, so a replay burst that arrives while the restore is
            // still running is already deduplicated against it.
            SeedKnownCaptures(state.knownCaptureIds);

            aquariumManager.Calendar.Restore(state.capturesTodayDate, state.capturesTodayCount, state.latestSequence, DateTime.Now);

            RestoreFish(state.fish);

            Debug.Log("AquariumPersistenceService: restored " + RestoredFishCount + " fish from " + path +
                      (UnrestorableFishCount > 0 ? (", " + UnrestorableFishCount + " could not be restored because their artwork was not cached") : string.Empty) +
                      ". Known capture ids seeded: " + state.knownCaptureIds.Count + ".");
        }

        private static AquariumSaveState ReadState(string path) {
            try {
                string json = File.ReadAllText(path);
                return BestHTTP.JSON.LitJson.JsonMapper.ToObject<AquariumSaveState>(json);
            } catch (Exception exception) {
                // A corrupt save costs the tank, not the installation. Starting empty is survivable;
                // throwing here would leave a black screen.
                Debug.LogError("AquariumPersistenceService: could not read " + path + " - " + exception.Message +
                               ". Starting with an empty tank rather than failing to start.");
                return null;
            }
        }

        private void RestoreFish(List<FishSaveRecord> records) {
            if (records == null) {
                return;
            }

            for (int i = 0; i < records.Count; i++) {
                FishSaveRecord record = records[i];
                if (record == null || string.IsNullOrEmpty(record.captureId)) {
                    continue;
                }

                if (!TryRestoreOne(record)) {
                    UnrestorableFishCount++;
                }
            }
        }

        private bool TryRestoreOne(FishSaveRecord record) {
            byte[] pngBytes;
            string error;

            if (!TryLoadArtwork(record.captureId, out pngBytes, out error)) {
                Debug.LogWarning("AquariumPersistenceService: cannot restore " + record.captureId + " - " + error +
                                 ". The drawing is gone, so the fish cannot come back.");
                return false;
            }

            Sprite sprite;
            if (!FishSpriteCache.TryAcquireFromBytes(record.captureId, pngBytes, config.SpritePixelsPerUnit, out sprite, out error)) {
                Debug.LogWarning("AquariumPersistenceService: could not rebuild a sprite for " + record.captureId + " - " + error);
                return false;
            }

            FishTraits traits = BuildTraits(record);

            // Restored silently: a restore is the aquarium putting fish back, not announcing arrivals, so
            // it skips the entrance animation, the birth moment and the crowd a newcomer would draw.
            FishController fish = fishFactory.CreateFish(sprite, record.captureId, traits, record.foregroundArea, true);

            if (fish == null) {
                FishSpriteCache.Release(record.captureId);
                return false;
            }

            fish.RestoreState(new Vector2(record.positionX, record.positionY), record.affection, record.ageSeconds, record.foodEaten);
            RestoredFishCount++;
            return true;
        }

        /// <summary>
        /// Finds a saved fish's drawing.
        ///
        /// The two ingest sources give a fish two different kinds of identity: a socket fish is keyed by
        /// its capture id and its PNG lives in the artwork cache, while a folder fish is keyed by the
        /// absolute path of the file it came from. So the cache is tried first, and an identity that turns
        /// out to be a file path still on disk is read straight from there.
        ///
        /// Reading the original rather than copying it into the cache keeps the folder path from doubling
        /// its disk use. The trade is that deleting a watched PNG does lose that fish on the next restart -
        /// acceptable, because the folder watcher is the development source and the socket is what an
        /// installation runs on.
        /// </summary>
        private static bool TryLoadArtwork(string identity, out byte[] pngBytes, out string error) {
            if (FishArtworkCache.TryLoad(identity, out pngBytes, out error)) {
                return true;
            }

            if (!FishTextureLoader.TryReadFile(identity, out pngBytes, out error)) {
                error = "not in the artwork cache, and not readable as a file either";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Rebuilds the personality from the file rather than re-deriving it. The deterministic fallback
        /// would produce a different creature for any fish that arrived with a real personality block from
        /// Python, and that fish would come back as a stranger.
        /// </summary>
        private static FishTraits BuildTraits(FishSaveRecord record) {
            return new FishTraits(record.captureId, record.speed, record.curiosity, record.fear, record.social,
                record.aggression, record.grace, record.preferredDepth, record.rarityScore,
                (FishRarityTier)record.rarityTier, (FishObjectType)record.objectType, record.objectConfidence,
                (FishTraitSource)record.traitSource);
        }

        private void SeedKnownCaptures(List<string> captureIds) {
            if (captureIds == null || captureIds.Count == 0) {
                return;
            }

            for (int i = 0; i < ingestServices.Count; i++) {
                ingestServices[i].SeedKnownCaptures(captureIds);
            }
        }

        private void ResolveIngestServices() {
            ingestServices.Clear();

            if (fishIngestServiceBehaviours == null) {
                return;
            }

            for (int i = 0; i < fishIngestServiceBehaviours.Length; i++) {
                MonoBehaviour behaviour = fishIngestServiceBehaviours[i];
                if (behaviour == null) {
                    continue;
                }

                IFishIngestService service = behaviour as IFishIngestService;

                if (service == null) {
                    Debug.LogError("AquariumPersistenceService: " + behaviour.GetType().Name +
                                   " does not implement IFishIngestService, so its already-seen sources cannot be saved or seeded.");
                    continue;
                }

                ingestServices.Add(service);
            }
        }
    }
}
