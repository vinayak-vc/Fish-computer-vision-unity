using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Owns one fish for its whole life: holds its data, runs the spawn and removal animations,
    /// and forwards the per-frame tick to movement and animation. AquariumManager drives the tick,
    /// so no fish runs an Update of its own.
    /// </summary>
    public sealed class FishController : MonoBehaviour {
        private const float EntryInset = 0.75f;

        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private FishMovement movement;
        [SerializeField] private FishAnimator fishAnimator;

        private AquariumConfig config;
        private Transform cachedTransform;
        private AquariumBounds bounds;
        private float baseScale = 1f;
        private float depthScaleMultiplier = 1f;
        private float depthAlpha = 1f;
        private float animationTimer;
        private float ageSeconds;
        private int foodEaten;
        private float eatCooldownRemaining;
        private bool suppressNovelty;
        private bool initialised;

        public FishData Data { get; private set; }

        public FishLifecycleState State { get; private set; }

        /// <summary> Index of this fish inside the position snapshot AquariumManager rebuilds each frame. </summary>
        public int SnapshotIndex { get; private set; }

        /// <summary> Monotonic spawn counter used by the RemoveOldest overflow strategy. </summary>
        public int SpawnOrder { get; private set; }

        public Vector2 Position {
            get { return cachedTransform != null ? (Vector2)cachedTransform.position : Vector2.zero; }
        }

        public bool IsRemovalComplete {
            get { return State == FishLifecycleState.Removing && animationTimer >= config.DespawnAnimationDuration; }
        }

        public FishMovement Movement {
            get { return movement; }
        }

        /// <summary> Seconds this fish has been swimming. Zero for a fish that has only just been created. </summary>
        public float AgeSeconds {
            get { return ageSeconds; }
        }

        /// <summary>
        /// Flakes of food this fish has won. Surfaced in the debug overlay, because whether the aggressive
        /// fish are actually winning is not something you can reliably judge by watching.
        /// </summary>
        public int FoodEaten {
            get { return foodEaten; }
        }

        /// <summary>
        /// False while the fish is still swallowing the last flake. AquariumManager will not stake a claim
        /// on its behalf until this comes back, which both bounds how fast one fish can hoover a pile and
        /// gives the aggression trait somewhere to show.
        /// </summary>
        public bool CanEat {
            get { return eatCooldownRemaining <= 0f; }
        }

        /// <summary> Called by AquariumManager when this fish wins a flake. Starts its chewing cooldown. </summary>
        public void RecordFoodEaten() {
            foodEaten++;
            eatCooldownRemaining = Data != null ? Data.EatCooldownSeconds : 0f;
        }

        /// <summary>
        /// How interesting this fish still is to its neighbours: 1 immediately after it is drawn, falling
        /// to 0 across noveltySeconds. Design point 16, a new arrival draws a curious crowd.
        ///
        /// Always 0 for a replayed capture. A replay is the aquarium restoring fish it already had, not
        /// announcing arrivals, so a restart would otherwise have the whole tank swarm ten fish at once -
        /// the same reason replays suppress the birth animation.
        /// </summary>
        public float EvaluateNovelty(float noveltySeconds) {
            if (suppressNovelty || noveltySeconds <= 0f || !initialised) {
                return 0f;
            }

            return 1f - Mathf.Clamp01(ageSeconds / noveltySeconds);
        }

        private void Awake() {
            cachedTransform = transform;
            ResolveComponentReferences();
        }

        /// <summary>
        /// Wires the fish up and starts the spawn animation. Called by FishFactory immediately after Instantiate.
        /// skipEntranceAnimation is for replayed captures, which are restoring fish the aquarium already had
        /// rather than announcing an arrival, so they appear at full size and opacity straight away.
        /// </summary>
        public void Initialize(FishData fishData, AquariumConfig aquariumConfig, AquariumBounds aquariumBounds, AquariumManager manager, int spawnOrder, Vector2 initialHeading, bool skipEntranceAnimation) {
            if (fishData == null || aquariumConfig == null || aquariumBounds == null) {
                Debug.LogError("FishController: Initialize called with missing dependencies.");
                return;
            }

            if (cachedTransform == null) {
                cachedTransform = transform;
                ResolveComponentReferences();
            }

            Data = fishData;
            config = aquariumConfig;
            bounds = aquariumBounds;
            SpawnOrder = spawnOrder;

            baseScale = Data.WorldScale;
            depthScaleMultiplier = FishDepthProfile.EvaluateScaleMultiplier(Data.Depth, config.DepthScaleInfluence);
            depthAlpha = FishDepthProfile.EvaluateAlpha(Data.Depth, config.DepthAlphaInfluence);

            cachedTransform.position = new Vector3(Data.SpawnPosition.x, Data.SpawnPosition.y, 0f);

            if (spriteRenderer != null) {
                spriteRenderer.sprite = Data.Sprite;
                spriteRenderer.sortingOrder = FishDepthProfile.EvaluateSortingOrder(Data.Depth, config.ForegroundSortingOrder, config.BackgroundSortingOrder);
            }

            movement.Initialize(this, Data, config, bounds, manager, spriteRenderer, initialHeading);
            fishAnimator.Initialize(Data, config);

            ageSeconds = 0f;
            eatCooldownRemaining = 0f;

            // A replay is a restoration, not an arrival, so it neither celebrates nor draws a crowd.
            suppressNovelty = skipEntranceAnimation;

            if (skipEntranceAnimation) {
                State = FishLifecycleState.Swimming;
                animationTimer = config.SpawnAnimationDuration;
                ApplyVisualState(1f);
            } else {
                State = FishLifecycleState.Spawning;
                animationTimer = 0f;
                ApplyVisualState(0f);
            }

            initialised = true;
        }

        public void SetSnapshotIndex(int index) {
            SnapshotIndex = index;
        }

        /// <summary> Starts the fade-out. The manager destroys the fish once IsRemovalComplete turns true. </summary>
        public void BeginRemoval() {
            if (State == FishLifecycleState.Removing || State == FishLifecycleState.Destroyed) {
                return;
            }

            State = FishLifecycleState.Removing;
            animationTimer = 0f;
        }

        /// <summary> One simulation step. Driven by AquariumManager so the whole shoal shares a single Update. </summary>
        public void Tick(float deltaTime) {
            if (!initialised) {
                return;
            }

            ageSeconds += deltaTime;

            if (eatCooldownRemaining > 0f) {
                eatCooldownRemaining -= deltaTime;
            }

            switch (State) {
                case FishLifecycleState.Spawning:
                    TickSpawning(deltaTime);
                    break;
                case FishLifecycleState.Entering:
                    TickEntering(deltaTime);
                    break;
                case FishLifecycleState.Swimming:
                    TickSwimming(deltaTime);
                    break;
                case FishLifecycleState.Removing:
                    TickRemoving(deltaTime);
                    break;
            }
        }

        private void TickSpawning(float deltaTime) {
            animationTimer += deltaTime;
            float progress = Mathf.Clamp01(animationTimer / config.SpawnAnimationDuration);

            movement.Tick(deltaTime);
            fishAnimator.Tick(deltaTime, NormalisedSpeed());
            ApplyVisualState(progress);

            if (progress >= 1f) {
                State = FishLifecycleState.Entering;
            }
        }

        private void TickEntering(float deltaTime) {
            movement.Tick(deltaTime);
            fishAnimator.Tick(deltaTime, NormalisedSpeed());

            Rect settledArea = bounds.GetPaddedArea(config.BoundsPadding + EntryInset);
            if (settledArea.Contains(Position)) {
                State = FishLifecycleState.Swimming;
            }
        }

        private void TickSwimming(float deltaTime) {
            movement.Tick(deltaTime);
            fishAnimator.Tick(deltaTime, NormalisedSpeed());
        }

        private void TickRemoving(float deltaTime) {
            animationTimer += deltaTime;
            float progress = Mathf.Clamp01(animationTimer / config.DespawnAnimationDuration);

            movement.Tick(deltaTime);
            fishAnimator.Tick(deltaTime, NormalisedSpeed());
            ApplyVisualState(1f - progress);
        }

        private float NormalisedSpeed() {
            if (Data == null || Data.Speed <= Mathf.Epsilon) {
                return 0f;
            }

            return Mathf.Clamp01(movement.CurrentSpeed / Data.Speed);
        }

        /// <summary> progress runs 0 to 1 while spawning and 1 to 0 while removing. </summary>
        private void ApplyVisualState(float progress) {
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
            float spawnScale = Mathf.Lerp(config.SpawnStartScaleMultiplier, 1f, eased);
            float finalScale = baseScale * depthScaleMultiplier * spawnScale;

            cachedTransform.localScale = new Vector3(finalScale, finalScale, 1f);

            if (spriteRenderer != null) {
                Color colour = spriteRenderer.color;
                colour.a = depthAlpha * eased;
                spriteRenderer.color = colour;
            }
        }

        private void ResolveComponentReferences() {
            if (movement == null) {
                movement = GetComponent<FishMovement>();
            }

            if (spriteRenderer == null) {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            }

            if (visualRoot == null && spriteRenderer != null) {
                visualRoot = spriteRenderer.transform;
            }

            if (fishAnimator == null && visualRoot != null) {
                fishAnimator = visualRoot.GetComponent<FishAnimator>();
            }

            if (fishAnimator == null) {
                fishAnimator = GetComponentInChildren<FishAnimator>(true);
            }
        }

        private void OnDestroy() {
            State = FishLifecycleState.Destroyed;

            if (Data != null) {
                FishSpriteCache.Release(Data.SourceFilePath);
            }
        }
    }
}
