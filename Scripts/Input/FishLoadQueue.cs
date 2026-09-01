using System;
using System.Collections.Generic;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// Thread-safe hand-off between the file watcher and the Unity main thread. Background threads only
    /// ever push strings in; the main thread pulls out the paths that are finished being written.
    /// Nothing in here touches the Unity API.
    /// </summary>
    public sealed class FishLoadQueue {
        private sealed class PendingFile {
            public string Path { get; set; }
            public float FirstSeenTime { get; set; }
            public float LastChangeTime { get; set; }
            public long LastObservedSize { get; set; }
        }

        private readonly object syncRoot = new object();
        private readonly Queue<string> incoming = new Queue<string>();
        private readonly HashSet<string> knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingFile> pending = new List<PendingFile>();
        private readonly List<string> abandoned = new List<string>();
        private readonly HashSet<string> processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary> Written by the main thread only; int reads are atomic so PendingCount stays cheap and lock-free. </summary>
        private int pendingCountCache;

        /// <summary> Files detected but not yet handed to the loader, including ones still settling. </summary>
        public int PendingCount {
            get {
                lock (syncRoot) {
                    return incoming.Count + pendingCountCache;
                }
            }
        }

        /// <summary> Distinct source files successfully turned into fish since startup. </summary>
        public int ProcessedCount {
            get { return processedPaths.Count; }
        }

        /// <summary> Safe to call from the FileSystemWatcher thread. Silently ignores paths already seen this session. </summary>
        public void Enqueue(string absolutePath) {
            if (string.IsNullOrWhiteSpace(absolutePath)) {
                return;
            }

            string normalised = PathUtility.Normalize(absolutePath);

            lock (syncRoot) {
                if (!knownPaths.Add(normalised)) {
                    return;
                }

                incoming.Enqueue(normalised);
            }
        }

        public bool HasSeen(string absolutePath) {
            if (string.IsNullOrWhiteSpace(absolutePath)) {
                return false;
            }

            lock (syncRoot) {
                return knownPaths.Contains(PathUtility.Normalize(absolutePath));
            }
        }

        /// <summary>
        /// Main-thread step. Moves newly detected files into the pending list, then returns the ones whose
        /// size has stopped changing and that can be opened and parsed as PNG. At most maxResults paths are
        /// handed out per call, so a burst of new files is spread across frames instead of stalling one.
        /// </summary>
        public int DrainReady(float now, float stabilitySeconds, float timeoutSeconds, int maxResults, List<string> readyPaths, List<string> timedOutPaths) {
            if (readyPaths == null) {
                return 0;
            }

            readyPaths.Clear();

            if (timedOutPaths != null) {
                timedOutPaths.Clear();
            }

            lock (syncRoot) {
                while (incoming.Count > 0) {
                    PendingFile entry = new PendingFile();
                    entry.Path = incoming.Dequeue();
                    entry.FirstSeenTime = now;
                    entry.LastChangeTime = now;
                    entry.LastObservedSize = -1;
                    pending.Add(entry);
                }
            }

            abandoned.Clear();

            for (int i = pending.Count - 1; i >= 0; i--) {
                if (readyPaths.Count >= maxResults) {
                    break;
                }

                PendingFile entry = pending[i];

                long size;
                if (!FishFileReadiness.TryGetFileSize(entry.Path, out size)) {
                    if (now - entry.FirstSeenTime >= timeoutSeconds) {
                        abandoned.Add(entry.Path);
                        pending.RemoveAt(i);
                    }

                    continue;
                }

                if (size != entry.LastObservedSize) {
                    entry.LastObservedSize = size;
                    entry.LastChangeTime = now;
                    continue;
                }

                if (now - entry.LastChangeTime < stabilitySeconds) {
                    continue;
                }

                string reason;
                long readySize;
                if (FishFileReadiness.IsReadyToRead(entry.Path, out readySize, out reason)) {
                    readyPaths.Add(entry.Path);
                    pending.RemoveAt(i);
                    continue;
                }

                if (now - entry.FirstSeenTime >= timeoutSeconds) {
                    abandoned.Add(entry.Path);
                    pending.RemoveAt(i);
                }
            }

            if (timedOutPaths != null) {
                for (int i = 0; i < abandoned.Count; i++) {
                    timedOutPaths.Add(abandoned[i]);
                }
            }

            pendingCountCache = pending.Count;
            return readyPaths.Count;
        }

        public void MarkProcessed(string absolutePath) {
            if (string.IsNullOrWhiteSpace(absolutePath)) {
                return;
            }

            processedPaths.Add(PathUtility.Normalize(absolutePath));
        }

        /// <summary> Forgets every path seen so far so the same files can be ingested again. Development aid, main thread only. </summary>
        public void ResetKnownPaths() {
            lock (syncRoot) {
                knownPaths.Clear();
                incoming.Clear();
            }

            pending.Clear();
            pendingCountCache = 0;
            processedPaths.Clear();
        }

        public void Clear() {
            ResetKnownPaths();
        }
    }
}
