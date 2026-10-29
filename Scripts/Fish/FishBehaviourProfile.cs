using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// What each kind of drawing does, from section 5 of the interaction contract and design points 21
    /// and 22.
    ///
    /// A set of flags rather than a class per creature. Every type shares the same movement code and
    /// differs only in which parts of it apply, so subclassing would mean five near-identical files and
    /// a factory that has to decide between them - for behaviour that is really four booleans.
    ///
    /// **A misclassification must be survivable, never fatal.** Python's classifier is a heuristic over
    /// aspect ratio, verticality, green-hue fraction, solidity and position on the sheet; it will be
    /// wrong sometimes. So an unrecognised type behaves as a fish rather than throwing, a plant that was
    /// really a fish sits on the floor and sways instead of vanishing, and nothing here can reject a
    /// drawing. A wrong plant is a better outcome than a rejected drawing.
    /// </summary>
    public static class FishBehaviourProfile {
        /// <summary> Whether this type holds a spot on the floor instead of swimming. Plants and rocks. </summary>
        public static bool Anchors(FishObjectType type) {
            return type == FishObjectType.Plant || type == FishObjectType.Rock;
        }

        /// <summary> Whether it moves under its own power at all. A rock does not. </summary>
        public static bool Swims(FishObjectType type) {
            return type != FishObjectType.Plant && type != FishObjectType.Rock;
        }

        /// <summary>
        /// Whether it joins a school. Contract section 5: a jellyfish ignores flocking - it is a drifter,
        /// not a shoal member, and a pulsing bell keeping formation with a school of fish would look
        /// wrong in a way that is hard to unsee.
        ///
        /// Separation still applies to everything; that is collision avoidance, not sociability.
        /// </summary>
        public static bool Flocks(FishObjectType type) {
            return type == FishObjectType.Fish || type == FishObjectType.Creature || type == FishObjectType.Unknown;
        }

        /// <summary> Whether it swims by pulsing vertically rather than by beating a tail. Jellyfish. </summary>
        public static bool Pulses(FishObjectType type) {
            return type == FishObjectType.Jellyfish;
        }

        /// <summary> Whether it sways in place. Plants do; a rock is a rock. </summary>
        public static bool Sways(FishObjectType type) {
            return type == FishObjectType.Plant;
        }

        /// <summary>
        /// Whether other fish should be wary of it. Contract section 5: a creature is a swimmer but is
        /// eligible to be a predator.
        ///
        /// Eligible, not automatically one - it also has to be aggressive enough, and the classifier has
        /// to have been confident. A timid creature is just an odd-looking fish.
        /// </summary>
        public static bool IsPredatorEligible(FishObjectType type) {
            return type == FishObjectType.Creature;
        }

        /// <summary>
        /// How frightening one drawing is to its neighbours, 0..1.
        ///
        /// Zero for everything that is not a confidently classified, aggressive creature. Confidence is
        /// part of the test because a mistaken creature should not empty the tank: section 5 asks for
        /// type-specific effects to be skipped when the classifier was guessing, and a fish that clears
        /// the water around it is the most disruptive effect there is.
        /// </summary>
        public static float EvaluateThreat(FishTraits traits, float aggressionThreshold) {
            if (traits == null || !IsPredatorEligible(traits.ObjectType) || !traits.IsClassificationConfident) {
                return 0f;
            }

            if (traits.Aggression < aggressionThreshold) {
                return 0f;
            }

            // Ramps from the threshold to full aggression, so the meekest qualifying creature is only
            // mildly alarming and the worst is properly so.
            return Mathf.Clamp01(Mathf.InverseLerp(aggressionThreshold, 1f, traits.Aggression));
        }

        /// <summary>
        /// Where an anchored drawing sits: on the floor, at the height its own size needs so it is not
        /// half-buried in the gravel.
        /// </summary>
        public static float EvaluateAnchorHeight(Rect area, float worldHeight) {
            return area.yMin + (Mathf.Max(0f, worldHeight) * 0.5f);
        }
    }
}
