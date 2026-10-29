using UnityEngine;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// One number, 0..1, describing how alive the tank feels right now. Design point 11.
    ///
    /// Everything ambient hangs off this: how fast the fish swim, how many bubbles rise, how bright the
    /// water is, how much particle detail there is. Having a single value rather than a dozen independent
    /// ones is the point - it means the whole aquarium swells and settles together, which reads as a mood
    /// rather than as several unrelated effects.
    ///
    /// Two things raise it, and they are the two things that mean somebody is there: the visitor moving
    /// the pointer, and drawings arriving. It falls back to a resting level on its own, so an unattended
    /// tank calms down instead of staying keyed up forever.
    ///
    /// Pure and clock-free - deltaTime comes in as an argument - so the rise and fall can be tested
    /// without waiting for it in real time.
    /// </summary>
    public sealed class AquariumEnergy {
        private readonly float restingLevel;
        private readonly float riseRate;
        private readonly float decayRate;
        private readonly float captureExcitement;

        private float current;

        public AquariumEnergy(float resting, float rise, float decay, float perCaptureExcitement) {
            restingLevel = Mathf.Clamp01(resting);
            riseRate = Mathf.Max(0f, rise);
            decayRate = Mathf.Max(0f, decay);
            captureExcitement = Mathf.Clamp01(perCaptureExcitement);
            current = restingLevel;
        }

        /// <summary> 0 is a still, dim, slow tank; 1 is a busy one. Never leaves that range. </summary>
        public float Current {
            get { return current; }
        }

        public float RestingLevel {
            get { return restingLevel; }
        }

        /// <summary>
        /// Advances the mood. stimulus is 0..1 and is whatever the visitor is currently contributing -
        /// pointer speed, normalised.
        ///
        /// The target is the higher of the resting level and the stimulus, so activity pulls the tank up
        /// and its absence lets it sink back but never below resting. Rise and decay are separate rates
        /// because they should not feel symmetrical: a tank should wake quickly and settle slowly.
        /// </summary>
        public void Tick(float deltaTime, float stimulus) {
            if (deltaTime <= 0f) {
                return;
            }

            float target = Mathf.Max(restingLevel, Mathf.Clamp01(stimulus));
            float rate = target > current ? riseRate : decayRate;

            current = Mathf.MoveTowards(current, target, rate * deltaTime);
            current = Mathf.Clamp01(current);
        }

        /// <summary>
        /// A drawing arrived. Bumps the mood immediately rather than through the stimulus channel, because
        /// a capture is an event with no duration - there is nothing to hold the stimulus high for.
        /// </summary>
        public void RecordCapture() {
            current = Mathf.Clamp01(current + captureExcitement);
        }

        /// <summary> Puts the mood back to rest. Used when the tank is cleared. </summary>
        public void Reset() {
            current = restingLevel;
        }
    }
}
