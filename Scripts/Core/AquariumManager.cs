using System;
using System.Collections.Generic;

using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Fish;
using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Owns the live fish population: registration, the maximum-count policy, removal and the single
    /// per-frame tick that advances every fish. Loading PNGs is deliberately not its job.
    ///
    /// It also holds the two things every fish needs to react to the visitor - the pointer source and the
    /// ripple field - because the fish already talk to the manager for neighbour queries, and giving the
    /// interaction layer its own Update would split the simulation across two frame orders for no gain.
    /// </summary>
    public sealed class AquariumManager : MonoBehaviour {
        [SerializeField] private AquariumConfig config;
        [SerializeField] private AquariumBounds bounds;
        [SerializeField] private Transform fishContainer;
        [Tooltip("Must be a component implementing IPointerSource, normally MousePointerSource. Leave empty to run the tank with no visitor interaction at all.")]
        [SerializeField] private MonoBehaviour pointerSourceBehaviour;

        private readonly List<FishController> activeFish = new List<FishController>();
        private readonly List<FishController> completedRemovals = new List<FishController>();

        private readonly FishSpatialHash spatialHash = new FishSpatialHash();

        // Parallel arrays rather than an array of structs: only the positions are handed to the spatial
        // hash, and keeping them contiguous is what makes the rebuild a single linear pass.
        private Vector2[] snapshotPositions = new Vector2[0];
        private Vector2[] snapshotHeadings = new Vector2[0];
        private float[] snapshotNovelty = new float[0];

        /// <summary> Snapshot indices of the fish that arrived recently enough to still draw a crowd. Usually empty. </summary>
        private int[] novelIndices = new int[0];

        private readonly List<int> foodWinners = new List<int>();

        private int[] neighbourBuffer = new int[0];
        private IPointerSource pointerSource;
        private RippleField ripples;
        private FoodField food;
        private int snapshotCount;
        private int novelCount;
        private int nextFishId = 1;
        private int nextSpawnOrder = 1;

        public event Action<FishController> FishSpawned;
        public event Action<FishController> FishRemoved;

        public AquariumConfig Config {
            get { return config; }
        }

        public AquariumBounds Bounds {
            get { return bounds; }
        }

        public Transform FishContainer {
            get { return fishContainer != null ? fishContainer : transform; }
        }

        public IReadOnlyList<FishController> ActiveFish {
            get { return activeFish; }
        }

        /// <summary>
        /// The visitor, or null when this installation has no pointer wired up. Fish must treat null and
        /// IsAvailable false the same way: carry on swimming, react to nothing.
        /// </summary>
        public IPointerSource Pointer {
            get { return pointerSource; }
        }

        /// <summary> Click shockwaves currently decaying in the tank. Never null. </summary>
        public RippleField Ripples {
            get { return ripples; }
        }

        /// <summary> Food sinking through the tank. Never null; the renderer reads it every frame. </summary>
        public FoodField Food {
            get { return food; }
        }

        /// <summary> Fish that are not already fading out. This is the number the maximum-count policy applies to. </summary>
        public int LiveFishCount {
            get { return CountLiveFish(); }
        }

        public int TotalFishCount {
            get { return activeFish.Count; }
        }

        public int MaxFishCount {
            get { return config != null ? config.MaxFishCount : 0; }
        }

        private void Awake() {
            if (config == null) {
                Debug.LogError("AquariumManager: no AquariumConfig assigned.");
            }

            if (bounds == null) {
                bounds = GetComponentInChildren<AquariumBounds>();
            }

            if (bounds == null) {
                Debug.LogError("AquariumManager: no AquariumBounds assigned; fish would have nowhere to swim.");
            }

            ResolvePointerSource();
            BuildRippleField();
            BuildFoodField();
        }

        private void OnEnable() {
            if (bounds != null) {
                bounds.BoundsChanged += HandleBoundsChanged;
            }

            if (pointerSource != null) {
                pointerSource.Pressed += HandlePointerPressed;
            }
        }

        private void OnDisable() {
            if (bounds != null) {
                bounds.BoundsChanged -= HandleBoundsChanged;
            }

            if (pointerSource != null) {
                pointerSource.Pressed -= HandlePointerPressed;
            }
        }

        private void Update() {
            float deltaTime = Time.deltaTime;

            ripples.Tick(deltaTime);
            food.Tick(deltaTime, ResolveFoodRestingHeight());
            RebuildNeighbourIndex();

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null) {
                    continue;
                }

                fish.Tick(deltaTime);
            }

            // After every fish has ticked, never during. Fish stake claims on food as they tick, and
            // resolving mid-loop would award each flake to whichever fish happened to come first in the
            // list rather than to the most aggressive one.
            ResolveFoodClaims();

            CollectCompletedRemovals();
        }

        public int ReserveFishId() {
            int reserved = nextFishId;
            nextFishId++;
            return reserved;
        }

        public int ReserveSpawnOrder() {
            int reserved = nextSpawnOrder;
            nextSpawnOrder++;
            return reserved;
        }

        /// <summary>
        /// Applies the configured overflow strategy so a new fish can be added. Returns false only when
        /// the aquarium is full and the strategy is to reject newcomers.
        /// </summary>
        public bool TryMakeRoomForNewFish() {
            if (config == null) {
                return false;
            }

            if (CountLiveFish() < config.MaxFishCount) {
                return true;
            }

            if (config.OverflowStrategy == FishOverflowStrategy.RejectNew) {
                return false;
            }

            FishController oldest = FindOldestLiveFish();
            if (oldest == null) {
                return false;
            }

            RemoveFish(oldest, true);
            return true;
        }

        public void RegisterFish(FishController fish) {
            if (fish == null) {
                Debug.LogError("AquariumManager: RegisterFish called with a null fish.");
                return;
            }

            if (activeFish.Contains(fish)) {
                return;
            }

            activeFish.Add(fish);

            if (FishSpawned != null) {
                FishSpawned(fish);
            }
        }

        /// <summary> Removes a fish, either with the fade-out animation or immediately. </summary>
        public void RemoveFish(FishController fish, bool animate) {
            if (fish == null) {
                return;
            }

            if (animate) {
                fish.BeginRemoval();
                return;
            }

            DestroyFish(fish);
        }

        public void ClearAllFish() {
            for (int i = activeFish.Count - 1; i >= 0; i--) {
                FishController fish = activeFish[i];
                if (fish == null) {
                    activeFish.RemoveAt(i);
                    continue;
                }

                DestroyFish(fish);
            }

            activeFish.Clear();
            snapshotCount = 0;
            novelCount = 0;
            spatialHash.Clear();
            food.Clear();
        }

        /// <summary>
        /// The whole neighbour response for one fish in a single pass: separation, alignment and cohesion,
        /// plus any pull from a fish that has just arrived.
        ///
        /// One spatial-hash query serves all of it. The separation radius is smaller than the neighbour
        /// radius, so querying at the wider of the two and filtering by distance inside the loop is cheaper
        /// than asking twice - and it is the reason this is one method rather than three.
        ///
        /// Alignment and cohesion are weighted by the fish's social trait through the response curves, so a
        /// solitary fish gets neither and keeps to the edges while a sociable one falls in with the school.
        /// </summary>
        public Vector2 CalculateFlocking(FishController self, Vector2 position, Vector2 heading, FishTraits traits) {
            if (self == null || config == null || snapshotCount <= 1) {
                return Vector2.zero;
            }

            int selfIndex = self.SnapshotIndex;
            Vector2 steer = CalculateNoveltyAttraction(selfIndex, position, traits);

            bool wantsSeparation = config.SeparationEnabled;
            bool wantsFlocking = config.FlockingEnabled;

            if (!wantsSeparation && !wantsFlocking) {
                return steer;
            }

            float separationRadius = config.SeparationRadius;
            float neighbourRadius = wantsFlocking ? config.NeighbourRadius : separationRadius;
            int candidateCount = spatialHash.Query(position, Mathf.Max(separationRadius, neighbourRadius), neighbourBuffer);

            if (candidateCount == 0) {
                return steer;
            }

            float separationRadiusSquared = separationRadius * separationRadius;
            float neighbourRadiusSquared = neighbourRadius * neighbourRadius;

            Vector2 separation = Vector2.zero;
            Vector2 headingSum = Vector2.zero;
            Vector2 positionSum = Vector2.zero;
            int neighbourCount = 0;

            for (int i = 0; i < candidateCount; i++) {
                int index = neighbourBuffer[i];
                if (index == selfIndex) {
                    continue;
                }

                Vector2 otherPosition = snapshotPositions[index];
                float squaredDistance = (otherPosition - position).sqrMagnitude;

                if (wantsSeparation && squaredDistance < separationRadiusSquared) {
                    separation += FishSteering.SeparationContribution(position, otherPosition, separationRadius);
                }

                if (wantsFlocking && squaredDistance < neighbourRadiusSquared) {
                    headingSum += snapshotHeadings[index];
                    positionSum += otherPosition;
                    neighbourCount++;
                }
            }

            steer += separation * config.SeparationStrength;

            if (neighbourCount == 0) {
                return steer;
            }

            float social = traits != null ? traits.Social : 0f;
            float alignmentWeight = config.AlignmentStrength * AquariumConfig.EvaluateResponse(config.SocialAlignmentResponse, social);
            float cohesionWeight = config.CohesionStrength * AquariumConfig.EvaluateResponse(config.SocialCohesionResponse, social);

            steer += FishFlocking.AlignmentDirection(headingSum, neighbourCount) * alignmentWeight;
            steer += FishFlocking.CohesionDirection(positionSum, neighbourCount, position) * cohesionWeight;

            return steer;
        }

        /// <summary>
        /// Pull towards any fish that has just been drawn. Walks the novel list directly rather than going
        /// through the spatial hash: newcomers are rare - none at all most of the time, one or two just
        /// after a capture - and their reach is deliberately wider than the neighbour radius, so routing
        /// this through the grid would force a cell size that made every other query worse.
        /// </summary>
        private Vector2 CalculateNoveltyAttraction(int selfIndex, Vector2 position, FishTraits traits) {
            if (novelCount == 0 || traits == null || !config.NoveltyEnabled) {
                return Vector2.zero;
            }

            float curiosity = AquariumConfig.EvaluateResponse(config.CuriosityNoveltyResponse, traits.Curiosity);
            if (curiosity <= 0f) {
                return Vector2.zero;
            }

            Vector2 accumulated = Vector2.zero;

            for (int i = 0; i < novelCount; i++) {
                int index = novelIndices[i];
                if (index == selfIndex) {
                    continue;
                }

                accumulated += FishFlocking.NoveltyAttraction(position, snapshotPositions[index], snapshotNovelty[index], curiosity, config.NoveltyRadius);
            }

            return accumulated * config.NoveltyAttractionStrength;
        }

        private void ResolvePointerSource() {
            if (pointerSourceBehaviour == null) {
                return;
            }

            pointerSource = pointerSourceBehaviour as IPointerSource;

            if (pointerSource == null) {
                Debug.LogError("AquariumManager: the component assigned as the pointer source does not implement IPointerSource; the tank will run with no visitor interaction.");
            }
        }

        /// <summary>
        /// Built once, from the config, because ripple tuning is authored rather than changed at runtime.
        /// Always constructed, even with interaction switched off, so nothing downstream has to null-check
        /// it on the hot path.
        /// </summary>
        private void BuildRippleField() {
            if (config == null) {
                ripples = new RippleField(1, 1f, 0f, 1f, 0f);
                return;
            }

            ripples = new RippleField(config.MaxConcurrentRipples, config.RippleDurationSeconds, config.RippleStrength, config.RippleRadius, config.RippleDangerRadius);
        }

        /// <summary>
        /// Built once, from the config, for the same reason as the ripple field. Always constructed so
        /// nothing downstream has to null-check it on the hot path.
        /// </summary>
        private void BuildFoodField() {
            if (config == null) {
                food = new FoodField(1, 0f, 1f);
                return;
            }

            food = new FoodField(config.MaxFoodParticles, config.FoodSinkSpeed, config.FoodLifetimeSeconds);
        }

        /// <summary>
        /// A press near the surface feeds; anywhere else sends a shockwave.
        ///
        /// Feeding deliberately does not also ripple. Scattering the shoal away from food the visitor has
        /// just dropped is the opposite of what the gesture is for, and it would make the cause and effect
        /// unreadable - which is the one thing the pointer interactions have to get right.
        /// </summary>
        private void HandlePointerPressed(Vector2 worldPosition) {
            if (config == null || !config.PointerInteractionEnabled) {
                return;
            }

            if (config.FeedingEnabled && IsWithinFeedingBand(worldPosition)) {
                DropFoodPinch(worldPosition);
                return;
            }

            if (config.RippleOnClickEnabled) {
                ripples.Emit(worldPosition);
            }
        }

        private bool IsWithinFeedingBand(Vector2 worldPosition) {
            if (bounds == null) {
                return false;
            }

            Rect area = bounds.Area;
            return worldPosition.y >= area.yMax - (area.height * config.FeedSurfaceBand);
        }

        /// <summary>
        /// Drops a pinch of food rather than a single crumb, so there is something for a crowd to form
        /// around. The horizontal scatter uses Random: where an individual flake lands is not part of any
        /// fish's identity, and nothing about the installation's promises depends on it.
        /// </summary>
        private void DropFoodPinch(Vector2 worldPosition) {
            float halfSpread = config.FoodPinchSpread * 0.5f;

            for (int i = 0; i < config.FoodPerPinch; i++) {
                Vector2 offset = new Vector2(UnityEngine.Random.Range(-halfSpread, halfSpread), UnityEngine.Random.Range(-halfSpread * 0.35f, halfSpread * 0.35f));
                food.Emit(worldPosition + offset);
            }
        }

        /// <summary> Where food comes to rest: the floor of the swimming area, not of the glass, so fish can still reach it. </summary>
        private float ResolveFoodRestingHeight() {
            if (bounds == null || config == null) {
                return 0f;
            }

            return bounds.GetPaddedArea(config.BoundsPadding).yMin;
        }

        private void ResolveFoodClaims() {
            if (food.ResolveClaims(foodWinners) == 0) {
                return;
            }

            for (int w = 0; w < foodWinners.Count; w++) {
                for (int i = 0; i < activeFish.Count; i++) {
                    FishController fish = activeFish[i];
                    if (fish == null || fish.Data == null || fish.Data.Id != foodWinners[w]) {
                        continue;
                    }

                    fish.RecordFoodEaten();
                    break;
                }
            }
        }

        /// <summary>
        /// Pull towards the nearest food, and a claim on it when the fish is close enough to eat.
        ///
        /// Two traits, two jobs, exactly as design point 2 asks: curiosity decides how hard a fish swims
        /// over, and aggression decides which of the fish that made it actually gets fed. The pull does not
        /// fall off with distance, so a fish that has noticed food commits to it rather than drifting
        /// vaguely in its direction.
        /// </summary>
        public Vector2 CalculateFoodAttraction(FishController self, Vector2 position, FishTraits traits) {
            if (self == null || self.Data == null || traits == null || config == null) {
                return Vector2.zero;
            }

            if (!config.FeedingEnabled || food.ActiveCount == 0) {
                return Vector2.zero;
            }

            Vector2 particlePosition;
            float distance;
            int index = food.FindNearest(position, config.FoodAwarenessRadius, out particlePosition, out distance);

            if (index < 0) {
                return Vector2.zero;
            }

            // Close enough to eat, and not still swallowing the last one. The cooldown is what stops a
            // single fish that happens to arrive first from hoovering a whole pile, and it is where
            // aggression shows when no two fish are actually contesting the same flake.
            if (distance <= config.FoodEatRadius && self.CanEat) {
                food.SubmitClaim(index, traits.Aggression, self.Data.Id);
            }

            Vector2 toFood = particlePosition - position;
            if (toFood.sqrMagnitude <= Mathf.Epsilon) {
                return Vector2.zero;
            }

            float appetite = AquariumConfig.EvaluateResponse(config.CuriosityFoodResponse, traits.Curiosity);
            return toFood.normalized * config.FoodAttractionStrength * appetite;
        }

        /// <summary>
        /// One linear pass that captures where every fish is, which way it is pointing and how recently it
        /// arrived, then indexes the positions so neighbour queries do not have to look at all of them.
        ///
        /// Rebuilt from scratch each frame. Every fish moves every frame, so an incremental update would
        /// cost more bookkeeping than the rebuild costs outright, and a stale index is a subtle bug.
        /// </summary>
        private void RebuildNeighbourIndex() {
            EnsureSnapshotCapacity(activeFish.Count);

            float noveltySeconds = config != null && config.NoveltyEnabled ? config.NoveltySeconds : 0f;

            snapshotCount = 0;
            novelCount = 0;

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null) {
                    continue;
                }

                fish.SetSnapshotIndex(snapshotCount);
                snapshotPositions[snapshotCount] = fish.Position;
                snapshotHeadings[snapshotCount] = fish.Movement != null ? fish.Movement.ForwardDirection : Vector2.right;

                float novelty = fish.EvaluateNovelty(noveltySeconds);
                snapshotNovelty[snapshotCount] = novelty;

                if (novelty > 0f) {
                    novelIndices[novelCount] = snapshotCount;
                    novelCount++;
                }

                snapshotCount++;
            }

            spatialHash.Build(snapshotPositions, snapshotCount, ResolveIndexArea(), ResolveCellSize());
        }

        /// <summary>
        /// Area the grid spans. The unpadded bounds on purpose: a fish can sit briefly outside the padded
        /// swimming area while entering or fleeing, and the grid should still have a cell for it.
        /// </summary>
        private Rect ResolveIndexArea() {
            if (bounds == null) {
                return new Rect(-10f, -10f, 20f, 20f);
            }

            return bounds.Area;
        }

        /// <summary> Cells sized to the widest radius any query will use, which is the usual balance for a uniform grid. </summary>
        private float ResolveCellSize() {
            if (config == null) {
                return 2f;
            }

            return Mathf.Max(config.SeparationRadius, config.FlockingEnabled ? config.NeighbourRadius : config.SeparationRadius);
        }

        private void EnsureSnapshotCapacity(int required) {
            if (snapshotPositions.Length < required) {
                int capacity = Mathf.NextPowerOfTwo(Mathf.Max(8, required));
                snapshotPositions = new Vector2[capacity];
                snapshotHeadings = new Vector2[capacity];
                snapshotNovelty = new float[capacity];
                novelIndices = new int[capacity];
            }

            int wantedNeighbours = config != null ? config.MaxNeighboursConsidered : 32;
            if (neighbourBuffer.Length != wantedNeighbours) {
                neighbourBuffer = new int[Mathf.Max(1, wantedNeighbours)];
            }
        }

        private void CollectCompletedRemovals() {
            completedRemovals.Clear();

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null || fish.IsRemovalComplete) {
                    completedRemovals.Add(fish);
                }
            }

            for (int i = 0; i < completedRemovals.Count; i++) {
                DestroyFish(completedRemovals[i]);
            }

            completedRemovals.Clear();
        }

        private void DestroyFish(FishController fish) {
            activeFish.Remove(fish);

            if (fish == null) {
                return;
            }

            if (FishRemoved != null) {
                FishRemoved(fish);
            }

            Destroy(fish.gameObject);
        }

        private int CountLiveFish() {
            int count = 0;

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null) {
                    continue;
                }

                if (fish.State == FishLifecycleState.Removing || fish.State == FishLifecycleState.Destroyed) {
                    continue;
                }

                count++;
            }

            return count;
        }

        private FishController FindOldestLiveFish() {
            FishController oldest = null;

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null) {
                    continue;
                }

                if (fish.State == FishLifecycleState.Removing || fish.State == FishLifecycleState.Destroyed) {
                    continue;
                }

                if (oldest == null || fish.SpawnOrder < oldest.SpawnOrder) {
                    oldest = fish;
                }
            }

            return oldest;
        }

        /// <summary> A resized window changes the swimming area, so every fish re-aims at a point inside the new one. </summary>
        private void HandleBoundsChanged(Rect area) {
            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null || fish.Movement == null) {
                    continue;
                }

                fish.Movement.PickNewTarget();
            }
        }
    }
}
