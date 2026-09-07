using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Turns a loaded Sprite into a living fish: sizes it from what Python measured, applies its
    /// personality, chooses an entry point and hands the finished instance to AquariumManager.
    ///
    /// Both ingest sources come through here, so population limits, texture lifetime and behaviour have
    /// exactly one implementation whether a drawing arrived over the socket or as a file.
    /// </summary>
    public sealed class FishFactory : MonoBehaviour {
        /// <summary> Passed as foreground_area when the caller has no measurement, so size falls back to the identity roll. </summary>
        public const int UnknownForegroundArea = 0;

        [SerializeField] private AquariumConfig config;
        [SerializeField] private AquariumManager aquariumManager;
        [SerializeField] private AquariumBounds bounds;
        [Tooltip("Optional. When empty the factory builds an equivalent fish hierarchy in code.")]
        [SerializeField] private FishController fishPrefab;

        private void Awake() {
            if (config == null) {
                Debug.LogError("FishFactory: no AquariumConfig assigned.");
            }

            if (aquariumManager == null) {
                Debug.LogError("FishFactory: no AquariumManager assigned.");
            }

            if (bounds == null && aquariumManager != null) {
                bounds = aquariumManager.Bounds;
            }
        }

        /// <summary> Creates one fish from an already-loaded sprite. Returns null when the aquarium refused it. </summary>
        public FishController CreateFish(Sprite sprite, string sourceFilePath) {
            return CreateFish(sprite, sourceFilePath, false);
        }

        /// <summary>
        /// As above, but a silent fish skips the entrance animation and is placed anywhere in the aquarium
        /// rather than swimming in from an edge. Used for replayed captures.
        ///
        /// No personality is supplied, so it is derived from the source path. That is the documented
        /// fallback of contract section 9, and it is what every folder-watched fish uses.
        /// </summary>
        public FishController CreateFish(Sprite sprite, string sourceFilePath, bool spawnSilently) {
            return CreateFish(sprite, sourceFilePath, FishTraitFallback.FromIdentity(sourceFilePath), UnknownForegroundArea, spawnSilently);
        }

        /// <summary>
        /// The full entry point, used by the socket ingest, where Python has already sent a personality and
        /// a mask area. traits must never be null: a fish without a personality has no behaviour at all.
        /// </summary>
        public FishController CreateFish(Sprite sprite, string sourceKey, FishTraits traits, int foregroundArea, bool spawnSilently) {
            if (sprite == null) {
                Debug.LogError("FishFactory: CreateFish called with a null sprite.");
                return null;
            }

            if (config == null || aquariumManager == null || bounds == null) {
                Debug.LogError("FishFactory: cannot create a fish, dependencies are missing.");
                return null;
            }

            if (traits == null) {
                Debug.LogWarning("FishFactory: no traits supplied for " + sourceKey + "; deriving them from its identity.");
                traits = FishTraitFallback.FromIdentity(sourceKey);
            }

            if (!aquariumManager.TryMakeRoomForNewFish()) {
                return null;
            }

            string fileName = string.IsNullOrEmpty(sourceKey) ? sprite.name : Path.GetFileName(sourceKey);

            FishData data = new FishData(aquariumManager.ReserveFishId(), fileName, sourceKey, sprite, traits);
            data.ApplyTraitProfile(config);
            data.SetWorldScale(CalculateWorldScale(sprite, data, foregroundArea));

            Vector2 initialHeading;
            data.SetSpawnPosition(ChooseSpawnPosition(data, out initialHeading, spawnSilently));

            FishController fish = InstantiateFish();
            if (fish == null) {
                return null;
            }

            fish.name = "Fish_" + data.Id.ToString("D3") + " (" + fileName + ")";
            fish.transform.SetParent(aquariumManager.FishContainer, false);

            fish.Initialize(data, config, bounds, aquariumManager, aquariumManager.ReserveSpawnOrder(), initialHeading, spawnSilently);
            aquariumManager.RegisterFish(fish);

            return fish;
        }

        /// <summary>
        /// Uniform scale that brings any source resolution to a world size without distorting it.
        /// The size comes from the mask area Python measured, not from the texture dimensions: the capture
        /// station crops and pads to its own rules, so two very differently sized drawings can arrive as
        /// the same-sized PNG, and sizing off the PNG would make every fish the same size.
        /// </summary>
        private float CalculateWorldScale(Sprite sprite, FishData data, int foregroundArea) {
            float normalised = -1f;

            if (config.SizeFromForegroundArea) {
                normalised = FishSizeNormalizer.NormaliseForegroundArea(foregroundArea, config.ForegroundAreaAtMinimumSize, config.ForegroundAreaAtMaximumSize);
            }

            if (normalised < 0f) {
                normalised = data.SizeRoll;
            } else {
                normalised = AquariumConfig.EvaluateResponse(config.ForegroundAreaResponse, normalised);
            }

            int pixelWidth = Mathf.RoundToInt(sprite.rect.width);
            int pixelHeight = Mathf.RoundToInt(sprite.rect.height);
            float targetLongestSide = config.FishWorldSizeRange.Evaluate(normalised);

            return FishSizeNormalizer.CalculateUniformScale(pixelWidth, pixelHeight, sprite.pixelsPerUnit, targetLongestSide);
        }

        /// <summary>
        /// Where the fish appears. Deliberately the one place UnityEngine.Random is still allowed: which
        /// edge a fish swims in from is not part of its identity, nobody could recognise a drawing by it,
        /// and M5 will persist real positions anyway. Every value that IS identity comes from FishData.
        /// </summary>
        private Vector2 ChooseSpawnPosition(FishData data, out Vector2 initialHeading, bool forceInsideBounds) {
            if (!forceInsideBounds && config.SpawnStrategy == FishSpawnStrategy.EdgeEntry) {
                Vector2 inward;
                Vector2 entryPoint = bounds.RandomEdgeEntryPoint(config.BoundsPadding, out inward);
                initialHeading = (inward + new Vector2(0f, UnityEngine.Random.Range(-0.25f, 0.25f))).normalized;

                // Entry is always on a side edge, so the height is free: bring the fish in at the depth it
                // prefers rather than making it swim across the whole tank to get there.
                if (config.PreferredDepthEnabled) {
                    entryPoint.y = ResolvePreferredDepthHeight(data);
                }

                return entryPoint;
            }

            initialHeading = UnityEngine.Random.insideUnitCircle.normalized;
            if (initialHeading.sqrMagnitude <= Mathf.Epsilon) {
                initialHeading = Vector2.right;
            }

            Vector2 position = bounds.RandomPointInside(config.BoundsPadding);

            if (config.PreferredDepthEnabled) {
                position.y = ResolvePreferredDepthHeight(data);
            }

            return position;
        }

        private float ResolvePreferredDepthHeight(FishData data) {
            Rect area = bounds.GetPaddedArea(config.BoundsPadding);
            return FishDepthProfile.EvaluatePreferredHeight(data.PreferredDepth, area);
        }

        private FishController InstantiateFish() {
            if (fishPrefab != null) {
                return Instantiate(fishPrefab);
            }

            return BuildRuntimeFish();
        }

        /// <summary> Code-built equivalent of Fish.prefab, so the aquarium still runs if the prefab reference is lost. </summary>
        private static FishController BuildRuntimeFish() {
            GameObject root = new GameObject("Fish");

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<SpriteRenderer>();
            visual.AddComponent<FishAnimator>();

            root.AddComponent<FishMovement>();
            return root.AddComponent<FishController>();
        }
    }
}
