namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// SplitMix64 value stream. Given the same seed it yields the same sequence, forever, on any platform.
    ///
    /// Used everywhere the aquarium needs a per-fish value that must not change between runs: the
    /// personality fallback of interaction_contract.md section 9, and the parallax depth, noise seed and
    /// animation phase that are just as visible to a returning visitor as a trait would be.
    ///
    /// A struct, and passed by ref, so drawing values costs nothing and the advancing state is not
    /// accidentally copied - a copied stream would silently repeat itself. The algorithm is specified in
    /// docs/decisions.md D-002 and is mirrored on the Python side.
    /// </summary>
    public struct DeterministicRandom {
        private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
        private const ulong MixA = 0xBF58476D1CE4E5B9UL;
        private const ulong MixB = 0x94D049BB133111EBUL;

        /// <summary> 2^53, the largest integer a double represents exactly. Divisor for the unit interval. </summary>
        private const double UnitDivisor = 9007199254740992.0;

        private ulong state;

        public DeterministicRandom(ulong seed) {
            state = seed;
        }

        public static DeterministicRandom FromIdentity(string identity) {
            return new DeterministicRandom(StableHash.Of(identity));
        }

        /// <summary>
        /// A second, independent stream for the same identity. Domain separation keeps two uses of one id
        /// from correlating: the personality fallback and the parallax and wander values both have to be
        /// stable across restarts, but a fish whose swimming depth tracked its speed because both came off
        /// the same draw would look like a bug. Only the undomained stream is mirrored on the Python side.
        /// </summary>
        public static DeterministicRandom FromIdentity(string identity, string domain) {
            return new DeterministicRandom(StableHash.Of(identity + "|" + domain));
        }

        /// <summary> Next raw 64-bit value. Advances the stream. </summary>
        public ulong NextUInt64() {
            unchecked {
                state += GoldenGamma;

                ulong z = state;
                z = (z ^ (z >> 30)) * MixA;
                z = (z ^ (z >> 27)) * MixB;
                return z ^ (z >> 31);
            }
        }

        /// <summary> Next value in [0, 1). Uses the top 53 bits, which are the well-mixed ones. </summary>
        public float NextUnit() {
            return (float)((NextUInt64() >> 11) * (1.0 / UnitDivisor));
        }

        /// <summary> Next value in [minimum, maximum). Equivalent to Random.Range, minus the randomness. </summary>
        public float NextRange(float minimum, float maximum) {
            return minimum + ((maximum - minimum) * NextUnit());
        }
    }
}
