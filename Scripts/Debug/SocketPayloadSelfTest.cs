#if !BESTHTTP_DISABLE_SOCKETIO

using System.Diagnostics;
using System.Globalization;
using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Network;

using Debug = UnityEngine.Debug;

namespace ViitorCloud.FishAquarium.Debugging {
    /// <summary>
    /// Development aid that pushes a synthetic capture through the real decode path, so the Unity half of
    /// the system can be proved without the Python capture station running.
    ///
    /// It builds the contract JSON by hand and deserialises it with the same LitJson mapper BestHTTP uses,
    /// so a mismatch between the wire contract and FishCaptureMessage shows up here rather than on site.
    /// </summary>
    public sealed class SocketPayloadSelfTest : MonoBehaviour {
        private const int ReplayBurstSize = 10;

        [SerializeField] private AquariumConfig config;
        [SerializeField] private SocketFishIngestService ingestService;
        [Tooltip("Folder of PNGs used as stand-in capture payloads, relative to StreamingAssets.")]
        [SerializeField] private string samplePngFolder = "Sample";
        [SerializeField] private KeyCode injectLiveKey = KeyCode.F5;
        [SerializeField] private KeyCode injectReplayBurstKey = KeyCode.F6;
        [Tooltip("Injects a schema v1 capture with no personality block, exercising the deterministic trait fallback.")]
        [SerializeField] private KeyCode injectLegacyKey = KeyCode.F7;

        private string[] samplePaths;
        private int sequence;

        private void Update() {
            if (config != null && !config.IsDevelopmentMode) {
                return;
            }

            if (UnityEngine.Input.GetKeyDown(injectLiveKey)) {
                InjectOne(false);
            }

            if (UnityEngine.Input.GetKeyDown(injectReplayBurstKey)) {
                InjectReplayBurst();
            }

            if (UnityEngine.Input.GetKeyDown(injectLegacyKey)) {
                InjectOne(false, true);
            }
        }

        /// <summary> Injects one synthetic schema v2 capture. Returns false when no sample PNG could be read. </summary>
        public bool InjectOne(bool asReplay) {
            return InjectOne(asReplay, false);
        }

        /// <summary>
        /// Injects one synthetic capture under a fresh id. legacy emits a schema v1 payload with no
        /// personality block, which exercises the deterministic fallback that every archived drawing uses.
        /// </summary>
        public bool InjectOne(bool asReplay, bool legacy) {
            sequence++;
            return Inject("fish_selftest_" + sequence.ToString("D3"), sequence, asReplay, legacy);
        }

        /// <summary>
        /// Mimics the burst the capture station sends on connect, deliberately reusing the same ten ids
        /// every time.
        ///
        /// That is what makes this the M0 acceptance check: press the key once and ten fish arrive with no
        /// birth animation; press it again and nothing happens at all, because section 8 dedupe discards
        /// every one of them. Fresh ids on the second press would prove neither.
        /// </summary>
        public void InjectReplayBurst() {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int accepted = 0;

            for (int i = 0; i < ReplayBurstSize; i++) {
                if (Inject("fish_selftest_replay_" + i.ToString("D2"), i + 1, true, false)) {
                    accepted++;
                }
            }

            Debug.Log("SocketPayloadSelfTest: replay burst of " + ReplayBurstSize + " queued in " +
                      stopwatch.Elapsed.TotalMilliseconds.ToString("F1") + " ms of main-thread time (" + accepted +
                      " handed to the decoder). Duplicates are dropped on the worker, so a second burst should spawn nothing.");
        }

        private bool Inject(string captureId, int variation, bool asReplay, bool legacy) {
            byte[] png = LoadRandomSamplePng();
            if (png == null) {
                return false;
            }

            string json = BuildContractJson(png, captureId, variation, asReplay, legacy);

            // Times exactly what the socket path costs the frame: a hand-off, with no parsing or decoding.
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool accepted = ingestService != null && ingestService.InjectRawPayload(json);
            double mainThreadMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

            Debug.Log("SocketPayloadSelfTest: injected " + captureId + ", " + (json.Length / 1024) + " KB (" +
                      (legacy ? "schema v1, no personality" : "schema v2") + ", " +
                      (asReplay ? "replay" : "live") + ") | main-thread cost " + mainThreadMilliseconds.ToString("F2") +
                      " ms | queued=" + accepted);

            return accepted;
        }

        /// <summary>
        /// Exactly the shape the capture station emits, so the DTO mapping is genuinely exercised.
        ///
        /// legacy builds a schema v1 payload with no personality block, which is what an installation with
        /// history on disk replays. It is the only way to see the deterministic trait fallback of contract
        /// section 9 without waiting for Python to replay a real archive.
        /// </summary>
        private string BuildContractJson(byte[] png, string captureId, int variation, bool asReplay, bool legacy) {
            string base64 = System.Convert.ToBase64String(png);
            string replayField = asReplay ? "\"replay\": true," : string.Empty;

            string header = "{" +
                            "\"id\": \"" + captureId + "\"," +
                            "\"timestamp\": \"2026-09-01T16:35:56\"," +
                            "\"sequence\": " + variation + "," +
                            "\"png_base64\": \"" + base64 + "\"," +
                            "\"format\": \"png\"," +
                            "\"width\": 332," +
                            "\"height\": 153," +
                            "\"file_resolution\": [332, 153]," +
                            "\"bounding_box\": { \"x\": 237, \"y\": 0, \"width\": 332, \"height\": 153 }," +
                            "\"bounding_box_frame\": { \"x\": 537, \"y\": 150, \"width\": 332, \"height\": 153 }," +
                            "\"source_resolution\": [640, 480]," +
                            "\"foreground_area\": " + BuildForegroundArea(variation) + "," +
                            "\"processing_method\": \"opencv\"," +
                            "\"confidence\": 0.1147," +
                            replayField;

            if (legacy) {
                return header + "\"schema_version\": 1" + "}";
            }

            return header +
                   "\"schema_version\": 2," +
                   "\"object_type\": \"fish\"," +
                   "\"object_confidence\": 0.81," +
                   BuildFeaturesJson() + "," +
                   BuildPersonalityJson(variation) + "," +
                   "\"anatomy\": null" +
                   "}";
        }

        /// <summary>
        /// A spread of mask areas so successive injections differ in on-screen size, which is what proves
        /// the fish is sized from foreground_area rather than from the PNG - every sample shares a texture
        /// size, so if sizing were wrong they would all come out identical.
        /// </summary>
        private static int BuildForegroundArea(int variation) {
            float spread = Mathf.Repeat(variation * 0.37f, 1f);
            return Mathf.RoundToInt(Mathf.Lerp(45000f, 400000f, spread));
        }

        private static string BuildFeaturesJson() {
            return "\"features\": {" +
                   "\"aspect_ratio\": 1.86," +
                   "\"circularity\": 0.41," +
                   "\"solidity\": 0.78," +
                   "\"extent\": 0.62," +
                   "\"elongation\": 0.71," +
                   "\"orientation_deg\": -12.4," +
                   "\"perimeter\": 2840.5," +
                   "\"complexity\": 34.2," +
                   "\"vertex_count\": 46," +
                   "\"edge_density\": 0.19," +
                   "\"symmetry\": 0.72," +
                   "\"ink_coverage\": 0.31," +
                   "\"stroke_width\": 4.2," +
                   "\"spikiness\": 0.55," +
                   "\"color_count\": 3," +
                   "\"dominant_colors\": [" +
                   "{ \"hex\": \"#2f6fb8\", \"ratio\": 0.44 }," +
                   "{ \"hex\": \"#e8d24a\", \"ratio\": 0.21 }," +
                   "{ \"hex\": \"#1b1b1b\", \"ratio\": 0.09 }" +
                   "]," +
                   "\"mean_hue\": 208," +
                   "\"mean_sat\": 0.61," +
                   "\"mean_val\": 0.48," +
                   "\"colorfulness\": 38.4" +
                   "}";
        }

        /// <summary>
        /// Eight traits that differ on every injection, so a handful of key presses fills the tank with
        /// visibly distinct swimmers. Irrational-ish step sizes keep the values from falling into a short
        /// cycle, and each trait uses a different one so they do not move in lockstep.
        /// </summary>
        private static string BuildPersonalityJson(int variation) {
            float speed = Mathf.Repeat(variation * 0.618f, 1f);
            float curiosity = Mathf.Repeat(variation * 0.414f, 1f);
            float fear = Mathf.Repeat(variation * 0.732f, 1f);
            float social = Mathf.Repeat(variation * 0.271f, 1f);
            float aggression = Mathf.Repeat(variation * 0.577f, 1f);
            float grace = Mathf.Repeat(variation * 0.303f, 1f);
            float preferredDepth = Mathf.Repeat(variation * 0.487f, 1f);
            float rarityScore = Mathf.Repeat(variation * 0.161f, 1f);

            return "\"personality\": {" +
                   "\"speed\": " + Number(speed) + "," +
                   "\"curiosity\": " + Number(curiosity) + "," +
                   "\"fear\": " + Number(fear) + "," +
                   "\"social\": " + Number(social) + "," +
                   "\"aggression\": " + Number(aggression) + "," +
                   "\"grace\": " + Number(grace) + "," +
                   "\"preferred_depth\": " + Number(preferredDepth) + "," +
                   "\"rarity_score\": " + Number(rarityScore) + "," +
                   "\"rarity_tier\": \"" + TierName(rarityScore) + "\"" +
                   "}";
        }

        /// <summary>
        /// Invariant culture, always. On a machine whose locale uses a decimal comma the default
        /// ToString would emit 0,81 and produce JSON that parses as two values or not at all - a bug that
        /// never shows up on the developer's machine and always shows up on the installation's.
        /// </summary>
        private static string Number(float value) {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string TierName(float rarityScore) {
            if (rarityScore >= 0.95f) {
                return "legendary";
            }

            if (rarityScore >= 0.80f) {
                return "rare";
            }

            if (rarityScore >= 0.55f) {
                return "uncommon";
            }

            return "common";
        }

        private byte[] LoadRandomSamplePng() {
            if (samplePaths == null || samplePaths.Length == 0) {
                string folder = Path.Combine(Application.streamingAssetsPath, samplePngFolder);

                if (!Directory.Exists(folder)) {
                    Debug.LogError("SocketPayloadSelfTest: no sample folder at " + folder);
                    return null;
                }

                samplePaths = Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly);
            }

            if (samplePaths.Length == 0) {
                Debug.LogError("SocketPayloadSelfTest: the sample folder contains no PNGs.");
                return null;
            }

            return File.ReadAllBytes(samplePaths[Random.Range(0, samplePaths.Length)]);
        }
    }
}

#endif
