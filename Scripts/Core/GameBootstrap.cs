using UnityEngine;

using ViitorCloud.FishAquarium.Debugging;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Application entry point. Applies any external settings file, sets the frame rate and window mode,
    /// checks the camera projection and switches the debug tooling off outside development mode.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour {
        [SerializeField] private AquariumConfig config;
        [SerializeField] private Camera aquariumCamera;
        [SerializeField] private DebugOverlay debugOverlay;

        private void Awake() {
            if (config == null) {
                Debug.LogError("GameBootstrap: no AquariumConfig assigned, the aquarium cannot start.");
                return;
            }

            AppConfig.TryApplyOverrides(config);
            AppConfig.ApplyApplicationSettings(config);

            ValidateCamera();
            ConfigureDebugTooling();

            Debug.Log("GameBootstrap: running in " + config.RuntimeMode + " mode, reading fish from " + config.ResolveInputFolderPath());
        }

        private void ValidateCamera() {
            if (aquariumCamera == null) {
                aquariumCamera = Camera.main;
            }

            if (aquariumCamera == null) {
                Debug.LogError("GameBootstrap: no camera found for the aquarium.");
                return;
            }

            if (!aquariumCamera.orthographic) {
                Debug.LogError("GameBootstrap: the aquarium camera must be orthographic; forcing it on.");
                aquariumCamera.orthographic = true;
            }
        }

        private void ConfigureDebugTooling() {
            if (debugOverlay == null) {
                return;
            }

            debugOverlay.enabled = config.IsDevelopmentMode;
            debugOverlay.gameObject.SetActive(config.IsDevelopmentMode);
        }
    }
}
