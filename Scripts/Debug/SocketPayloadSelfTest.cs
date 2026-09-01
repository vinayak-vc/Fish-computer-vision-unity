#if !BESTHTTP_DISABLE_SOCKETIO

using System.Diagnostics;
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
        }

        /// <summary> Injects one synthetic capture. Returns false when no sample PNG could be read. </summary>
        public bool InjectOne(bool asReplay) {
            byte[] png = LoadRandomSamplePng();
            if (png == null) {
                return false;
            }

            string json = BuildContractJson(png, asReplay);

            // Times exactly what the socket path costs the frame: a hand-off, with no parsing or decoding.
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool accepted = ingestService != null && ingestService.InjectRawPayload(json);
            double mainThreadMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

            Debug.Log("SocketPayloadSelfTest: injected " + (json.Length / 1024) + " KB payload (" +
                      (asReplay ? "replay" : "live") + ") | main-thread cost " + mainThreadMilliseconds.ToString("F2") +
                      " ms | accepted=" + accepted);

            return accepted;
        }

        /// <summary> Mimics the burst the capture station sends on connect. </summary>
        public void InjectReplayBurst() {
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < ReplayBurstSize; i++) {
                InjectOne(true);
            }

            Debug.Log("SocketPayloadSelfTest: replay burst of " + ReplayBurstSize + " queued in " +
                      stopwatch.Elapsed.TotalMilliseconds.ToString("F1") + " ms of main-thread time. " +
                      "Decoding and spawning are spread across the following frames.");
        }

        /// <summary> Exactly the shape the capture station emits, so the DTO mapping is genuinely exercised. </summary>
        private string BuildContractJson(byte[] png, bool asReplay) {
            sequence++;

            string base64 = System.Convert.ToBase64String(png);
            string id = "fish_selftest_" + sequence.ToString("D3");
            string replayField = asReplay ? "\"replay\": true," : string.Empty;

            return "{" +
                   "\"id\": \"" + id + "\"," +
                   "\"timestamp\": \"2026-09-01T16:35:56\"," +
                   "\"png_base64\": \"" + base64 + "\"," +
                   "\"format\": \"png\"," +
                   "\"width\": 332," +
                   "\"height\": 153," +
                   "\"file_resolution\": [332, 153]," +
                   "\"bounding_box\": { \"x\": 237, \"y\": 0, \"width\": 332, \"height\": 153 }," +
                   "\"bounding_box_frame\": { \"x\": 537, \"y\": 150, \"width\": 332, \"height\": 153 }," +
                   "\"source_resolution\": [640, 480]," +
                   "\"foreground_area\": 20395," +
                   "\"processing_method\": \"opencv\"," +
                   "\"confidence\": 0.1147," +
                   replayField +
                   "\"schema_version\": 1" +
                   "}";
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
