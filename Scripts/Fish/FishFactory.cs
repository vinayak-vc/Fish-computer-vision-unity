using System.IO;

using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Turns a loaded Sprite into a living fish: normalises its size, rolls its behaviour profile,
    /// chooses an entry point and hands the finished instance to AquariumManager.
    /// </summary>
    public sealed class FishFactory : MonoBehaviour {
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
        /// </summary>
        public FishController CreateFish(Sprite sprite, string sourceFilePath, bool spawnSilently) {
            if (sprite == null) {
                Debug.LogError("FishFactory: CreateFish called with a null sprite.");
                return null;
            }

            if (config == null || aquariumManager == null || bounds == null) {
                Debug.LogError("FishFactory: cannot create a fish, dependencies are missing.");
                return null;
            }

            if (!aquariumManager.TryMakeRoomForNewFish()) {
                return null;
            }

            string fileName = string.IsNullOrEmpty(sourceFilePath) ? sprite.name : Path.GetFileName(sourceFilePath);

            FishData data = new FishData(aquariumManager.ReserveFishId(), fileName, sourceFilePath, sprite);
            data.ApplyRandomisedProfile(config);
            data.SetWorldScale(CalculateWorldScale(sprite));

            Vector2 initialHeading;
            data.SetSpawnPosition(ChooseSpawnPosition(out initialHeading, spawnSilently));

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

        /// <summary> Uniform scale that brings any source resolution to the configured world size without distorting it. </summary>
        private float CalculateWorldScale(Sprite sprite) {
            int pixelWidth = Mathf.RoundToInt(sprite.rect.width);
            int pixelHeight = Mathf.RoundToInt(sprite.rect.height);
            float targetLongestSide = config.FishWorldSizeRange.PickRandom();

            return FishSizeNormalizer.CalculateUniformScale(pixelWidth, pixelHeight, sprite.pixelsPerUnit, targetLongestSide);
        }

        private Vector2 ChooseSpawnPosition(out Vector2 initialHeading, bool forceInsideBounds) {
            if (!forceInsideBounds && config.SpawnStrategy == FishSpawnStrategy.EdgeEntry) {
                Vector2 inward;
                Vector2 entryPoint = bounds.RandomEdgeEntryPoint(config.BoundsPadding, out inward);
                initialHeading = (inward + new Vector2(0f, UnityEngine.Random.Range(-0.25f, 0.25f))).normalized;
                return entryPoint;
            }

            initialHeading = UnityEngine.Random.insideUnitCircle.normalized;
            if (initialHeading.sqrMagnitude <= Mathf.Epsilon) {
                initialHeading = Vector2.right;
            }

            return bounds.RandomPointInside(config.BoundsPadding);
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
