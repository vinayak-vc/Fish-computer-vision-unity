using System.Collections.Generic;
using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// Drives the whole intake pipeline: watcher and scanner find files, the queue decides when they are
    /// safe to read, the sprite cache decodes them and the factory turns them into fish. Every Unity call
    /// happens here on the main thread.
    /// </summary>
    public sealed class FishIngestService : MonoBehaviour, IFishIngestService {
        private const float WatcherRestartIntervalSeconds = 5f;

        [SerializeField] private AquariumConfig config;
        [SerializeField] private FishFactory fishFactory;
        [SerializeField] private AquariumManager aquariumManager;

        private readonly FishLoadQueue loadQueue = new FishLoadQueue();
        private readonly List<string> readyPaths = new List<string>();
        private readonly List<string> timedOutPaths = new List<string>();
        private readonly List<string> scanBuffer = new List<string>();
        private readonly List<string> deletedPaths = new List<string>();
        private readonly List<string> deletionBuffer = new List<string>();
        private readonly object deletionSyncRoot = new object();

        private FishFileWatcher fileWatcher;
        private string inputFolderPath = string.Empty;
        private string testFishFolderPath = string.Empty;
        private string lastLoadedFileName = string.Empty;
        private float nextRescanTime;
        private float nextWatcherRestartTime;
        private bool started;

        public string InputFolderPath {
            get { return inputFolderPath; }
        }

        public bool IsWatcherActive {
            get { return fileWatcher != null && fileWatcher.IsActive; }
        }

        public string LastLoadedFileName {
            get { return lastLoadedFileName; }
        }

        public int PendingFileCount {
            get { return loadQueue.PendingCount; }
        }

        public int ProcessedFileCount {
            get { return loadQueue.ProcessedCount; }
        }

        private void Start() {
            if (config == null || fishFactory == null || aquariumManager == null) {
                Debug.LogError("FishIngestService: dependencies are missing, no fish will be loaded.");
                return;
            }

            inputFolderPath = config.ResolveInputFolderPath();
            testFishFolderPath = config.ResolveTestFishFolderPath();

            if (string.IsNullOrEmpty(inputFolderPath)) {
                Debug.LogError("FishIngestService: the configured fish input folder resolved to an empty path.");
                return;
            }

            if (config.CreateInputFolderIfMissing) {
                AppConfig.EnsureFolderExists(inputFolderPath);
            }

            Debug.Log("FishIngestService: watching " + inputFolderPath);

            if (config.LoadExistingFishOnStartup) {
                EnqueueFolder(inputFolderPath);
            }

            if (config.IsDevelopmentMode && config.LoadTestFishInDevelopment) {
                EnqueueFolder(testFishFolderPath);
            }

            if (config.WatchForNewFiles) {
                StartWatcher();
            }

            nextRescanTime = Time.time + config.PeriodicRescanSeconds;
            started = true;
        }

        private void Update() {
            if (!started) {
                return;
            }

            MaintainWatcher();
            ProcessDeletedFiles();
            ProcessPeriodicRescan();
            ProcessReadyFiles();
        }

        private void OnDestroy() {
            if (fileWatcher != null) {
                fileWatcher.FileDetected -= HandleFileDetected;
                fileWatcher.FileDeleted -= HandleFileDeleted;
                fileWatcher.Dispose();
                fileWatcher = null;
            }
        }

        public void RescanInputFolder() {
            if (string.IsNullOrEmpty(inputFolderPath)) {
                return;
            }

            EnqueueFolder(inputFolderPath);

            if (config != null && config.IsDevelopmentMode && config.LoadTestFishInDevelopment) {
                EnqueueFolder(testFishFolderPath);
            }
        }

        public void ResetProcessedFileCache() {
            loadQueue.ResetKnownPaths();
            lastLoadedFileName = string.Empty;
        }

        /// <summary> Loads one random PNG from the development sample folder. Uses the shared sprite cache, so repeats cost no extra memory. </summary>
        public bool SpawnTestFish() {
            if (config == null || fishFactory == null) {
                return false;
            }

            if (FishFileScanner.Scan(testFishFolderPath, config.FileSearchPattern, scanBuffer) == 0) {
                Debug.LogWarning("FishIngestService: no test fish found in " + testFishFolderPath);
                return false;
            }

            string chosen = scanBuffer[UnityEngine.Random.Range(0, scanBuffer.Count)];
            return TryLoadAndSpawn(chosen);
        }

        private void StartWatcher() {
            if (fileWatcher == null) {
                fileWatcher = new FishFileWatcher();
                fileWatcher.FileDetected += HandleFileDetected;
                fileWatcher.FileDeleted += HandleFileDeleted;
            }

            fileWatcher.Start(inputFolderPath, config.FileSearchPattern);
        }

        /// <summary> Called on the FileSystemWatcher thread. Pushing a string into the queue is the only work allowed here. </summary>
        private void HandleFileDetected(string absolutePath) {
            loadQueue.Enqueue(absolutePath);
        }

        /// <summary> Also called on the watcher thread, so the path is buffered for the main thread to act on. </summary>
        private void HandleFileDeleted(string absolutePath) {
            lock (deletionSyncRoot) {
                deletedPaths.Add(absolutePath);
            }
        }

        /// <summary>
        /// Keeps the watcher alive for the life of the app. It can stop for reasons outside our control:
        /// a FileSystemWatcher buffer overflow, the folder handle being lost, or an Editor domain reload
        /// dropping the instance mid-play. All of them recover by starting a fresh watcher and rescanning,
        /// so any files that arrived while it was down are not missed.
        /// </summary>
        private void MaintainWatcher() {
            if (!config.WatchForNewFiles) {
                return;
            }

            if (fileWatcher != null) {
                string error;
                if (fileWatcher.TryConsumeError(out error)) {
                    Debug.LogError("FishIngestService: file watcher error - " + error);
                }

                if (fileWatcher.IsActive) {
                    return;
                }
            }

            if (Time.time < nextWatcherRestartTime) {
                return;
            }

            nextWatcherRestartTime = Time.time + WatcherRestartIntervalSeconds;
            StartWatcher();

            if (fileWatcher.IsActive) {
                Debug.Log("FishIngestService: file watcher started on " + inputFolderPath);
                EnqueueFolder(inputFolderPath);
            }
        }

        private void ProcessPeriodicRescan() {
            if (config.PeriodicRescanSeconds <= 0f) {
                return;
            }

            if (Time.time < nextRescanTime) {
                return;
            }

            nextRescanTime = Time.time + config.PeriodicRescanSeconds;
            EnqueueFolder(inputFolderPath);
        }

        private void ProcessReadyFiles() {
            loadQueue.DrainReady(Time.time, config.FileStabilitySeconds, config.FileReadTimeoutSeconds, config.MaxFishLoadsPerFrame, readyPaths, timedOutPaths);

            for (int i = 0; i < timedOutPaths.Count; i++) {
                Debug.LogWarning("FishIngestService: gave up waiting for " + timedOutPaths[i] + " to finish being written.");
            }

            for (int i = 0; i < readyPaths.Count; i++) {
                TryLoadAndSpawn(readyPaths[i]);
            }
        }

        /// <summary> Section 45 behaviour: only acts when the config explicitly opts in to mirroring deletions. </summary>
        private void ProcessDeletedFiles() {
            lock (deletionSyncRoot) {
                if (deletedPaths.Count == 0) {
                    return;
                }

                deletionBuffer.Clear();
                for (int i = 0; i < deletedPaths.Count; i++) {
                    deletionBuffer.Add(deletedPaths[i]);
                }

                deletedPaths.Clear();
            }

            if (!config.RemoveFishWhenSourceFileDeleted) {
                return;
            }

            for (int i = 0; i < deletionBuffer.Count; i++) {
                RemoveFishBySourcePath(deletionBuffer[i]);
            }
        }

        private void RemoveFishBySourcePath(string absolutePath) {
            IReadOnlyList<FishController> fish = aquariumManager.ActiveFish;

            for (int i = fish.Count - 1; i >= 0; i--) {
                FishController candidate = fish[i];
                if (candidate == null || candidate.Data == null) {
                    continue;
                }

                if (!string.Equals(candidate.Data.SourceFilePath, absolutePath, System.StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                aquariumManager.RemoveFish(candidate, true);
            }
        }

        private bool TryLoadAndSpawn(string absolutePath) {
            Sprite sprite;
            string error;

            if (!FishSpriteCache.TryAcquire(absolutePath, config.SpritePixelsPerUnit, out sprite, out error)) {
                Debug.LogWarning("FishIngestService: could not load " + absolutePath + " - " + error);
                return false;
            }

            FishController fish = fishFactory.CreateFish(sprite, absolutePath);
            if (fish == null) {
                FishSpriteCache.Release(absolutePath);
                return false;
            }

            loadQueue.MarkProcessed(absolutePath);
            lastLoadedFileName = Path.GetFileName(absolutePath);
            return true;
        }

        private void EnqueueFolder(string folderPath) {
            if (string.IsNullOrEmpty(folderPath)) {
                return;
            }

            int found = FishFileScanner.Scan(folderPath, config.FileSearchPattern, scanBuffer);

            for (int i = 0; i < found; i++) {
                loadQueue.Enqueue(scanBuffer[i]);
            }
        }
    }
}
