using UnityEngine;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// One place where Windows path separators are turned into forward slashes, so every path key,
    /// dictionary lookup and log line in the aquarium uses the same spelling of the same file.
    /// </summary>
    public static class PathUtility {
        public static string Normalize(string path) {
            if (string.IsNullOrEmpty(path)) {
                return string.Empty;
            }

            return path.Replace('\\', '/');
        }

        /// <summary> Normalised path with any trailing separator removed, for folder comparisons. </summary>
        public static string NormalizeFolder(string path) {
            return Normalize(path).TrimEnd('/');
        }
    }
}
