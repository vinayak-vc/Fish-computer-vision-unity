using System;
using System.IO;
using System.Text;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Keeps a local copy of every drawing's PNG, so the aquarium can rebuild its fish without Python.
    ///
    /// This is what makes persistence actually work. Section 8 of the contract replays only the most
    /// recent `publish.replay_last` captures, ten by default - so a tank of fifty fish cannot be restored
    /// from the socket, however patient you are. Either the artwork is cached here or forty of those fish
    /// are gone for good.
    ///
    /// Writing to captures/ is explicitly forbidden, that directory being Python's, so this lives under
    /// Application.persistentDataPath and is Unity's alone.
    ///
    /// Capture ids are opaque per contract section 3 rule 3, so they are never parsed for meaning - but
    /// they do end up as file names, and an id containing a path separator would otherwise write outside
    /// the cache directory. Every id is therefore mapped to a safe name.
    /// </summary>
    public static class FishArtworkCache {
        public const string CacheFolderName = "artwork";

        private const string FileExtension = ".png";

        /// <summary> Absolute path of the cache directory. Created on demand. </summary>
        public static string ResolveCacheFolder() {
            return PathUtility.NormalizeFolder(Path.Combine(Application.persistentDataPath, CacheFolderName));
        }

        /// <summary>
        /// File name a capture id maps to. Alphanumerics, dash, dot and underscore survive; everything
        /// else becomes an underscore, and a short hash of the original id is appended so two ids that
        /// sanitise to the same string cannot collide.
        /// </summary>
        public static string BuildFileName(string captureId) {
            if (string.IsNullOrEmpty(captureId)) {
                return "unknown" + FileExtension;
            }

            StringBuilder builder = new StringBuilder(captureId.Length + 20);

            for (int i = 0; i < captureId.Length; i++) {
                char character = captureId[i];
                bool safe = (character >= 'a' && character <= 'z') ||
                            (character >= 'A' && character <= 'Z') ||
                            (character >= '0' && character <= '9') ||
                            character == '-' || character == '_' || character == '.';

                builder.Append(safe ? character : '_');
            }

            // Guards against both collisions after sanitising and against an id long enough to break the
            // path length limit.
            if (builder.Length > 96) {
                builder.Length = 96;
            }

            builder.Append('-');
            builder.Append(StableHash.Of(captureId).ToString("x16"));
            builder.Append(FileExtension);
            return builder.ToString();
        }

        public static string ResolvePath(string captureId) {
            return PathUtility.Normalize(Path.Combine(ResolveCacheFolder(), BuildFileName(captureId)));
        }

        public static bool Contains(string captureId) {
            if (string.IsNullOrEmpty(captureId)) {
                return false;
            }

            return File.Exists(ResolvePath(captureId));
        }

        /// <summary>
        /// Stores the PNG for a capture. Already-cached artwork is left alone: the bytes for a given id
        /// never change, so rewriting them would be pure disk churn on every replay burst.
        /// </summary>
        public static bool TryStore(string captureId, byte[] pngBytes, out string error) {
            error = string.Empty;

            if (string.IsNullOrEmpty(captureId)) {
                error = "empty capture id";
                return false;
            }

            if (pngBytes == null || pngBytes.Length == 0) {
                error = "no image data";
                return false;
            }

            string path = ResolvePath(captureId);

            if (File.Exists(path)) {
                return true;
            }

            try {
                Directory.CreateDirectory(ResolveCacheFolder());

                // Written to a temporary name and moved into place, so a crash or a pulled plug mid-write
                // cannot leave a half-file that looks cached but decodes to nothing.
                string temporaryPath = path + ".tmp";
                File.WriteAllBytes(temporaryPath, pngBytes);

                if (File.Exists(path)) {
                    File.Delete(path);
                }

                File.Move(temporaryPath, path);
            } catch (Exception exception) {
                error = exception.Message;
                return false;
            }

            return true;
        }

        public static bool TryLoad(string captureId, out byte[] pngBytes, out string error) {
            pngBytes = null;
            error = string.Empty;

            if (string.IsNullOrEmpty(captureId)) {
                error = "empty capture id";
                return false;
            }

            string path = ResolvePath(captureId);

            if (!File.Exists(path)) {
                error = "not cached";
                return false;
            }

            try {
                pngBytes = File.ReadAllBytes(path);
            } catch (Exception exception) {
                error = exception.Message;
                return false;
            }

            if (pngBytes.Length == 0 || !FishTextureLoader.HasPngSignature(pngBytes)) {
                pngBytes = null;
                error = "cached file is not a valid PNG";
                return false;
            }

            return true;
        }

        /// <summary> Removes one drawing's cached artwork. Used when a fish is forgotten for good. </summary>
        public static bool TryRemove(string captureId) {
            if (string.IsNullOrEmpty(captureId)) {
                return false;
            }

            string path = ResolvePath(captureId);

            try {
                if (!File.Exists(path)) {
                    return false;
                }

                File.Delete(path);
            } catch (Exception exception) {
                Debug.LogWarning("FishArtworkCache: could not delete " + path + " - " + exception.Message);
                return false;
            }

            return true;
        }

        /// <summary> Cached files currently on disk. Zero when nothing has been cached yet. </summary>
        public static int CountCachedFiles() {
            string folder = ResolveCacheFolder();

            try {
                if (!Directory.Exists(folder)) {
                    return 0;
                }

                return Directory.GetFiles(folder, "*" + FileExtension, SearchOption.TopDirectoryOnly).Length;
            } catch (Exception exception) {
                Debug.LogWarning("FishArtworkCache: could not read " + folder + " - " + exception.Message);
                return 0;
            }
        }
    }
}
