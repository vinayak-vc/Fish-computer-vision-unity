using System.Collections.Generic;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;
using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Bridges the Socket.IO capture feed into the aquarium. It owns the transport and the decode queue,
    /// and every Unity call it makes happens here on the main thread.
    ///
    /// Population limits, texture lifetime and swimming behaviour are not reimplemented: a decoded payload
    /// goes to the same FishFactory the folder watcher uses, so both sources share one set of rules.
    /// </summary>
    public sealed class SocketFishIngestService : MonoBehaviour, IFishIngestService {
        private const float ErrorSummaryIntervalSeconds = 30f;

        [SerializeField] private AquariumConfig config;
        [SerializeField] private FishFactory fishFactory;
        [SerializeField] private AquariumManager aquariumManager;

        private readonly List<FishPayloadDecodeQueue.DecodedFish> drained = new List<FishPayloadDecodeQueue.DecodedFish>();
        private readonly FishPayloadDecodeQueue decodeQueue = new FishPayloadDecodeQueue();

        private ISocketIoTransport transport;
        private string lastErrorMessage = string.Empty;
        private int suppressedErrorCount;
        private float nextErrorSummaryTime;
        private string lastLoadedFileName = string.Empty;
        private int spawnedCount;
        private int rejectedCount;
        private bool schemaWarningLogged;
        private bool started;

        public string InputFolderPath {
            get { return config != null ? config.ResolveSocketUrl() : string.Empty; }
        }

        public bool IsWatcherActive {
            get { return transport != null && transport.IsConnected; }
        }

        public string LastLoadedFileName {
            get { return lastLoadedFileName; }
        }

        public int PendingFileCount {
            get { return decodeQueue.PendingCount + decodeQueue.ReadyCount; }
        }

        public int ProcessedFileCount {
            get { return spawnedCount; }
        }

        /// <summary> Captures the socket accepted but the aquarium turned away, usually because it was at its cap. </summary>
        public int RejectedCount {
            get { return rejectedCount; }
        }

        private void Start() {
            if (config == null || fishFactory == null || aquariumManager == null) {
                Debug.LogError("SocketFishIngestService: dependencies are missing, no fish will arrive over the socket.");
                return;
            }

            if (!config.SocketSourceEnabled) {
                Debug.Log("SocketFishIngestService: socket source is disabled in the config.");
                return;
            }

            started = true;

            if (config.SocketAutoConnect) {
                Connect();
            } else {
                Debug.Log("SocketFishIngestService: auto-connect is off; call Connect() to attach to " + config.ResolveSocketUrl());
            }
        }

        private void Update() {
            if (!started) {
                return;
            }

            LogDecodeFailures();
            SpawnDecodedFish();
        }

        private void OnDestroy() {
            Disconnect();
            decodeQueue.Clear();
        }

        /// <summary> Opens the connection. Safe to call again; an existing connection is closed first. </summary>
        public void Connect() {
            if (config == null) {
                return;
            }

            Disconnect();

            transport = CreateTransport();
            if (transport == null) {
                return;
            }

            transport.Connected += HandleConnected;
            transport.Disconnected += HandleDisconnected;
            transport.Error += HandleError;
            transport.FishPayloadReceived += HandleFishPayloadReceived;

            SocketIoConnectionSettings settings = new SocketIoConnectionSettings();
            settings.Url = config.ResolveSocketUrl();
            settings.NamespaceName = config.SocketNamespace;
            settings.EventName = config.SocketEventName;
            settings.PreferWebSocketOnly = config.SocketTransport == SocketTransportMode.WebSocketOnly;
            settings.ReconnectionAttempts = config.SocketReconnectionAttempts;
            settings.ReconnectionDelaySeconds = config.SocketReconnectionDelaySeconds;
            settings.ReconnectionDelayMaxSeconds = config.SocketReconnectionDelayMaxSeconds;
            settings.ConnectTimeoutSeconds = config.SocketConnectTimeoutSeconds;

            Debug.Log("SocketFishIngestService: connecting to " + settings.Url + " (namespace " + settings.NamespaceName + ", event " + settings.EventName + ")");
            transport.Connect(settings);
        }

        public void Disconnect() {
            if (transport == null) {
                return;
            }

            transport.Connected -= HandleConnected;
            transport.Disconnected -= HandleDisconnected;
            transport.Error -= HandleError;
            transport.FishPayloadReceived -= HandleFishPayloadReceived;
            transport.Dispose();
            transport = null;
        }

        /// <summary> Reconnects. Exposed so the debug overlay F4 key is useful for the socket source too. </summary>
        public void RescanInputFolder() {
            Connect();
        }

        /// <summary> Nothing to forget: the capture station gives every event a unique id, so there is no dedupe cache. </summary>
        public void ResetProcessedFileCache() {
            spawnedCount = 0;
            rejectedCount = 0;
            lastLoadedFileName = string.Empty;
        }

        /// <summary> Not a socket concern; the folder-based service owns the development sample fish. </summary>
        public bool SpawnTestFish() {
            return false;
        }

        /// <summary>
        /// Injects raw capture JSON as if the socket had delivered it, exercising the real path: queue to a
        /// worker, parse and decode there, spawn on the main thread.
        /// </summary>
        public bool InjectRawPayload(string rawJson) {
            EnsureStarted();

            string reason;
            if (!decodeQueue.Submit(rawJson, out reason)) {
                Debug.LogWarning("SocketFishIngestService: ignoring injected capture - " + reason);
                return false;
            }

            return true;
        }

        /// <summary> Injects an already-parsed payload. Used by the edit-mode tests. </summary>
        public bool InjectPayload(FishCaptureMessage message) {
            EnsureStarted();
            return TryAcceptPayload(message);
        }

        private void EnsureStarted() {
            if (!started) {
                started = config != null && fishFactory != null && aquariumManager != null;
            }
        }

        private ISocketIoTransport CreateTransport() {
#if !BESTHTTP_DISABLE_SOCKETIO
            return new BestHttpSocketIoTransport();
#else
            Debug.LogError("SocketFishIngestService: BestHTTP Socket.IO support is compiled out (BESTHTTP_DISABLE_SOCKETIO). No fish can arrive over the socket.");
            return null;
#endif
        }

        private void HandleConnected() {
            lastErrorMessage = string.Empty;
            suppressedErrorCount = 0;
            Debug.Log("SocketFishIngestService: connected to " + config.ResolveSocketUrl() + ". Expecting a replay burst of recent captures.");
        }

        private void HandleDisconnected(string reason) {
            Debug.LogWarning("SocketFishIngestService: disconnected (" + reason + "). Automatic reconnection is enabled.");
        }

        /// <summary>
        /// A capture station that is simply not running yet produces the same connection error once a second.
        /// Left unthrottled that is thousands of identical lines an hour, which buries anything real, so
        /// repeats are collapsed into a periodic summary and the count is kept.
        /// </summary>
        private void HandleError(string message) {
            if (string.Equals(message, lastErrorMessage)) {
                suppressedErrorCount++;

                if (Time.time < nextErrorSummaryTime) {
                    return;
                }

                nextErrorSummaryTime = Time.time + ErrorSummaryIntervalSeconds;
                Debug.LogWarning("SocketFishIngestService: still cannot reach " + config.ResolveSocketUrl() + " - " + message +
                                 " (" + suppressedErrorCount + " further attempts since the last message)");
                suppressedErrorCount = 0;
                return;
            }

            lastErrorMessage = message;
            suppressedErrorCount = 0;
            nextErrorSummaryTime = Time.time + ErrorSummaryIntervalSeconds;
            Debug.LogError("SocketFishIngestService: socket error - " + message);
        }

        /// <summary>
        /// Main thread, straight off the socket. Only validation and a hand-off to the worker happen here;
        /// the megabyte of base64 is not touched.
        /// </summary>
        /// <summary>
        /// Main thread, straight off the socket. The payload is still raw JSON here on purpose: it goes
        /// to a worker without being parsed, so a megabyte capture costs the frame almost nothing.
        /// </summary>
        private void HandleFishPayloadReceived(string rawJson) {
            string reason;
            if (!decodeQueue.Submit(rawJson, out reason)) {
                Debug.LogWarning("SocketFishIngestService: ignoring capture - " + reason);
            }
        }

        private bool TryAcceptPayload(FishCaptureMessage message) {
            if (message == null) {
                return false;
            }

            string reason;
            if (!decodeQueue.Submit(message, out reason)) {
                Debug.LogWarning("SocketFishIngestService: ignoring capture - " + reason);
                return false;
            }

            return true;
        }

        /// <summary> Checked once a payload reaches the main thread, since the parse now happens on a worker. </summary>
        private void WarnOnUnexpectedSchema(int schemaVersion) {
            if (schemaWarningLogged || config == null || schemaVersion == config.ExpectedSchemaVersion) {
                return;
            }

            schemaWarningLogged = true;
            Debug.LogWarning("SocketFishIngestService: payload schema_version is " + schemaVersion + " but this client was written against " + config.ExpectedSchemaVersion + ". Fields may have changed; further occurrences are not logged.");
        }

        private void LogDecodeFailures() {
            string failure;
            while (decodeQueue.TryTakeFailure(out failure)) {
                Debug.LogWarning("SocketFishIngestService: " + failure);
            }
        }

        /// <summary>
        /// Turns decoded payloads into fish, at most MaxFishLoadsPerFrame each frame. That cap is what keeps
        /// the ten-event replay burst from landing in a single frame.
        /// </summary>
        private void SpawnDecodedFish() {
            int budget = config.MaxFishLoadsPerFrame;
            if (decodeQueue.Drain(budget, drained) == 0) {
                return;
            }

            for (int i = 0; i < drained.Count; i++) {
                SpawnOne(drained[i]);
            }
        }

        private void SpawnOne(FishPayloadDecodeQueue.DecodedFish decoded) {
            WarnOnUnexpectedSchema(decoded.SchemaVersion);

            Sprite sprite;
            string error;

            if (!FishSpriteCache.TryAcquireFromBytes(decoded.Id, decoded.PngBytes, config.SpritePixelsPerUnit, out sprite, out error)) {
                Debug.LogWarning("SocketFishIngestService: could not build a sprite for " + decoded.Id + " - " + error);
                return;
            }

            FishController fish = fishFactory.CreateFish(sprite, decoded.Id, decoded.IsReplay);
            if (fish == null) {
                // The aquarium turned it away, so give the texture straight back rather than leaking it.
                FishSpriteCache.Release(decoded.Id);
                rejectedCount++;
                return;
            }

            spawnedCount++;
            lastLoadedFileName = decoded.Id + (decoded.IsReplay ? " (replay)" : string.Empty);
        }
    }
}
