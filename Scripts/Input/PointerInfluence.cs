using UnityEngine;

namespace ViitorCloud.FishAquarium.Input {
    /// <summary>
    /// The rules governing how a fish reacts to the visitor, as pure functions. No component state and no
    /// Unity lifecycle, so the whole interaction model can be unit tested without a scene - which matters,
    /// because "a first-time visitor works out cause and effect within five seconds" is not something a
    /// test can check, and the arithmetic underneath it is the part that can silently regress.
    ///
    /// Curiosity and fear are not complements. A fish can be both, and the good ones are: it approaches,
    /// then bolts. Nothing here forces them to sum to anything.
    /// </summary>
    public static class PointerInfluence {
        /// <summary> Stops the force blowing up when a fish and the pointer occupy the same point. </summary>
        private const float MinimumDistance = 0.05f;

        /// <summary>
        /// How strongly the pointer is felt at a given distance. Linear, 1 at the pointer and 0 at the
        /// influence radius, so a fish drifting out of range fades out rather than snapping free.
        /// </summary>
        public static float ProximityFalloff(float distance, float influenceRadius) {
            if (influenceRadius <= Mathf.Epsilon || distance >= influenceRadius) {
                return 0f;
            }

            return 1f - Mathf.Clamp01(distance / influenceRadius);
        }

        /// <summary>
        /// How much a fish has come to trust the pointer, 0..1, from its accumulated affection.
        ///
        /// Below the trust threshold affection only takes the edge off fear, reaching 0.5 at the threshold.
        /// At or above it, trust climbs to 1 and the flee response inverts into an approach. Piecewise but
        /// continuous, because a discontinuity here would read as a fish changing its mind for no reason.
        /// </summary>
        public static float Comfort(float affection, float trustThreshold) {
            float clampedAffection = Mathf.Clamp01(affection);
            float clampedThreshold = Mathf.Clamp(trustThreshold, 0.01f, 0.99f);

            if (clampedAffection <= clampedThreshold) {
                return 0.5f * (clampedAffection / clampedThreshold);
            }

            return 0.5f + (0.5f * ((clampedAffection - clampedThreshold) / (1f - clampedThreshold)));
        }

        /// <summary>
        /// Pointer speed at which this fish startles. A fearful fish spooks at the base threshold; a
        /// fearless one needs fearlessMultiplier times as much, so the same flick of the mouse scatters
        /// the nervous fish and is ignored by the bold ones. That contrast is the whole tell that
        /// personality is real.
        /// </summary>
        public static float StartleThreshold(float fear, float baseThreshold, float fearlessMultiplier) {
            float clampedFear = Mathf.Clamp01(fear);
            float multiplier = Mathf.Lerp(Mathf.Max(1f, fearlessMultiplier), 1f, clampedFear);
            return Mathf.Max(0.01f, baseThreshold) * multiplier;
        }

        /// <summary> Whether this pointer movement startles this fish. Affection does not protect against a sudden movement. </summary>
        public static bool ShouldStartle(float pointerSpeed, float distance, float influenceRadius, float fear, float baseThreshold, float fearlessMultiplier) {
            if (distance > influenceRadius) {
                return false;
            }

            return pointerSpeed >= StartleThreshold(fear, baseThreshold, fearlessMultiplier);
        }

        /// <summary>
        /// Steering contribution from the pointer for one fish. Returns an unnormalised force; the caller
        /// blends it with the rest of its steering.
        ///
        /// Three things are summed: attraction weighted by curiosity, repulsion weighted by fear, and a
        /// personal-space push that applies whatever the personality. The last one is what stops a curious
        /// fish parking under the cursor and sitting there, which looks broken rather than interested.
        /// </summary>
        public static Vector2 EvaluateSteering(Vector2 fishPosition, Vector2 pointerPosition, float curiosity, float fear, float comfort, float influenceRadius, float attractionStrength, float repulsionStrength, float personalSpace) {
            Vector2 toPointer = pointerPosition - fishPosition;
            float distance = toPointer.magnitude;

            float falloff = ProximityFalloff(distance, influenceRadius);
            if (falloff <= 0f) {
                return Vector2.zero;
            }

            Vector2 towards = distance > MinimumDistance ? (toPointer / distance) : Vector2.right;
            Vector2 away = -towards;

            float clampedComfort = Mathf.Clamp01(comfort);

            // Comfort below 0.5 softens fear; above 0.5 it cancels fear and pulls curiosity up towards 1,
            // which is what turns a familiar visitor from a threat into something worth swimming to.
            float effectiveFear = Mathf.Clamp01(fear) * (1f - clampedComfort);
            float trust = Mathf.Max(0f, (clampedComfort - 0.5f) * 2f);
            float effectiveCuriosity = Mathf.Clamp01(curiosity) + ((1f - Mathf.Clamp01(curiosity)) * trust);

            Vector2 force = (towards * effectiveCuriosity * attractionStrength * falloff) +
                            (away * effectiveFear * repulsionStrength * falloff);

            float personalSpaceFalloff = ProximityFalloff(distance, personalSpace);
            if (personalSpaceFalloff > 0f) {
                force += away * repulsionStrength * personalSpaceFalloff;
            }

            return force;
        }

        /// <summary>
        /// Change in affection this frame. A pointer that lingers close and slow is company; one that is
        /// far away or moving fast is not, and trust decays. Returns a delta to add to the fish's value.
        /// </summary>
        public static float AffectionDelta(float distance, float pointerSpeed, bool pointerAvailable, float proximityRadius, float pointerSpeedLimit, float gainPerSecond, float decayPerSecond, float deltaTime) {
            bool nearAndCalm = pointerAvailable && distance <= proximityRadius && pointerSpeed <= pointerSpeedLimit;

            if (nearAndCalm) {
                // Closer and slower earns trust faster, so deliberately holding still is rewarded.
                float closeness = ProximityFalloff(distance, proximityRadius);
                float calmness = 1f - Mathf.Clamp01(pointerSpeed / Mathf.Max(0.01f, pointerSpeedLimit));
                return gainPerSecond * Mathf.Max(closeness, 0.25f) * Mathf.Max(calmness, 0.25f) * deltaTime;
            }

            return -decayPerSecond * deltaTime;
        }
    }
}
