namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Read-only status plus the few operations the debug overlay needs from the file ingest pipeline.
    /// Keeps the debug layer decoupled from the watcher, queue and loader implementations.
    /// </summary>
    public interface IFishIngestService {
        /// <summary> Absolute folder currently being watched for fish PNGs. </summary>
        string InputFolderPath { get; }

        /// <summary> True while the FileSystemWatcher is running and raising events. </summary>
        bool IsWatcherActive { get; }

        /// <summary> File name of the most recently spawned fish, or empty when none has loaded yet. </summary>
        string LastLoadedFileName { get; }

        /// <summary> Files detected but not yet decoded, including ones still waiting to finish being written. </summary>
        int PendingFileCount { get; }

        /// <summary> Number of distinct source files ingested since startup. </summary>
        int ProcessedFileCount { get; }

        /// <summary>
        /// Arrivals discarded because that source had already been ingested this session. Expected to be
        /// non-zero in normal operation - a socket reconnect replays recent captures, and a folder rescan
        /// re-reports every file it finds - so it is a health reading, not an error count.
        /// </summary>
        int DuplicateCount { get; }

        /// <summary> Re-reads the input folder. Already-ingested files are skipped unless the cache was reset. </summary>
        void RescanInputFolder();

        /// <summary> Forgets which files were already ingested so a rescan can load them again. Development aid only. </summary>
        void ResetProcessedFileCache();

        /// <summary> Spawns one fish from the development sample folder without touching the input folder. </summary>
        bool SpawnTestFish();
    }
}
