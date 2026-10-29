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

        /// <summary> How frightening each drawing is. Non-zero only for confidently classified, aggressive creatures. </summary>
        private float[] snapshotThreat = new float[0];

        /// <summary> Snapshot indices of the fish that arrived recently enough to still draw a crowd. Usually empty. </summary>
        private int[] novelIndices = new int[0];

        private readonly List<int> foodWinners = new List<int>();
        private readonly List<long> pairsReadyToLay = new List<long>();
        private readonly List<int> hatchedEggs = new List<int>();
        private readonly CaptureCalendar calendar = new CaptureCalendar();

        private int[] neighbourBuffer = new int[0];
        private IPointerSource pointerSource;
        private RippleField ripples;
        private FoodField food;
        private AquariumEnergy energy;
        private AquariumCycle cycle;
        private AquariumPredator predator;
        private FishBondRegistry bonds;
        private EggField eggs;
        [SerializeField] private FishFactory fishFactory;
        private float worldTime;
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

        /// <summary> How many drawings arrived today, design point 29. Never null. </summary>
        public CaptureCalendar Calendar {
            get { return calendar; }
        }

        /// <summary>
        /// How alive the tank feels, 0..1. Design point 11: one value that fish speed, bubble density and
        /// the water's brightness all read, so the aquarium has a single mood rather than several.
        /// </summary>
        public AquariumEnergy Energy {
            get { return energy; }
        }

        /// <summary> Time of day and weather. Never null. </summary>
        public AquariumCycle Cycle {
            get { return cycle; }
        }

        /// <summary> The thing that crosses the tank now and then and scatters everything. Never null. </summary>
        public AquariumPredator Predator {
            get { return predator; }
        }

        /// <summary> Who is fond of whom, and which pairs are expecting. Design points 5 and 8. Never null. </summary>
        public FishBondRegistry Bonds {
            get { return bonds; }
        }

        /// <summary> Eggs waiting to hatch. Never null; the renderer reads it every frame. </summary>
        public EggField Eggs {
            get { return eggs; }
        }

        /// <summary> Seconds the tank has been running. Drives the current, which is a function of position and time. </summary>
        public float WorldTime {
            get { return worldTime; }
        }

        /// <summary>
        /// Speed multiplier every fish applies, from the tank's mood. A busy aquarium swims faster, which
        /// is the most legible of the energy outputs and the reason it is wired here rather than per fish.
        /// </summary>
        public float EnergySpeedMultiplier {
            get {
                if (config == null || !config.AquariumEnergyEnabled || energy == null) {
                    return 1f;
                }

                return 1f + (energy.Current * config.EnergySpeedInfluence);
            }
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
            BuildWorldSystems();
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

            // The world is advanced before any fish reads it, so every fish in a frame sees the same
            // current, the same weather and the same threat.
            TickWorldSystems(deltaTime);

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

            // Courtship runs after the fish have moved, so familiarity is judged on where they ended the
            // frame rather than where they started it.
            TickSocialBonds(deltaTime);

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

            if (energy != null) {
                energy.Reset();
            }

            if (predator != null) {
                predator.Reset();
            }

            if (bonds != null) {
                bonds.Clear();
            }

            if (eggs != null) {
                eggs.Clear();
            }
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
            return CalculateFlocking(self, position, heading, traits, true);
        }

        /// <summary>
        /// As above, but schoolsWithOthers says whether this drawing joins a school at all. A jellyfish
        /// does not - contract section 5 - though it still separates from its neighbours, because that is
        /// collision avoidance rather than sociability.
        /// </summary>
        public Vector2 CalculateFlocking(FishController self, Vector2 position, Vector2 heading, FishTraits traits, bool schoolsWithOthers) {
            if (self == null || config == null || snapshotCount <= 1) {
                return Vector2.zero;
            }

            int selfIndex = self.SnapshotIndex;
            Vector2 steer = CalculateNoveltyAttraction(selfIndex, position, traits);

            bool wantsSeparation = config.SeparationEnabled;
            bool wantsFlocking = config.FlockingEnabled && schoolsWithOthers;

            if (!wantsSeparation && !wantsFlocking) {
                return steer;
            }

            float separationRadius = config.SeparationRadius;
            float neighbourRadius = wantsFlocking ? config.NeighbourRadius : separationRadius;
            float creatureRadius = config.ObjectTypeBehavioursEnabled ? config.CreatureFearRadius : 0f;
            float queryRadius = Mathf.Max(Mathf.Max(separationRadius, neighbourRadius), creatureRadius);
            int candidateCount = spatialHash.Query(position, queryRadius, neighbourBuffer);

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

                // A predatory creature nearby. Contract section 5: a creature is eligible to be a
                // predator, so the other drawings give it room.
                if (snapshotThreat[index] > 0f && creatureRadius > 0f && squaredDistance < creatureRadius * creatureRadius) {
                    steer += BuildCreatureFlee(position, otherPosition, snapshotThreat[index], traits, creatureRadius);
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
        /// Flee force from one predatory creature. Weaker and much shorter-ranged than the procedural
        /// predator of design point 20: that is a rare event which empties the tank, this is a bully that
        /// other fish simply keep away from.
        /// </summary>
        private Vector2 BuildCreatureFlee(Vector2 position, Vector2 creaturePosition, float threat, FishTraits traits, float radius) {
            Vector2 away = position - creaturePosition;
            float distance = away.magnitude;

            if (distance <= 0.0001f) {
                return Vector2.up * config.CreatureFleeStrength * threat;
            }

            float closeness = 1f - Mathf.Clamp01(distance / radius);

            // Even a bold fish gives a predator room, at about half the strength of a nervous one.
            float nerve = Mathf.Lerp(0.5f, 1f, traits != null ? traits.Fear : 0.5f);
            return (away / distance) * config.CreatureFleeStrength * threat * closeness * nerve;
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
        /// <summary>
        /// Built once from the config, like the ripple and food fields. Always constructed even when the
        /// features are switched off, so nothing downstream has to null-check them on the hot path.
        /// </summary>
        private void BuildWorldSystems() {
            if (config == null) {
                energy = new AquariumEnergy(0.35f, 0.8f, 0.12f, 0.25f);
                cycle = new AquariumCycle(300f, 110f, 22f, 5f);
                predator = new AquariumPredator(165f, 11f, 2.5f);
                bonds = new FishBondRegistry(14f, 0.6f, 30f);
                eggs = new EggField(8, 12f);
                return;
            }

            energy = new AquariumEnergy(config.EnergyRestingLevel, config.EnergyRisePerSecond, config.EnergyDecayPerSecond, config.EnergyPerCapture);
            cycle = new AquariumCycle(config.DayLengthSeconds, config.StormIntervalSeconds, config.StormDurationSeconds, config.StormRampSeconds);
            predator = new AquariumPredator(config.PredatorIntervalSeconds, config.PredatorCrossingSeconds, config.PredatorFadeSeconds);
            bonds = new FishBondRegistry(config.SecondsTogetherToPair, config.FamiliarityDecayPerSecond, config.SecondsPerBrood);
            eggs = new EggField(config.MaxEggs, config.EggIncubationSeconds);
        }

        /// <summary>
        /// Advances the mood, the weather and the predator, once per frame and before any fish reads them.
        /// The visitor's contribution to the mood is pointer speed, which is the one signal available
        /// without person tracking.
        /// </summary>
        private void TickWorldSystems(float deltaTime) {
            worldTime += deltaTime;

            cycle.Tick(deltaTime);

            if (config != null && config.PredatorEnabled && bounds != null) {
                predator.Tick(deltaTime, bounds.Area);
            }

            float stimulus = 0f;
            if (pointerSource != null && pointerSource.IsAvailable && config != null) {
                stimulus = Mathf.Clamp01(pointerSource.Speed / Mathf.Max(0.01f, config.EnergyPointerSpeedForFull));
            }

            energy.Tick(deltaTime, stimulus);
        }

        /// <summary>
        /// The current at a point: broad slow eddies plus the prevailing drift, roughened by any storm.
        /// Read by every fish each frame, which is affordable because the flow is a pure function rather
        /// than a grid anybody has to own.
        /// </summary>
        public Vector2 SampleCurrent(Vector2 position) {
            if (config == null || !config.CurrentsEnabled) {
                return Vector2.zero;
            }

            float turbulence = config.StormTurbulence * cycle.StormIntensity;

            Vector2 flow = AquariumFlow.Sample(position, worldTime, config.CurrentScale, config.CurrentStrength, turbulence);
            flow += AquariumFlow.SampleMigration(worldTime, config.MigrationPeriodSeconds, config.MigrationStrength);
            return flow;
        }

        /// <summary>
        /// Flee force from the predator, and how panicked the fish is about it, 0..1.
        ///
        /// Every fish reacts, however brave: a fish with fear 0 still runs, just less hard. That is the
        /// difference between a predator and the pointer - the pointer is a curiosity, this is a threat.
        /// </summary>
        public Vector2 CalculatePredatorFlee(Vector2 position, float fear, out float panic) {
            panic = 0f;

            if (config == null || !config.PredatorEnabled || predator == null || !predator.IsPresent) {
                return Vector2.zero;
            }

            float threat = predator.Threat;
            if (threat <= 0f) {
                return Vector2.zero;
            }

            Vector2 away = position - predator.Position;
            float distance = away.magnitude;

            if (distance >= config.PredatorFearRadius) {
                return Vector2.zero;
            }

            float closeness = 1f - (distance / config.PredatorFearRadius);

            // Even a fearless fish flees, at roughly half strength. Fear scales the panic on top.
            float nerve = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(fear));
            panic = Mathf.Clamp01(threat * closeness * nerve);

            Vector2 direction = distance > 0.0001f ? (away / distance) : Vector2.up;
            return direction * config.PredatorFleeStrength * panic;
        }

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

        /// <summary>
        /// Advances courtship, laying and hatching. Design points 5 and 8.
        ///
        /// Company is judged on nearest neighbours only. A fish surrounded by six others is not keeping
        /// company with all six - it is in a shoal - and crediting every pair in a crowd would have the
        /// whole tank paired within a minute of the first school forming.
        /// </summary>
        private void TickSocialBonds(float deltaTime) {
            if (config == null || !config.SocialBondsEnabled || bonds == null) {
                return;
            }

            AccrueFamiliarity(deltaTime);

            bonds.Tick(deltaTime, pairsReadyToLay);

            for (int i = 0; i < pairsReadyToLay.Count; i++) {
                TryLayEgg(pairsReadyToLay[i]);
            }

            HatchEggs(deltaTime);
        }

        private void AccrueFamiliarity(float deltaTime) {
            if (snapshotCount <= 1) {
                return;
            }

            float radius = config.BondRadius;

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null || fish.Data == null || fish.State != FishLifecycleState.Swimming) {
                    continue;
                }

                int nearest = FindNearestFishId(fish, radius);
                if (nearest > 0) {
                    bonds.RecordProximity(fish.Data.Id, nearest, deltaTime);
                }
            }
        }

        /// <summary> Nearest other fish within radius, by fish id, or 0 when it is swimming alone. </summary>
        private int FindNearestFishId(FishController self, float radius) {
            int candidateCount = spatialHash.Query(self.Position, radius, neighbourBuffer);
            if (candidateCount == 0) {
                return 0;
            }

            int selfIndex = self.SnapshotIndex;
            float bestSquared = radius * radius;
            int bestSnapshotIndex = -1;

            for (int i = 0; i < candidateCount; i++) {
                int index = neighbourBuffer[i];
                if (index == selfIndex) {
                    continue;
                }

                float squared = (snapshotPositions[index] - self.Position).sqrMagnitude;
                if (squared > bestSquared) {
                    continue;
                }

                bestSquared = squared;
                bestSnapshotIndex = index;
            }

            if (bestSnapshotIndex < 0) {
                return 0;
            }

            // The snapshot holds positions, not fish, so the owner has to be found by its index.
            for (int i = 0; i < activeFish.Count; i++) {
                FishController other = activeFish[i];
                if (other != null && other.Data != null && other.SnapshotIndex == bestSnapshotIndex) {
                    return other.Data.Id;
                }
            }

            return 0;
        }

        private void TryLayEgg(long pairKey) {
            int firstId, secondId;
            bonds.UnpackKey(pairKey, out firstId, out secondId);

            FishController first = FindFishById(firstId);
            FishController second = FindFishById(secondId);

            if (first == null || second == null || first.Data == null || second.Data == null) {
                return;
            }

            if (eggs.ActiveCount >= eggs.Capacity) {
                // No room. The brood stays at its threshold and will try again next frame rather than
                // resetting, so a full nursery only delays a pair instead of denying them.
                return;
            }

            FishTraits blended = FishTraitBlend.Blend(first.Data.Traits, second.Data.Traits,
                bonds.GetBroodCount(pairKey), config.BroodTraitJitter,
                "baby_" + firstId.ToString() + "_" + secondId.ToString() + "_" + bonds.GetBroodCount(pairKey).ToString());

            Vector2 position = (first.Position + second.Position) * 0.5f;
            Color tint = Color.Lerp(first.Data.Traits.DominantColour, second.Data.Traits.DominantColour, 0.5f);
            float scale = FishTraitBlend.BlendWorldScale(first.Data.WorldScale, second.Data.WorldScale, config.BabyScaleFactor);

            if (eggs.TryLay(position, blended, first.Data.SourceFilePath, tint, scale)) {
                bonds.ConsumeBrood(pairKey);
            }
        }

        private void HatchEggs(float deltaTime) {
            if (eggs.CollectHatched(deltaTime, hatchedEggs) == 0) {
                return;
            }

            for (int i = 0; i < hatchedEggs.Count; i++) {
                int index = hatchedEggs[i];

                EggField.Egg egg;
                if (!eggs.TryGetEgg(index, out egg)) {
                    continue;
                }

                if (fishFactory == null) {
                    // Nothing can hatch without a factory, and leaving the egg would have it retried
                    // every frame forever.
                    eggs.Remove(index);
                    continue;
                }

                FishController baby = fishFactory.CreateBaby(egg.SpriteKey, egg.Traits, egg.Tint, egg.WorldScale, egg.Position);

                // The egg is removed either way. A baby the tank had no room for is not kept waiting:
                // the pair simply starts another brood.
                eggs.Remove(index);

                if (baby == null) {
                    continue;
                }
            }
        }

        private FishController FindFishById(int fishId) {
            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish != null && fish.Data != null && fish.Data.Id == fishId) {
                    return fish;
                }
            }

            return null;
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

            // Negative disables the creature-threat channel, which is cheaper than testing the config
            // flag once per fish.
            float creatureThreshold = config != null && config.ObjectTypeBehavioursEnabled
                ? config.CreaturePredatorAggressionThreshold
                : -1f;

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
                snapshotThreat[snapshotCount] = creatureThreshold >= 0f && fish.Data != null
                    ? FishBehaviourProfile.EvaluateThreat(fish.Data.Traits, creatureThreshold)
                    : 0f;

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
                snapshotThreat = new float[capacity];
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

            // A partner left waiting for a fish that no longer exists would never brood again.
            if (bonds != null && fish.Data != null) {
                bonds.Forget(fish.Data.Id);
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
