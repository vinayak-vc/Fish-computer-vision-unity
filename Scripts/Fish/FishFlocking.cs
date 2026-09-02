using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// The two flocking rules that were missing, as pure maths: alignment, which turns a fish to match the
    /// heading of the fish around it, and cohesion, which draws it towards their centre. Separation, the
    /// third of the classic three, already lives in FishSteering and is unchanged.
    ///
    /// Kept free of component state so the rules can be tested without a scene, in the same spirit as
    /// FishSteering. The trait weighting is deliberately NOT here: how strongly a given fish feels these
    /// is a question for the response curves on AquariumConfig, where an artist can see it change.
    ///
    /// Every function returns a direction of unit length or zero, never a magnitude. The caller scales.
    /// That keeps a fish with forty neighbours from being pulled forty times harder than one with two.
    /// </summary>
    public static class FishFlocking {
        private const float DirectionEpsilon = 0.0001f;

        /// <summary>
        /// Average heading of the neighbours, normalised. Zero when there are none, or when they are
        /// swimming in exactly opposing directions and cancel out - in which case a fish has no school to
        /// match and should keep its own heading rather than being nudged by a rounding error.
        /// </summary>
        public static Vector2 AlignmentDirection(Vector2 headingSum, int neighbourCount) {
            if (neighbourCount <= 0 || headingSum.sqrMagnitude <= DirectionEpsilon) {
                return Vector2.zero;
            }

            return headingSum.normalized;
        }

        /// <summary>
        /// Direction from the fish towards the centre of mass of its neighbours, normalised. Zero when
        /// there are none, or when the fish is already at that centre.
        /// </summary>
        public static Vector2 CohesionDirection(Vector2 positionSum, int neighbourCount, Vector2 selfPosition) {
            if (neighbourCount <= 0) {
                return Vector2.zero;
            }

            Vector2 centre = positionSum / neighbourCount;
            Vector2 toCentre = centre - selfPosition;

            if (toCentre.sqrMagnitude <= DirectionEpsilon) {
                return Vector2.zero;
            }

            return toCentre.normalized;
        }

        /// <summary>
        /// The pull a newly arrived fish exerts on one onlooker. Design point 16: a fish that has just been
        /// drawn draws a crowd, and the crowd is made of the curious ones.
        ///
        /// Three things scale it, and all three have to be present for anything to happen: how recently the
        /// newcomer arrived, how curious the onlooker is, and how close the two are. An incurious fish
        /// ignores an arrival entirely, which is what makes the ones that do come over read as interested
        /// rather than as the whole tank lurching.
        /// </summary>
        public static Vector2 NoveltyAttraction(Vector2 selfPosition, Vector2 arrivalPosition, float novelty, float curiosity, float radius) {
            float clampedNovelty = Mathf.Clamp01(novelty);
            float clampedCuriosity = Mathf.Clamp01(curiosity);

            if (clampedNovelty <= 0f || clampedCuriosity <= 0f || radius <= Mathf.Epsilon) {
                return Vector2.zero;
            }

            Vector2 toArrival = arrivalPosition - selfPosition;
            float distance = toArrival.magnitude;

            if (distance >= radius || distance <= DirectionEpsilon) {
                return Vector2.zero;
            }

            float closeness = 1f - (distance / radius);
            return (toArrival / distance) * clampedNovelty * clampedCuriosity * closeness;
        }
    }
}
