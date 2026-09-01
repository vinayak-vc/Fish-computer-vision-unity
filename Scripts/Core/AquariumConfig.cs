using System.IO;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary> Single source of truth for every tunable value in the aquarium. Created via Assets > Create > Fish Aquarium > Aquarium Config. </summary>
    [CreateAssetMenu(fileName = "AquariumConfig", menuName = "Fish Aquarium/Aquarium Config", order = 0)]
    public sealed class AquariumConfig : ScriptableObject {
        [Header("Runtime Mode")]
        [SerializeField] private AquariumRuntimeMode runtimeMode = AquariumRuntimeMode.Development;
        [SerializeField] private bool fullscreenInProduction = true;
        [SerializeField] private int targetFrameRate = 60;
        [SerializeField] private int vSyncCount = 1;
        [SerializeField] private bool allowExternalSettingsOverride = true;

        [Header("Fish Input Folder")]
        [Tooltip("Root the relative path below is resolved against. Absolute uses the explicit path field instead.")]
        [SerializeField] private InputFolderRoot inputFolderRoot = InputFolderRoot.StreamingAssets;
        [SerializeField] private string inputFolderRelativePath = "FishInput";
        [SerializeField] private string inputFolderAbsolutePath = string.Empty;
        [SerializeField] private string fileSearchPattern = "*.png";
        [SerializeField] private bool createInputFolderIfMissing = true;

        [Header("Fish Input Behaviour")]
        [SerializeField] private bool loadExistingFishOnStartup = true;
        [SerializeField] private bool watchForNewFiles = true;
        [Tooltip("Seconds a newly detected file must stay unchanged before Unity attempts to read it.")]
        [SerializeField] private float fileStabilitySeconds = 0.35f;
        [Tooltip("Seconds a pending file may stay unreadable before it is abandoned with a warning.")]
        [SerializeField] private float fileReadTimeoutSeconds = 20f;
        [Tooltip("Safety-net rescan interval in seconds. 0 disables it; the FileSystemWatcher stays the primary source.")]
        [SerializeField] private float periodicRescanSeconds = 15f;
        [Tooltip("Maximum PNGs decoded per frame. Keeps a burst of new files from stalling the render loop.")]
        [SerializeField] private int maxFishLoadsPerFrame = 1;
        [Tooltip("Section 45 default: a deleted PNG leaves its already-spawned fish alone.")]
        [SerializeField] private bool removeFishWhenSourceFileDeleted = false;

        [Header("Test Fish (development only)")]
        [SerializeField] private bool loadTestFishInDevelopment = true;
        [SerializeField] private string testFishFolderRelativePath = "Sample";

        [Header("Aquarium Population")]
        [SerializeField] private int maxFishCount = 30;
        [SerializeField] private FishOverflowStrategy overflowStrategy = FishOverflowStrategy.RemoveOldest;
        [SerializeField] private FishSpawnStrategy spawnStrategy = FishSpawnStrategy.EdgeEntry;
        [SerializeField] private float spawnAnimationDuration = 0.7f;
        [SerializeField] private float despawnAnimationDuration = 0.5f;
        [SerializeField] private float spawnStartScaleMultiplier = 0.55f;

        [Header("Fish Size Normalisation")]
        [Tooltip("World-unit length of the longest side of the fish. Aspect ratio of the source PNG is always preserved.")]
        [SerializeField] private FloatRange fishWorldSizeRange = new FloatRange(1.7f, 2.7f);
        [SerializeField] private float spritePixelsPerUnit = 100f;

        [Header("Fish Movement")]
        [SerializeField] private FloatRange speedRange = new FloatRange(0.5f, 1.5f);
        [SerializeField] private FloatRange turnSpeedRange = new FloatRange(30f, 90f);
        [SerializeField] private FloatRange accelerationRange = new FloatRange(0.8f, 2.0f);
        [Tooltip("Peak heading deviation in degrees driven by Perlin noise. Keeps motion organic without jitter.")]
        [SerializeField] private float directionNoiseDegrees = 22f;
        [Tooltip("Perlin sampling rate. Low values give slow, smooth wander.")]
        [SerializeField] private float directionNoiseFrequency = 0.28f;
        [Tooltip("Scales the vertical part of target seeking. Below 1 the fish cruise in flatter, more fish-like arcs instead of diving.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float verticalSteeringDamping = 0.55f;
        [SerializeField] private float targetReachDistance = 0.45f;
        [SerializeField] private float minimumTargetTravelDistance = 2.2f;
        [SerializeField] private FloatRange idleProbabilityRange = new FloatRange(0.05f, 0.25f);
        [SerializeField] private FloatRange idleDurationRange = new FloatRange(0.6f, 2.2f);
        [Tooltip("Distance from the aquarium edge at which fish start steering back inwards.")]
        [SerializeField] private float boundsAvoidanceMargin = 1.1f;
        [SerializeField] private float boundsAvoidanceStrength = 2.4f;
        [SerializeField] private float boundsPadding = 0.35f;

        [Header("Fish Separation")]
        [SerializeField] private bool separationEnabled = true;
        [SerializeField] private float separationRadius = 1.0f;
        [SerializeField] private float separationStrength = 0.9f;

        [Header("Fish Orientation")]
        [Tooltip("Describes the source PNG, not the swimming direction: tick it only when the drawings have their nose pointing RIGHT. The sample drawings face left, so this is off by default. Getting it wrong makes every fish swim tail first.")]
        [SerializeField] private bool artworkFacesRight = false;
        [Tooltip("Horizontal direction required before the sprite flips. Prevents flicker when swimming near-vertically.")]
        [SerializeField] private float facingFlipThreshold = 0.16f;
        [Tooltip("Maximum nose-up or nose-down tilt. The fish never rolls upside down.")]
        [SerializeField] private float maxPitchAngle = 32f;
        [SerializeField] private float pitchSmoothing = 6f;

        [Header("Fish Swim Animation")]
        [SerializeField] private FloatRange swimAmplitudeRange = new FloatRange(0.02f, 0.06f);
        [SerializeField] private FloatRange swimFrequencyRange = new FloatRange(0.8f, 1.8f);
        [SerializeField] private float bodyRollDegrees = 4.5f;
        [SerializeField] private float breathingScaleAmount = 0.025f;

        [Header("Depth")]
        [SerializeField] private FloatRange depthRange = new FloatRange(0f, 1f);
        [Tooltip("How much a fully background fish shrinks. 0.4 renders it at 60 percent of its normalised size.")]
        [SerializeField] private float depthScaleInfluence = 0.32f;
        [Tooltip("How much a fully background fish fades. 0.25 renders it at 75 percent opacity.")]
        [SerializeField] private float depthAlphaInfluence = 0.25f;
        [SerializeField] private float depthSpeedInfluence = 0.3f;
        [SerializeField] private int foregroundSortingOrder = 200;
        [SerializeField] private int backgroundSortingOrder = 20;

        [Header("Socket.IO Source")]
        [Tooltip("Receive fish from the Python capture station over Socket.IO. Independent of the folder watcher; both can run at once.")]
        [SerializeField] private bool socketSourceEnabled = true;
        [SerializeField] private bool socketAutoConnect = true;
        [SerializeField] private string socketHost = "127.0.0.1";
        [SerializeField] private int socketPort = 8765;
        [Tooltip("Socket.IO namespace. The capture station uses the default namespace.")]
        [SerializeField] private string socketNamespace = "/";
        [SerializeField] private string socketEventName = "fish_captured";
        [SerializeField] private SocketTransportMode socketTransport = SocketTransportMode.PollingThenUpgradeToWebSocket;
        [Tooltip("0 or below means keep retrying forever, which is what an unattended installation wants.")]
        [SerializeField] private int socketReconnectionAttempts = 0;
        [SerializeField] private float socketReconnectionDelaySeconds = 1f;
        [SerializeField] private float socketReconnectionDelayMaxSeconds = 10f;
        [SerializeField] private float socketConnectTimeoutSeconds = 20f;
        [Tooltip("Schema the payload contract was written against. A different value on the wire is logged once as a warning.")]
        [SerializeField] private int expectedSchemaVersion = 1;

        [Header("Debug")]
        [SerializeField] private bool debugOverlayVisibleOnStart = true;
        [SerializeField] private bool drawAquariumBoundsGizmo = true;


        public AquariumRuntimeMode RuntimeMode {
            get { return runtimeMode; }
        }

        public bool FullscreenInProduction {
            get { return fullscreenInProduction; }
        }

        public int TargetFrameRate {
            get { return targetFrameRate; }
        }

        public int VSyncCount {
            get { return vSyncCount; }
        }

        public bool AllowExternalSettingsOverride {
            get { return allowExternalSettingsOverride; }
        }

        public InputFolderRoot InputFolderRootMode {
            get { return inputFolderRoot; }
        }

        public string InputFolderRelativePath {
            get { return inputFolderRelativePath; }
        }

        public string InputFolderAbsolutePath {
            get { return inputFolderAbsolutePath; }
        }

        public string FileSearchPattern {
            get { return fileSearchPattern; }
        }

        public bool CreateInputFolderIfMissing {
            get { return createInputFolderIfMissing; }
        }

        public bool LoadExistingFishOnStartup {
            get { return loadExistingFishOnStartup; }
        }

        public bool WatchForNewFiles {
            get { return watchForNewFiles; }
        }

        public float FileStabilitySeconds {
            get { return fileStabilitySeconds; }
        }

        public float FileReadTimeoutSeconds {
            get { return fileReadTimeoutSeconds; }
        }

        public float PeriodicRescanSeconds {
            get { return periodicRescanSeconds; }
        }

        public int MaxFishLoadsPerFrame {
            get { return maxFishLoadsPerFrame; }
        }

        public bool RemoveFishWhenSourceFileDeleted {
            get { return removeFishWhenSourceFileDeleted; }
        }

        public bool LoadTestFishInDevelopment {
            get { return loadTestFishInDevelopment; }
        }

        public string TestFishFolderRelativePath {
            get { return testFishFolderRelativePath; }
        }

        public int MaxFishCount {
            get { return maxFishCount; }
        }

        public FishOverflowStrategy OverflowStrategy {
            get { return overflowStrategy; }
        }

        public FishSpawnStrategy SpawnStrategy {
            get { return spawnStrategy; }
        }

        public float SpawnAnimationDuration {
            get { return spawnAnimationDuration; }
        }

        public float DespawnAnimationDuration {
            get { return despawnAnimationDuration; }
        }

        public float SpawnStartScaleMultiplier {
            get { return spawnStartScaleMultiplier; }
        }

        public FloatRange FishWorldSizeRange {
            get { return fishWorldSizeRange; }
        }

        public float SpritePixelsPerUnit {
            get { return spritePixelsPerUnit; }
        }

        public FloatRange SpeedRange {
            get { return speedRange; }
        }

        public FloatRange TurnSpeedRange {
            get { return turnSpeedRange; }
        }

        public FloatRange AccelerationRange {
            get { return accelerationRange; }
        }

        public float DirectionNoiseDegrees {
            get { return directionNoiseDegrees; }
        }

        public float DirectionNoiseFrequency {
            get { return directionNoiseFrequency; }
        }

        public float VerticalSteeringDamping {
            get { return verticalSteeringDamping; }
        }

        public float TargetReachDistance {
            get { return targetReachDistance; }
        }

        public float MinimumTargetTravelDistance {
            get { return minimumTargetTravelDistance; }
        }

        public FloatRange IdleProbabilityRange {
            get { return idleProbabilityRange; }
        }

        public FloatRange IdleDurationRange {
            get { return idleDurationRange; }
        }

        public float BoundsAvoidanceMargin {
            get { return boundsAvoidanceMargin; }
        }

        public float BoundsAvoidanceStrength {
            get { return boundsAvoidanceStrength; }
        }

        public float BoundsPadding {
            get { return boundsPadding; }
        }

        public bool SeparationEnabled {
            get { return separationEnabled; }
        }

        public float SeparationRadius {
            get { return separationRadius; }
        }

        public float SeparationStrength {
            get { return separationStrength; }
        }

        public bool ArtworkFacesRight {
            get { return artworkFacesRight; }
        }

        public float FacingFlipThreshold {
            get { return facingFlipThreshold; }
        }

        public float MaxPitchAngle {
            get { return maxPitchAngle; }
        }

        public float PitchSmoothing {
            get { return pitchSmoothing; }
        }

        public FloatRange SwimAmplitudeRange {
            get { return swimAmplitudeRange; }
        }

        public FloatRange SwimFrequencyRange {
            get { return swimFrequencyRange; }
        }

        public float BodyRollDegrees {
            get { return bodyRollDegrees; }
        }

        public float BreathingScaleAmount {
            get { return breathingScaleAmount; }
        }

        public FloatRange DepthRange {
            get { return depthRange; }
        }

        public float DepthScaleInfluence {
            get { return depthScaleInfluence; }
        }

        public float DepthAlphaInfluence {
            get { return depthAlphaInfluence; }
        }

        public float DepthSpeedInfluence {
            get { return depthSpeedInfluence; }
        }

        public int ForegroundSortingOrder {
            get { return foregroundSortingOrder; }
        }

        public int BackgroundSortingOrder {
            get { return backgroundSortingOrder; }
        }

        public bool DebugOverlayVisibleOnStart {
            get { return debugOverlayVisibleOnStart; }
        }

        public bool DrawAquariumBoundsGizmo {
            get { return drawAquariumBoundsGizmo; }
        }

        public bool SocketSourceEnabled {
            get { return socketSourceEnabled; }
        }

        public bool SocketAutoConnect {
            get { return socketAutoConnect; }
        }

        public string SocketHost {
            get { return socketHost; }
        }

        public int SocketPort {
            get { return socketPort; }
        }

        public string SocketNamespace {
            get { return socketNamespace; }
        }

        public string SocketEventName {
            get { return socketEventName; }
        }

        public SocketTransportMode SocketTransport {
            get { return socketTransport; }
        }

        public int SocketReconnectionAttempts {
            get { return socketReconnectionAttempts; }
        }

        public float SocketReconnectionDelaySeconds {
            get { return socketReconnectionDelaySeconds; }
        }

        public float SocketReconnectionDelayMaxSeconds {
            get { return socketReconnectionDelayMaxSeconds; }
        }

        public float SocketConnectTimeoutSeconds {
            get { return socketConnectTimeoutSeconds; }
        }

        public int ExpectedSchemaVersion {
            get { return expectedSchemaVersion; }
        }

        public bool IsDevelopmentMode {
            get { return runtimeMode == AquariumRuntimeMode.Development; }
        }

        /// <summary> Absolute path Unity watches for fish PNGs, always derived from the serialised root plus relative path. </summary>
        public string ResolveInputFolderPath() {
            if (inputFolderRoot == InputFolderRoot.Absolute) {
                return NormalizePath(inputFolderAbsolutePath);
            }

            string root = inputFolderRoot == InputFolderRoot.PersistentData ? Application.persistentDataPath : Application.streamingAssetsPath;
            return NormalizePath(Path.Combine(root, inputFolderRelativePath));
        }

        /// <summary> Socket.IO endpoint of the capture station, built from the configured host and port. </summary>
        public string ResolveSocketUrl() {
            string host = string.IsNullOrWhiteSpace(socketHost) ? "127.0.0.1" : socketHost.Trim();
            return "http://" + host + ":" + socketPort + "/socket.io/";
        }

        /// <summary> Absolute path of the development-only sample fish folder under StreamingAssets. </summary>
        public string ResolveTestFishFolderPath() {
            return NormalizePath(Path.Combine(Application.streamingAssetsPath, testFishFolderRelativePath));
        }

        /// <summary> Applied by AppConfig when an external settings file overrides the authored values. </summary>
        public void ApplyExternalOverrides(string overrideInputFolder, int overrideMaxFishCount, AquariumRuntimeMode overrideRuntimeMode) {
            if (!string.IsNullOrWhiteSpace(overrideInputFolder)) {
                inputFolderRoot = InputFolderRoot.Absolute;
                inputFolderAbsolutePath = overrideInputFolder;
            }

            if (overrideMaxFishCount > 0) {
                maxFishCount = overrideMaxFishCount;
            }

            runtimeMode = overrideRuntimeMode;
        }

        private static string NormalizePath(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return string.Empty;
            }

            return PathUtility.NormalizeFolder(path);
        }

        private void OnValidate() {
            maxFishCount = Mathf.Max(1, maxFishCount);
            maxFishLoadsPerFrame = Mathf.Max(1, maxFishLoadsPerFrame);
            spritePixelsPerUnit = Mathf.Max(1f, spritePixelsPerUnit);
            fileStabilitySeconds = Mathf.Max(0f, fileStabilitySeconds);
            fileReadTimeoutSeconds = Mathf.Max(1f, fileReadTimeoutSeconds);
            periodicRescanSeconds = Mathf.Max(0f, periodicRescanSeconds);
            spawnAnimationDuration = Mathf.Max(0.01f, spawnAnimationDuration);
            despawnAnimationDuration = Mathf.Max(0.01f, despawnAnimationDuration);
            spawnStartScaleMultiplier = Mathf.Clamp(spawnStartScaleMultiplier, 0.01f, 1f);
            verticalSteeringDamping = Mathf.Clamp(verticalSteeringDamping, 0.05f, 1f);
            targetReachDistance = Mathf.Max(0.05f, targetReachDistance);
            minimumTargetTravelDistance = Mathf.Max(0.1f, minimumTargetTravelDistance);
            boundsPadding = Mathf.Max(0f, boundsPadding);
            boundsAvoidanceMargin = Mathf.Max(0.01f, boundsAvoidanceMargin);
            separationRadius = Mathf.Max(0.01f, separationRadius);
            facingFlipThreshold = Mathf.Clamp(facingFlipThreshold, 0f, 0.9f);
            maxPitchAngle = Mathf.Clamp(maxPitchAngle, 0f, 80f);
            depthScaleInfluence = Mathf.Clamp01(depthScaleInfluence);
            depthAlphaInfluence = Mathf.Clamp01(depthAlphaInfluence);
            depthSpeedInfluence = Mathf.Clamp01(depthSpeedInfluence);

            socketPort = Mathf.Clamp(socketPort, 1, 65535);
            socketReconnectionDelaySeconds = Mathf.Max(0.1f, socketReconnectionDelaySeconds);
            socketReconnectionDelayMaxSeconds = Mathf.Max(socketReconnectionDelaySeconds, socketReconnectionDelayMaxSeconds);
            socketConnectTimeoutSeconds = Mathf.Max(1f, socketConnectTimeoutSeconds);

            if (string.IsNullOrWhiteSpace(socketNamespace)) {
                socketNamespace = "/";
            }

            if (string.IsNullOrWhiteSpace(socketEventName)) {
                socketEventName = "fish_captured";
            }

            if (string.IsNullOrWhiteSpace(fileSearchPattern)) {
                fileSearchPattern = "*.png";
            }

            fishWorldSizeRange = fishWorldSizeRange.Normalized();
            speedRange = speedRange.Normalized();
            turnSpeedRange = turnSpeedRange.Normalized();
            accelerationRange = accelerationRange.Normalized();
            idleProbabilityRange = idleProbabilityRange.Normalized();
            idleDurationRange = idleDurationRange.Normalized();
            swimAmplitudeRange = swimAmplitudeRange.Normalized();
            swimFrequencyRange = swimFrequencyRange.Normalized();
            depthRange = depthRange.Normalized();
        }
    }
}
