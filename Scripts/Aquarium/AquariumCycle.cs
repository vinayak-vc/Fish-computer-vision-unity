using UnityEngine;

namespace ViitorCloud.FishAquarium.Aquarium {
    /// <summary>
    /// The tank's weather and time of day. Design points 14 and 15.
    ///
    /// A full day is compressed into a few minutes, because the acceptance criterion for this milestone is
    /// that the tank is worth watching for two minutes with nobody touching it - a real twenty-four hour
    /// cycle would be indistinguishable from a static scene over that window.
    ///
    /// Storms are scheduled rather than random: they arrive on a fixed interval and last a fixed time,
    /// which makes them testable and, more usefully, makes them predictable for whoever is tuning the
    /// look. Their intensity ramps in and out so nothing snaps.
    ///
    /// Clock-free by design; deltaTime is an argument, so a whole day can be tested in a loop.
    /// </summary>
    public sealed class AquariumCycle {
        private readonly float dayLengthSeconds;
        private readonly float stormIntervalSeconds;
        private readonly float stormDurationSeconds;
        private readonly float stormRampSeconds;

        private float elapsed;
        private float sinceLastStorm;
        private float stormElapsed;
        private bool storming;

        public AquariumCycle(float dayLength, float stormInterval, float stormDuration, float stormRamp) {
            dayLengthSeconds = Mathf.Max(1f, dayLength);
            stormIntervalSeconds = Mathf.Max(1f, stormInterval);
            stormDurationSeconds = Mathf.Max(0f, stormDuration);

            // The ramp cannot exceed half the storm, or a storm would never reach full strength.
            stormRampSeconds = Mathf.Clamp(stormRamp, 0.01f, Mathf.Max(0.02f, stormDurationSeconds * 0.5f));
        }

        /// <summary> 0 is the middle of the night, 0.5 is midday. Wraps. </summary>
        public float TimeOfDay {
            get { return Mathf.Repeat(elapsed / dayLengthSeconds, 1f); }
        }

        /// <summary>
        /// How much daylight there is, 0..1, as a smooth curve rather than a sawtooth: it peaks at midday
        /// and bottoms out at midnight, with dawn and dusk taking real time to pass.
        /// </summary>
        public float Daylight {
            get { return (Mathf.Cos((TimeOfDay + 0.5f) * Mathf.PI * 2f) * 0.5f) + 0.5f; }
        }

        public bool IsStorming {
            get { return storming; }
        }

        /// <summary> 0 outside a storm, ramping to 1 at its height. Drives turbulence and the water's colour. </summary>
        public float StormIntensity {
            get {
                if (!storming || stormDurationSeconds <= 0f) {
                    return 0f;
                }

                float rampIn = Mathf.Clamp01(stormElapsed / stormRampSeconds);
                float rampOut = Mathf.Clamp01((stormDurationSeconds - stormElapsed) / stormRampSeconds);
                return Mathf.Clamp01(Mathf.Min(rampIn, rampOut));
            }
        }

        public void Tick(float deltaTime) {
            if (deltaTime <= 0f) {
                return;
            }

            elapsed += deltaTime;

            if (storming) {
                stormElapsed += deltaTime;

                if (stormElapsed >= stormDurationSeconds) {
                    storming = false;
                    stormElapsed = 0f;
                    sinceLastStorm = 0f;
                }

                return;
            }

            sinceLastStorm += deltaTime;

            if (sinceLastStorm >= stormIntervalSeconds) {
                storming = true;
                stormElapsed = 0f;
            }
        }

        /// <summary> Starts a storm now. Exposed so the debug overlay does not need to wait for one. </summary>
        public void ForceStorm() {
            storming = true;
            stormElapsed = 0f;
        }

        public void Reset() {
            elapsed = 0f;
            sinceLastStorm = 0f;
            stormElapsed = 0f;
            storming = false;
        }
    }
}
