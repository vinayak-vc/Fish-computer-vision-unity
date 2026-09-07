using System;
using System.IO;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Applies application-level settings and lets an installation override the authored AquariumConfig
    /// through a plain JSON file placed next to the built executable, so an exhibition machine can be
    /// re-pointed at a different capture folder without a rebuild.
    /// </summary>
    public static class AppConfig {
        public const string OverrideFileName = "aquarium_settings.json";

        [Serializable]
        private sealed class OverrideFile {
            public string inputFolder = string.Empty;
            public int maxFishCount = 0;
            public string runtimeMode = string.Empty;
            public int screenWidth = 0;
            public int screenHeight = 0;
        }

        /// <summary> Folder the override file is read from: the build directory, or the project root inside the Editor. </summary>
        public static string GetOverrideFolder() {
            string dataPath = PathUtility.Normalize(Application.dataPath);
            DirectoryInfo parent = Directory.GetParent(dataPath);
            if (parent == null) {
                return dataPath;
            }

            return PathUtility.Normalize(parent.FullName);
        }

        public static string GetOverrideFilePath() {
            return GetOverrideFolder() + "/" + OverrideFileName;
        }

        /// <summary> Reads the optional override file and pushes its values into the config. Returns true when an override was applied. </summary>
        public static bool TryApplyOverrides(AquariumConfig config) {
            if (config == null) {
                Debug.LogError("AppConfig: TryApplyOverrides called with a null AquariumConfig.");
                return false;
            }

            if (!config.AllowExternalSettingsOverride) {
                return false;
            }

            string path = GetOverrideFilePath();
            if (!File.Exists(path)) {
                return false;
            }

            OverrideFile parsed = null;
            try {
                string json = File.ReadAllText(path);
                parsed = JsonUtility.FromJson<OverrideFile>(json);
            } catch (Exception exception) {
                Debug.LogError("AppConfig: failed to read " + path + " - " + exception.Message);
                return false;
            }

            if (parsed == null) {
                Debug.LogError("AppConfig: " + path + " did not contain a valid settings object.");
                return false;
            }

            AquariumRuntimeMode mode = config.RuntimeMode;
            if (!string.IsNullOrWhiteSpace(parsed.runtimeMode)) {
                if (string.Equals(parsed.runtimeMode, AquariumRuntimeMode.Production.ToString(), StringComparison.OrdinalIgnoreCase)) {
                    mode = AquariumRuntimeMode.Production;
                } else if (string.Equals(parsed.runtimeMode, AquariumRuntimeMode.Development.ToString(), StringComparison.OrdinalIgnoreCase)) {
                    mode = AquariumRuntimeMode.Development;
                } else {
                    Debug.LogWarning("AppConfig: unknown runtimeMode value in " + OverrideFileName + " - " + parsed.runtimeMode);
                }
            }

            config.ApplyExternalOverrides(parsed.inputFolder, parsed.maxFishCount, mode);

            if (parsed.screenWidth > 0 && parsed.screenHeight > 0) {
                Screen.SetResolution(parsed.screenWidth, parsed.screenHeight, Screen.fullScreenMode);
            }

            Debug.Log("AppConfig: applied external overrides from " + path);
            return true;
        }

        /// <summary> Frame rate, vsync and window mode. Production runs fullscreen and never sleeps in the background. </summary>
        public static void ApplyApplicationSettings(AquariumConfig config) {
            if (config == null) {
                Debug.LogError("AppConfig: ApplyApplicationSettings called with a null AquariumConfig.");
                return;
            }

            Application.runInBackground = true;
            QualitySettings.vSyncCount = Mathf.Max(0, config.VSyncCount);
            Application.targetFrameRate = config.TargetFrameRate > 0 ? config.TargetFrameRate : -1;

            if (Application.isEditor) {
                return;
            }

            if (config.RuntimeMode == AquariumRuntimeMode.Production && config.FullscreenInProduction) {
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                Screen.fullScreen = true;
            }

            Cursor.visible = config.IsDevelopmentMode;
        }

        /// <summary> Creates the configured fish folder when it is missing so the watcher always has a valid target. </summary>
        public static bool EnsureFolderExists(string folderPath) {
            if (string.IsNullOrWhiteSpace(folderPath)) {
                return false;
            }

            if (Directory.Exists(folderPath)) {
                return true;
            }

            try {
                Directory.CreateDirectory(folderPath);
                Debug.Log("AppConfig: created fish input folder at " + folderPath);
                return true;
            } catch (Exception exception) {
                Debug.LogError("AppConfig: could not create folder " + folderPath + " - " + exception.Message);
                return false;
            }
        }
    }
}
