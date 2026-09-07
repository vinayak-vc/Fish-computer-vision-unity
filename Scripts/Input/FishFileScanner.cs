using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// One-shot directory listing. Used for the startup scan and as the periodic safety net behind
    /// the FileSystemWatcher, never as a per-frame poll.
    /// </summary>
    public static class FishFileScanner {
        public const string PngExtension = ".png";

        public static bool IsPngFile(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return false;
            }

            return string.Equals(Path.GetExtension(path), PngExtension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Fills results with every PNG in the folder, oldest first so a restart rebuilds the aquarium
        /// in the order the fish were originally captured. Returns the number of files found.
        /// </summary>
        public static int Scan(string folderPath, string searchPattern, List<string> results) {
            if (results == null) {
                Debug.LogError("FishFileScanner: Scan called with a null results list.");
                return 0;
            }

            results.Clear();

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) {
                return 0;
            }

            string pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" + PngExtension : searchPattern;

            string[] found;
            try {
                found = Directory.GetFiles(folderPath, pattern, SearchOption.TopDirectoryOnly);
            } catch (Exception exception) {
                Debug.LogError("FishFileScanner: could not read " + folderPath + " - " + exception.Message);
                return 0;
            }

            for (int i = 0; i < found.Length; i++) {
                if (!IsPngFile(found[i])) {
                    continue;
                }

                results.Add(PathUtility.Normalize(found[i]));
            }

            results.Sort(CompareByWriteTime);
            return results.Count;
        }

        private static int CompareByWriteTime(string left, string right) {
            DateTime leftTime = SafeGetWriteTime(left);
            DateTime rightTime = SafeGetWriteTime(right);

            int byTime = leftTime.CompareTo(rightTime);
            if (byTime != 0) {
                return byTime;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime SafeGetWriteTime(string path) {
            try {
                return File.GetLastWriteTimeUtc(path);
            } catch (Exception) {
                return DateTime.MinValue;
            }
        }
    }
}
