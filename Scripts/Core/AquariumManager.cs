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

        private Vector2[] positionSnapshot = new Vector2[0];
        private IPointerSource pointerSource;
        private RippleField ripples;
        private int snapshotCount;
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
            RebuildPositionSnapshot();

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null) {
                    continue;
                }

                fish.Tick(deltaTime);
            }

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
        }

        /// <summary>
        /// Soft repulsion from nearby fish. Straight iteration over the frame snapshot: with the
        /// configured population ceiling this is far cheaper than colliders or a spatial grid.
        /// </summary>
        public Vector2 CalculateSeparation(FishController self, Vector2 position, float radius) {
            if (self == null || snapshotCount <= 1) {
                return Vector2.zero;
            }

            int selfIndex = self.SnapshotIndex;
            Vector2 accumulated = Vector2.zero;

            for (int i = 0; i < snapshotCount; i++) {
                if (i == selfIndex) {
                    continue;
                }

                accumulated += FishSteering.SeparationContribution(position, positionSnapshot[i], radius);
            }

            return accumulated;
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

        private void HandlePointerPressed(Vector2 worldPosition) {
            if (config == null || !config.PointerInteractionEnabled || !config.RippleOnClickEnabled) {
                return;
            }

            ripples.Emit(worldPosition);
        }

        private void RebuildPositionSnapshot() {
            if (positionSnapshot.Length < activeFish.Count) {
                positionSnapshot = new Vector2[Mathf.NextPowerOfTwo(Mathf.Max(8, activeFish.Count))];
            }

            snapshotCount = 0;

            for (int i = 0; i < activeFish.Count; i++) {
                FishController fish = activeFish[i];
                if (fish == null) {
                    continue;
                }

                fish.SetSnapshotIndex(snapshotCount);
                positionSnapshot[snapshotCount] = fish.Position;
                snapshotCount++;
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
