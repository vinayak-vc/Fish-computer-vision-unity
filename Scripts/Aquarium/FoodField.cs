using System.Collections.Generic;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// Food in the water: where each flake is, how it sinks, and which fish gets it.
    ///
    /// A plain class rather than a MonoBehaviour, matching RippleField. AquariumManager already owns the
    /// one Update in the simulation, and it also means the whole feeding model can be unit tested without
    /// a scene - which matters, because the competition rule is the part that decides whether design
    /// point 2 reads at all.
    ///
    /// Capacity is fixed at construction and flakes live in a pre-allocated array. A visitor feeding
    /// repeatedly is exactly the moment the frame budget is under pressure and the worst moment to be
    /// allocating.
    ///
    /// **Claiming is two-phase, and it has to be.** During a frame each fish close enough to eat submits
    /// a claim; only once every fish has had its say are the winners resolved. Consuming a flake the
    /// instant the first fish touched it would hand it to whichever fish happened to tick first, which is
    /// an arbitrary ordering the visitor cannot see - and aggression, the trait that is supposed to decide
    /// this, would never get a look in.
    /// </summary>
    public sealed class FoodField {
        /// <summary> Sentinel for "no fish has claimed this flake". Fish ids are assigned from 1 upwards. </summary>
        private const int NoClaimant = 0;

        private struct FoodParticle {
            public Vector2 Position { get; set; }
            public float Age { get; set; }
            public bool Alive { get; set; }

            /// <summary> Best claim seen this frame: the id of the most aggressive fish in eating range. </summary>
            public int ClaimantId { get; set; }

            public float ClaimantAggression { get; set; }
        }

        private readonly FoodParticle[] particles;
        private readonly float sinkSpeed;
        private readonly float lifetimeSeconds;

        private int activeCount;

        public FoodField(int capacity, float particleSinkSpeed, float particleLifetimeSeconds) {
            particles = new FoodParticle[Mathf.Max(1, capacity)];
            sinkSpeed = Mathf.Max(0f, particleSinkSpeed);
            lifetimeSeconds = Mathf.Max(0.1f, particleLifetimeSeconds);
        }

        public int ActiveCount {
            get { return activeCount; }
        }

        /// <summary> Pool size. The renderer walks this range, so it is part of the public shape. </summary>
        public int Capacity {
            get { return particles.Length; }
        }

        /// <summary> Drops one flake. Replaces the oldest when every slot is in use. </summary>
        public void Emit(Vector2 position) {
            int slot = FindFreeSlot();

            if (!particles[slot].Alive) {
                activeCount++;
            }

            particles[slot].Position = position;
            particles[slot].Age = 0f;
            particles[slot].Alive = true;
            particles[slot].ClaimantId = NoClaimant;
            particles[slot].ClaimantAggression = 0f;
        }

        /// <summary>
        /// Sinks every flake and retires the ones that have gone stale. floorY is where food comes to rest,
        /// which should be somewhere a fish can still reach rather than the very bottom of the glass.
        /// </summary>
        public void Tick(float deltaTime, float floorY) {
            if (activeCount == 0) {
                return;
            }

            for (int i = 0; i < particles.Length; i++) {
                if (!particles[i].Alive) {
                    continue;
                }

                particles[i].Age += deltaTime;

                if (particles[i].Age >= lifetimeSeconds) {
                    Kill(i);
                    continue;
                }

                Vector2 position = particles[i].Position;
                position.y = Mathf.Max(floorY, position.y - (sinkSpeed * deltaTime));
                particles[i].Position = position;
            }
        }

        /// <summary>
        /// Nearest live flake within radius, or -1 when there is none. A linear scan over the pool: food is
        /// sparse by design, so indexing it would cost more bookkeeping than the scan costs.
        /// </summary>
        public int FindNearest(Vector2 position, float radius, out Vector2 particlePosition, out float distance) {
            particlePosition = Vector2.zero;
            distance = 0f;

            if (activeCount == 0 || radius <= 0f) {
                return -1;
            }

            int best = -1;
            float bestSquared = radius * radius;

            for (int i = 0; i < particles.Length; i++) {
                if (!particles[i].Alive) {
                    continue;
                }

                float squared = (particles[i].Position - position).sqrMagnitude;
                if (squared > bestSquared) {
                    continue;
                }

                bestSquared = squared;
                best = i;
            }

            if (best < 0) {
                return -1;
            }

            particlePosition = particles[best].Position;
            distance = Mathf.Sqrt(bestSquared);
            return best;
        }

        /// <summary>
        /// Stakes a claim on a flake this frame. The most aggressive claimant wins; on an exact tie the
        /// first to claim keeps it, which makes the outcome depend on tick order only in the one case
        /// where the trait that is meant to decide cannot.
        ///
        /// Design point 2: curiosity decides who swims over, aggression decides who actually gets fed.
        /// </summary>
        public void SubmitClaim(int index, float aggression, int claimantId) {
            if (index < 0 || index >= particles.Length || !particles[index].Alive || claimantId == NoClaimant) {
                return;
            }

            if (particles[index].ClaimantId != NoClaimant && aggression <= particles[index].ClaimantAggression) {
                return;
            }

            particles[index].ClaimantId = claimantId;
            particles[index].ClaimantAggression = aggression;
        }

        /// <summary>
        /// Consumes every claimed flake and reports which fish ate. Called once per frame, after every fish
        /// has ticked, and it clears the frame's claims whether or not anything was eaten.
        /// </summary>
        public int ResolveClaims(List<int> winnerIds) {
            if (winnerIds == null) {
                return 0;
            }

            winnerIds.Clear();

            if (activeCount == 0) {
                return 0;
            }

            for (int i = 0; i < particles.Length; i++) {
                if (!particles[i].Alive || particles[i].ClaimantId == NoClaimant) {
                    continue;
                }

                winnerIds.Add(particles[i].ClaimantId);
                Kill(i);
            }

            return winnerIds.Count;
        }

        /// <summary> Reads one pool slot for the renderer. normalisedAge runs 0 at the drop to 1 as it goes stale. </summary>
        public bool TryGetParticle(int index, out Vector2 position, out float normalisedAge) {
            position = Vector2.zero;
            normalisedAge = 0f;

            if (index < 0 || index >= particles.Length || !particles[index].Alive) {
                return false;
            }

            position = particles[index].Position;
            normalisedAge = Mathf.Clamp01(particles[index].Age / lifetimeSeconds);
            return true;
        }

        public void Clear() {
            for (int i = 0; i < particles.Length; i++) {
                particles[i].Alive = false;
                particles[i].ClaimantId = NoClaimant;
                particles[i].ClaimantAggression = 0f;
            }

            activeCount = 0;
        }

        private void Kill(int index) {
            particles[index].Alive = false;
            particles[index].ClaimantId = NoClaimant;
            particles[index].ClaimantAggression = 0f;
            activeCount--;
        }

        /// <summary> A free slot, or the stalest live flake when there is none. </summary>
        private int FindFreeSlot() {
            int oldestIndex = 0;
            float oldestAge = -1f;

            for (int i = 0; i < particles.Length; i++) {
                if (!particles[i].Alive) {
                    return i;
                }

                if (particles[i].Age > oldestAge) {
                    oldestAge = particles[i].Age;
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }
    }
}
