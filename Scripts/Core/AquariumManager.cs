using System;
using System.Collections.Generic;

using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// Owns the live fish population: registration, the maximum-count policy, removal and the single
    /// per-frame tick that advances every fish. Loading PNGs is deliberately not its job.
    /// </summary>
    public sealed class AquariumManager : MonoBehaviour {
        [SerializeField] private AquariumConfig config;
        [SerializeField] private AquariumBounds bounds;
        [SerializeField] private Transform fishContainer;

        private readonly List<FishController> activeFish = new List<FishController>();
        private readonly List<FishController> completedRemovals = new List<FishController>();

        private Vector2[] positionSnapshot = new Vector2[0];
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
        }

        private void OnEnable() {
            if (bounds != null) {
                bounds.BoundsChanged += HandleBoundsChanged;
            }
        }

        private void OnDisable() {
            if (bounds != null) {
                bounds.BoundsChanged -= HandleBoundsChanged;
            }
        }

        private void Update() {
            float deltaTime = Time.deltaTime;

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
