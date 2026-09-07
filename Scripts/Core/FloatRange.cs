using System;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary> Inclusive min/max float pair used by AquariumConfig for every randomised tuning value. </summary>
    [Serializable]
    public struct FloatRange {
        [SerializeField] private float min;
        [SerializeField] private float max;

        public FloatRange(float minimum, float maximum) {
            min = minimum;
            max = maximum;
        }

        public float Min {
            get { return min; }
        }

        public float Max {
            get { return max; }
        }

        /// <summary> Maps a 0..1 value onto the range. </summary>
        public float Evaluate(float normalized) {
            return Mathf.LerpUnclamped(min, max, normalized);
        }

        public float PickRandom() {
            return UnityEngine.Random.Range(min, max);
        }

        public float Clamp(float value) {
            return Mathf.Clamp(value, Mathf.Min(min, max), Mathf.Max(min, max));
        }

        /// <summary> Swaps min/max when an inspector edit inverts them. Call from OnValidate. </summary>
        public FloatRange Normalized() {
            if (min <= max) {
                return this;
            }

            return new FloatRange(max, min);
        }
    }
}
