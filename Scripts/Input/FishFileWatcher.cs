using System;
using System.IO;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// FileSystemWatcher wrapper. It reports paths and nothing else: no file reading, no texture
    /// creation and no Unity API calls, because its callbacks arrive on a background thread.
    /// Errors are buffered for the main thread to log and act on.
    /// </summary>
    public sealed class FishFileWatcher : IDisposable {
        private const int InternalBufferSize = 32768;

        private readonly object syncRoot = new object();

        private FileSystemWatcher watcher;
        private string bufferedError = string.Empty;
        private bool disposed;

        /// <summary> Raised on a background thread with the absolute path of a new or renamed file. </summary>
        public event Action<string> FileDetected;

        /// <summary> Raised on a background thread when a watched file disappears. </summary>
        public event Action<string> FileDeleted;

        public bool IsActive { get; private set; }

        public string FolderPath { get; private set; }

        /// <summary> Begins watching. Returns false and buffers the reason when the folder cannot be watched. </summary>
        public bool Start(string folderPath, string filter) {
            Stop();

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) {
                BufferError("folder does not exist: " + folderPath);
                return false;
            }

            try {
                FileSystemWatcher created = new FileSystemWatcher(folderPath);
                created.Filter = string.IsNullOrWhiteSpace(filter) ? "*.png" : filter;
                created.NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite;
                created.IncludeSubdirectories = false;
                created.InternalBufferSize = InternalBufferSize;

                created.Created += HandleCreated;
                created.Renamed += HandleRenamed;
                created.Deleted += HandleDeleted;
                created.Error += HandleError;

                created.EnableRaisingEvents = true;

                watcher = created;
                FolderPath = folderPath;
                IsActive = true;
                return true;
            } catch (Exception exception) {
                BufferError("could not start watching " + folderPath + " - " + exception.Message);
                return false;
            }
        }

        public void Stop() {
            IsActive = false;

            if (watcher == null) {
                return;
            }

            try {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= HandleCreated;
                watcher.Renamed -= HandleRenamed;
                watcher.Deleted -= HandleDeleted;
                watcher.Error -= HandleError;
                watcher.Dispose();
            } catch (Exception exception) {
                BufferError("failed to stop cleanly - " + exception.Message);
            }

            watcher = null;
        }

        /// <summary> Main thread pulls any buffered error out for logging. Returns false when there is nothing to report. </summary>
        public bool TryConsumeError(out string error) {
            lock (syncRoot) {
                if (string.IsNullOrEmpty(bufferedError)) {
                    error = string.Empty;
                    return false;
                }

                error = bufferedError;
                bufferedError = string.Empty;
                return true;
            }
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            Stop();
            FileDetected = null;
            FileDeleted = null;
        }

        private void HandleCreated(object sender, FileSystemEventArgs args) {
            RaiseDetected(args.FullPath);
        }

        private void HandleRenamed(object sender, RenamedEventArgs args) {
            RaiseDetected(args.FullPath);
        }

        private void HandleDeleted(object sender, FileSystemEventArgs args) {
            if (!FishFileScanner.IsPngFile(args.FullPath)) {
                return;
            }

            Action<string> handler = FileDeleted;
            if (handler != null) {
                handler(PathUtility.Normalize(args.FullPath));
            }
        }

        /// <summary> Buffer overflow or a lost directory handle lands here; the ingest service restarts the watcher. </summary>
        private void HandleError(object sender, ErrorEventArgs args) {
            IsActive = false;

            Exception exception = args.GetException();
            BufferError(exception != null ? exception.Message : "unknown FileSystemWatcher error");
        }

        private void RaiseDetected(string fullPath) {
            if (!FishFileScanner.IsPngFile(fullPath)) {
                return;
            }

            Action<string> handler = FileDetected;
            if (handler != null) {
                handler(PathUtility.Normalize(fullPath));
            }
        }

        private void BufferError(string message) {
            lock (syncRoot) {
                bufferedError = message;
            }
        }
    }
}
