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
        private float birthMomentRemaining;
        private bool birthGlowActive;
        private Color spriteTint = Color.white;
        private SpriteRenderer eyeRenderer;
        private TrailRenderer trail;
        private float rarityGlow;
        private float birthLoudness = 1f;
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

        /// <summary>
        /// True while this fish is still having its arrival celebrated. Design point 28's birth moment;
        /// M9 makes it louder for a rare drawing.
        /// </summary>
        public bool IsCelebratingBirth {
            get { return birthMomentRemaining > 0f; }
        }

        /// <summary>
        /// Puts back the life this fish had before the last restart: where it was, how much it trusts the
        /// visitor, how old it is and how well it has eaten.
        ///
        /// Called immediately after the factory has built the fish, and only for a restore. Everything
        /// here is state the aquarium owns under contract section 1 - Python holds none of it.
        /// </summary>
        public void RestoreState(Vector2 position, float affection, float ageSecondsSoFar, int foodEatenSoFar) {
            if (cachedTransform == null) {
                cachedTransform = transform;
            }

            cachedTransform.position = new Vector3(position.x, position.y, cachedTransform.position.z);

            if (movement != null) {
                movement.RestoreAffection(affection);
                movement.PickNewTarget();
            }

            ageSeconds = Mathf.Max(0f, ageSecondsSoFar);
            foodEaten = Mathf.Max(0, foodEatenSoFar);

            // A restored fish is not a new arrival: no celebration, and nothing for the shoal to swarm.
            birthMomentRemaining = 0f;
            birthGlowActive = false;
            suppressNovelty = true;
        }

        /// <summary>
        /// Tints this fish's sprite. Used for a baby, which wears a parent's drawing under a blend of both
        /// parents' colours - design point 5's "colour blended from the parents".
        ///
        /// Held as a field rather than written straight to the renderer, because the spawn, removal and
        /// birth-glow animations all rewrite the colour every frame and would otherwise wipe it out.
        /// </summary>
        public void ApplyTint(Color tint) {
            spriteTint = tint;
            ApplyVisualState(State == FishLifecycleState.Spawning ? 0f : 1f);
        }

        /// <summary>
        /// Places the eye highlight. Design point 26, and the reason FishAnatomyLandmarks exists.
        ///
        /// uv is a sprite UV with the y already flipped out of Python's top-left convention, so nothing
        /// here re-flips it. The highlight is parented to the visual root, which the sprite mirrors, so
        /// the eye stays on the fish's head whichever way it is swimming.
        /// </summary>
        public void ApplyEyeMarker(Vector2 uv, float radius) {
            if (config == null || !config.EyeHighlightEnabled || spriteRenderer == null || spriteRenderer.sprite == null) {
                return;
            }

            if (eyeRenderer == null) {
                eyeRenderer = BuildEyeRenderer();
            }

            if (eyeRenderer == null) {
                return;
            }

            Bounds bounds = spriteRenderer.sprite.bounds;

            // UV to sprite-local: 0..1 across the sprite's own extent, centred on its pivot.
            Vector3 local = new Vector3(
                bounds.min.x + (bounds.size.x * Mathf.Clamp01(uv.x)),
                bounds.min.y + (bounds.size.y * Mathf.Clamp01(uv.y)),
                0f);

            eyeRenderer.transform.localPosition = local;

            float longestSide = Mathf.Max(bounds.size.x, bounds.size.y);
            float diameter = Mathf.Max(0.001f, radius * 2f * longestSide * config.EyeHighlightScale);
            float spriteHeight = eyeRenderer.sprite != null ? eyeRenderer.sprite.bounds.size.y : 1f;
            float scale = spriteHeight > Mathf.Epsilon ? diameter / spriteHeight : 1f;

            eyeRenderer.transform.localScale = new Vector3(scale, scale, 1f);
            eyeRenderer.color = config.EyeHighlightColour;
            eyeRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
            eyeRenderer.gameObject.SetActive(true);
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

            // Live arrivals get a moment; replays and restores are the aquarium putting fish back, not
            // announcing them, so they get none.
            ResolveRarityEffects();

            // Design point 7: rarity gates a louder birth moment, in both length and brightness.
            birthMomentRemaining = skipEntranceAnimation ? 0f : config.BirthMomentSeconds * birthLoudness;

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

            if (birthMomentRemaining > 0f) {
                birthMomentRemaining -= deltaTime;
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

            RefreshBirthGlow();
        }

        /// <summary>
        /// Fades the birth glow out over its window.
        ///
        /// Driven from here because the glow outlives the spawn animation - a 1.4 second moment against a
        /// 0.7 second entrance - and neither Entering nor Swimming refresh the sprite colour on their own.
        /// Without this the fish would keep whatever brightness it had at the instant it started swimming
        /// and stay lit for the rest of its life.
        ///
        /// The flag exists so the reset back to normal happens exactly once rather than every frame.
        /// </summary>
        private void RefreshBirthGlow() {
            if (State != FishLifecycleState.Entering && State != FishLifecycleState.Swimming) {
                return;
            }

            if (birthMomentRemaining > 0f) {
                birthGlowActive = true;
                ApplyVisualState(1f);
                return;
            }

            if (birthGlowActive) {
                birthGlowActive = false;
                ApplyVisualState(1f);
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

            if (spriteRenderer == null) {
                return;
            }

            // The birth moment: a fish that has just been drawn glows, fading back to normal over the
            // window. Multiplying the colour rather than adding an object keeps it free - no extra
            // renderer, no particle system, nothing to pool - and it reads clearly against the water.
            float brightness = EvaluateBirthBrightness();

            // A jellyfish glows as it pulses. Only a confidently classified one: contract section 5 asks
            // for type-specific effects to be skipped when the classifier was guessing.
            if (movement != null) {
                brightness += movement.EvaluateTypeGlow();
            }

            // A rare drawing glows steadily. Design point 7.
            brightness += rarityGlow;

            Color colour = new Color(spriteTint.r * brightness, spriteTint.g * brightness, spriteTint.b * brightness, depthAlpha * eased);
            spriteRenderer.color = colour;
        }

        private float EvaluateBirthBrightness() {
            if (birthMomentRemaining <= 0f || config.BirthMomentSeconds <= 0f) {
                return 1f;
            }

            float window = config.BirthMomentSeconds * birthLoudness;
            float remaining = Mathf.Clamp01(birthMomentRemaining / Mathf.Max(0.0001f, window));
            return Mathf.Lerp(1f, config.BirthMomentBrightness * birthLoudness, remaining);
        }

        /// <summary>
        /// Works out what this drawing's rarity earns it: a steady glow, a trail, and a louder arrival.
        /// Design point 7.
        /// </summary>
        private void ResolveRarityEffects() {
            rarityGlow = 0f;
            birthLoudness = 1f;

            if (!config.RarityEffectsEnabled || Data == null || Data.Traits == null) {
                return;
            }

            rarityGlow = FishRarityProfile.EvaluateGlow(Data.Traits.RarityScore, config.RarityGlowResponse, config.RarityMaximumGlow);
            birthLoudness = FishRarityProfile.EvaluateBirthLoudness(Data.Traits.RarityTier, config.RarityBirthLoudness);

            if (FishRarityProfile.HasTrail(Data.Traits.RarityTier, config.MinimumTrailTier)) {
                BuildTrail();
            }
        }

        /// <summary>
        /// A trail for a rare drawing, using Unity's own TrailRenderer.
        ///
        /// Built rather than pooled because it is rare by construction - only drawings at or above the
        /// configured tier get one, which on a common-heavy tank is a handful at most.
        /// </summary>
        private void BuildTrail() {
            if (trail != null) {
                return;
            }

            Transform host = visualRoot != null ? visualRoot : cachedTransform;

            trail = host.gameObject.GetComponent<TrailRenderer>();
            if (trail == null) {
                trail = host.gameObject.AddComponent<TrailRenderer>();
            }

            trail.time = config.RarityTrailSeconds;
            trail.startWidth = config.RarityTrailStartWidth * baseScale;
            trail.endWidth = 0f;
            trail.numCapVertices = 4;
            trail.alignment = LineAlignment.View;
            trail.sortingOrder = spriteRenderer != null ? spriteRenderer.sortingOrder - 1 : 0;
            trail.material = new Material(Shader.Find("Sprites/Default"));

            Color tail = Data.Traits.DominantColour;
            tail.a = 0.5f;
            trail.startColor = tail;

            Color faded = tail;
            faded.a = 0f;
            trail.endColor = faded;
        }

        private SpriteRenderer BuildEyeRenderer() {
            Transform host = visualRoot != null ? visualRoot : cachedTransform;
            if (host == null) {
                return null;
            }

            GameObject eye = new GameObject("EyeHighlight");
            eye.transform.SetParent(host, false);

            SpriteRenderer renderer = eye.AddComponent<SpriteRenderer>();
            renderer.sprite = ViitorCloud.FishAquarium.Aquarium.ProceduralSpriteLibrary.GetPellet(16);
            eye.SetActive(false);
            return renderer;
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
