using System.Text;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Fish;
using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Debugging {
    /// <summary>
    /// Development-only status panel and hotkeys. Disabled entirely in production, where OnGUI is
    /// never reached because the component itself is switched off by GameBootstrap.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour {
        private const float TextRefreshIntervalSeconds = 0.25f;
        private const float FrameRateSmoothing = 0.1f;

        [SerializeField] private AquariumConfig config;
        [SerializeField] private AquariumManager aquariumManager;
        [Tooltip("Must be a component implementing IFishIngestService, normally FishIngestService.")]
        [SerializeField] private MonoBehaviour fishIngestServiceBehaviour;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private KeyCode spawnTestFishKey = KeyCode.F2;
        [SerializeField] private KeyCode clearAquariumKey = KeyCode.F3;
        [SerializeField] private KeyCode rescanFolderKey = KeyCode.F4;
        [SerializeField] private KeyCode quitKey = KeyCode.Escape;

        private readonly StringBuilder textBuilder = new StringBuilder(512);

        private IFishIngestService ingestService;
        private GUIStyle panelStyle;
        private GUIStyle labelStyle;
        private Texture2D panelBackground;
        private string cachedText = string.Empty;
        private float smoothedDeltaTime;
        private float nextTextRefreshTime;
        private bool visible = true;

        private void Awake() {
            ingestService = fishIngestServiceBehaviour as IFishIngestService;

            if (fishIngestServiceBehaviour != null && ingestService == null) {
                Debug.LogError("DebugOverlay: the assigned component does not implement IFishIngestService.");
            }

            if (config != null) {
                visible = config.DebugOverlayVisibleOnStart;
            }

            smoothedDeltaTime = Time.unscaledDeltaTime;
        }

        private void Update() {
            smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, Time.unscaledDeltaTime, FrameRateSmoothing);
            HandleHotkeys();
        }

        private void OnDestroy() {
            if (panelBackground != null) {
                Destroy(panelBackground);
                panelBackground = null;
            }
        }

        private void HandleHotkeys() {
            if (UnityEngine.Input.GetKeyDown(toggleKey)) {
                visible = !visible;
            }

            if (UnityEngine.Input.GetKeyDown(spawnTestFishKey) && ingestService != null) {
                ingestService.SpawnTestFish();
            }

            if (UnityEngine.Input.GetKeyDown(clearAquariumKey)) {
                ClearAquarium();
            }

            if (UnityEngine.Input.GetKeyDown(rescanFolderKey) && ingestService != null) {
                ingestService.RescanInputFolder();
            }

            if (UnityEngine.Input.GetKeyDown(quitKey)) {
                QuitApplication();
            }
        }

        /// <summary> Clearing also forgets which files were ingested, so a following rescan reloads them. </summary>
        private void ClearAquarium() {
            if (aquariumManager != null) {
                aquariumManager.ClearAllFish();
            }

            if (ingestService != null) {
                ingestService.ResetProcessedFileCache();
            }
        }

        private void QuitApplication() {
            if (config != null && !config.IsDevelopmentMode) {
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnGUI() {
            if (!visible) {
                return;
            }

            EnsureStyles();
            RefreshText();

            GUI.Box(new Rect(12f, 12f, 620f, 284f), GUIContent.none, panelStyle);
            GUI.Label(new Rect(24f, 22f, 600f, 266f), cachedText, labelStyle);
        }

        private void RefreshText() {
            if (Time.unscaledTime < nextTextRefreshTime) {
                return;
            }

            nextTextRefreshTime = Time.unscaledTime + TextRefreshIntervalSeconds;

            float frameRate = smoothedDeltaTime > Mathf.Epsilon ? (1f / smoothedDeltaTime) : 0f;

            textBuilder.Length = 0;
            textBuilder.Append("FPS: ").Append(frameRate.ToString("F1")).Append('\n');
            textBuilder.Append("Fish: ").Append(aquariumManager != null ? aquariumManager.LiveFishCount : 0);
            textBuilder.Append(" / ").Append(aquariumManager != null ? aquariumManager.MaxFishCount : 0).Append('\n');

            if (ingestService != null) {
                textBuilder.Append("Input folder: ").Append(ingestService.InputFolderPath).Append('\n');
                textBuilder.Append("Last fish: ").Append(string.IsNullOrEmpty(ingestService.LastLoadedFileName) ? "none" : ingestService.LastLoadedFileName).Append('\n');
                textBuilder.Append("File watcher: ").Append(ingestService.IsWatcherActive ? "ACTIVE" : "INACTIVE");
                textBuilder.Append("   pending: ").Append(ingestService.PendingFileCount);
                textBuilder.Append("   loaded: ").Append(ingestService.ProcessedFileCount);
                textBuilder.Append("   dupes: ").Append(ingestService.DuplicateCount).Append('\n');
            } else {
                textBuilder.Append("Ingest service: NOT ASSIGNED\n");
            }

            AppendPointerLine();
            AppendFeedingLine();
            AppendNewestFishTraits();

            textBuilder.Append("F1 overlay   F2 test fish   F3 clear   F4 rescan   ESC quit\n");
            textBuilder.Append("click near the surface to feed, lower down for a ripple");

            cachedText = textBuilder.ToString();
        }

        /// <summary>
        /// Where the visitor is, as the fish see it. Worth showing because a pointer reporting unavailable
        /// looks exactly like fish that are simply not reacting, and the two have different fixes.
        /// </summary>
        private void AppendPointerLine() {
            if (aquariumManager == null) {
                return;
            }

            IPointerSource pointer = aquariumManager.Pointer;

            if (pointer == null) {
                textBuilder.Append("Pointer: NOT ASSIGNED\n");
                return;
            }

            textBuilder.Append("Pointer: ").Append(pointer.IsAvailable ? "ok" : "away");
            textBuilder.Append("   speed: ").Append(pointer.Speed.ToString("F1"));
            textBuilder.Append("   ripples: ").Append(aquariumManager.Ripples != null ? aquariumManager.Ripples.ActiveCount : 0);
            textBuilder.Append("   food: ").Append(aquariumManager.Food != null ? aquariumManager.Food.ActiveCount : 0).Append('\n');
        }

        /// <summary>
        /// Who is actually winning the food. Whether the aggressive fish are eating more than the timid
        /// ones is the acceptance criterion for feeding, and it is not something you can judge by watching
        /// a tank of two hundred fish - so the numbers are put on screen instead.
        /// </summary>
        private void AppendFeedingLine() {
            if (aquariumManager == null || aquariumManager.ActiveFish.Count == 0) {
                return;
            }

            float aggressiveEaten = 0f;
            float timidEaten = 0f;
            int aggressiveCount = 0;
            int timidCount = 0;

            for (int i = 0; i < aquariumManager.ActiveFish.Count; i++) {
                FishController fish = aquariumManager.ActiveFish[i];
                if (fish == null || fish.Data == null || fish.Data.Traits == null) {
                    continue;
                }

                if (fish.Data.Traits.Aggression >= 0.5f) {
                    aggressiveEaten += fish.FoodEaten;
                    aggressiveCount++;
                } else {
                    timidEaten += fish.FoodEaten;
                    timidCount++;
                }
            }

            if (aggressiveCount == 0 && timidCount == 0) {
                return;
            }

            textBuilder.Append("Eaten per fish: aggressive ").Append(aggressiveCount == 0 ? 0f : (aggressiveEaten / aggressiveCount));
            textBuilder.Append("   timid ").Append(timidCount == 0 ? 0f : (timidEaten / timidCount)).Append('\n');
        }

        /// <summary>
        /// Personality of the most recently spawned fish. The fastest way to tell whether a drawing arrived
        /// with traits from Python or fell back to its identity hash, which is otherwise invisible.
        /// </summary>
        private void AppendNewestFishTraits() {
            if (aquariumManager == null || aquariumManager.ActiveFish.Count == 0) {
                return;
            }

            FishController newest = aquariumManager.ActiveFish[aquariumManager.ActiveFish.Count - 1];
            if (newest == null || newest.Data == null || newest.Data.Traits == null) {
                return;
            }

            textBuilder.Append("Newest: ").Append(newest.Data.Traits.ToString()).Append('\n');
        }

        private void EnsureStyles() {
            if (panelStyle != null && labelStyle != null && panelBackground != null) {
                return;
            }

            if (panelBackground == null) {
                panelBackground = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                panelBackground.name = "DebugOverlayBackground";
                panelBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
                panelBackground.Apply(false, false);
            }

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = panelBackground;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.wordWrap = true;
            labelStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 1f);
        }
    }
}
