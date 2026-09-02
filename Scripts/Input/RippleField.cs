using UnityEngine;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// The shockwaves a visitor's clicks leave in the water. Each one is a radial force field that pushes
    /// fish outwards with F = strength / distance, fading over its lifetime, plus a smaller danger zone at
    /// its centre that fish route around until it dies.
    ///
    /// A plain class rather than a MonoBehaviour: AquariumManager already owns the one Update in the
    /// simulation, and adding a second one for a handful of decaying floats would be the wrong trade.
    ///
    /// Capacity is fixed at construction and ripples live in a pre-allocated array, because a visitor
    /// clicking repeatedly is exactly the moment the frame budget matters and is the worst moment to be
    /// allocating. When the array is full the oldest ripple is replaced.
    /// </summary>
    public sealed class RippleField {
        /// <summary> Stops the force blowing up for a fish sitting exactly on the click point. </summary>
        private const float MinimumDistance = 0.35f;

        private struct Ripple {
            public Vector2 Origin { get; set; }
            public float Age { get; set; }
            public bool Alive { get; set; }
        }

        private readonly Ripple[] ripples;
        private readonly float durationSeconds;
        private readonly float strength;
        private readonly float radius;
        private readonly float dangerRadius;

        private int activeCount;

        public RippleField(int capacity, float rippleDurationSeconds, float rippleStrength, float rippleRadius, float rippleDangerRadius) {
            ripples = new Ripple[Mathf.Max(1, capacity)];
            durationSeconds = Mathf.Max(0.01f, rippleDurationSeconds);
            strength = Mathf.Max(0f, rippleStrength);
            radius = Mathf.Max(0.01f, rippleRadius);
            dangerRadius = Mathf.Clamp(rippleDangerRadius, 0f, radius);
        }

        public int ActiveCount {
            get { return activeCount; }
        }

        /// <summary> Starts a ripple at a world position. Replaces the oldest when every slot is in use. </summary>
        public void Emit(Vector2 origin) {
            int slot = FindFreeSlot();

            ripples[slot].Origin = origin;
            ripples[slot].Age = 0f;

            if (!ripples[slot].Alive) {
                ripples[slot].Alive = true;
                activeCount++;
            }
        }

        /// <summary> Ages every ripple and retires the ones that have decayed. Called once per frame by AquariumManager. </summary>
        public void Tick(float deltaTime) {
            if (activeCount == 0) {
                return;
            }

            for (int i = 0; i < ripples.Length; i++) {
                if (!ripples[i].Alive) {
                    continue;
                }

                ripples[i].Age += deltaTime;

                if (ripples[i].Age >= durationSeconds) {
                    ripples[i].Alive = false;
                    activeCount--;
                }
            }
        }

        /// <summary>
        /// Total outward force at a world position from every live ripple. Returns zero when nothing is
        /// active, which is the common case and costs one comparison.
        /// </summary>
        public Vector2 Evaluate(Vector2 position) {
            if (activeCount == 0) {
                return Vector2.zero;
            }

            Vector2 total = Vector2.zero;

            for (int i = 0; i < ripples.Length; i++) {
                if (!ripples[i].Alive) {
                    continue;
                }

                total += EvaluateOne(ripples[i], position);
            }

            return total;
        }

        /// <summary> True while a live ripple's danger zone covers this position. Fish steer around these. </summary>
        public bool IsInDangerZone(Vector2 position) {
            if (activeCount == 0 || dangerRadius <= 0f) {
                return false;
            }

            float squaredDanger = dangerRadius * dangerRadius;

            for (int i = 0; i < ripples.Length; i++) {
                if (!ripples[i].Alive) {
                    continue;
                }

                if ((position - ripples[i].Origin).sqrMagnitude <= squaredDanger) {
                    return true;
                }
            }

            return false;
        }

        public void Clear() {
            for (int i = 0; i < ripples.Length; i++) {
                ripples[i].Alive = false;
            }

            activeCount = 0;
        }

        private Vector2 EvaluateOne(Ripple ripple, Vector2 position) {
            Vector2 away = position - ripple.Origin;
            float distance = away.magnitude;

            if (distance >= radius) {
                return Vector2.zero;
            }

            Vector2 direction = distance > MinimumDistance ? (away / distance) : Vector2.up;
            float decay = 1f - Mathf.Clamp01(ripple.Age / durationSeconds);

            // F = strength / distance, floored so a fish at the centre gets a hard shove rather than NaN.
            float magnitude = (strength / Mathf.Max(MinimumDistance, distance)) * decay;

            return direction * magnitude;
        }

        /// <summary> A free slot, or the oldest live ripple when there is none. </summary>
        private int FindFreeSlot() {
            int oldestIndex = 0;
            float oldestAge = -1f;

            for (int i = 0; i < ripples.Length; i++) {
                if (!ripples[i].Alive) {
                    return i;
                }

                if (ripples[i].Age > oldestAge) {
                    oldestAge = ripples[i].Age;
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }
    }
}
