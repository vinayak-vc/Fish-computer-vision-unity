using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Makes a baby's personality out of its parents'. Design point 5: a noisy average of theirs.
    ///
    /// The noise matters as much as the average. A pure mean would make every baby of two given parents
    /// identical, and over an evening of pairings the whole tank would converge on one middling
    /// personality - the opposite of what an aquarium full of individual drawings is for. The jitter keeps
    /// siblings distinct and keeps the population from flattening.
    ///
    /// Deterministic all the same: the stream is seeded from both parents' identities and the number of
    /// broods they have had, so the same pair's third baby is always the same baby. A baby is not a
    /// drawing and carries no promise to a visitor, but a reproducible one is easier to reason about and
    /// costs nothing.
    /// </summary>
    public static class FishTraitBlend {
        private const string BroodDomain = "brood";

        /// <summary>
        /// Blends two parents. jitter is the maximum a trait can wander from the midpoint, so 0 gives an
        /// exact average and 0.5 gives a baby that may resemble neither parent much.
        ///
        /// The identity is generated rather than taken from a capture, because there is no capture: a baby
        /// was never drawn, never archived, and Python never hears about it.
        /// </summary>
        public static FishTraits Blend(FishTraits first, FishTraits second, int broodNumber, float jitter, string babyIdentity) {
            if (first == null && second == null) {
                return FishTraitFallback.FromIdentity(babyIdentity);
            }

            if (first == null) {
                return second;
            }

            if (second == null) {
                return first;
            }

            DeterministicRandom stream = DeterministicRandom.FromIdentity(first.Identity + "+" + second.Identity + "#" + broodNumber.ToString(), BroodDomain);
            float clampedJitter = Mathf.Clamp01(jitter);

            float speed = Mix(first.Speed, second.Speed, clampedJitter, ref stream);
            float curiosity = Mix(first.Curiosity, second.Curiosity, clampedJitter, ref stream);
            float fear = Mix(first.Fear, second.Fear, clampedJitter, ref stream);
            float social = Mix(first.Social, second.Social, clampedJitter, ref stream);
            float aggression = Mix(first.Aggression, second.Aggression, clampedJitter, ref stream);
            float grace = Mix(first.Grace, second.Grace, clampedJitter, ref stream);
            float preferredDepth = Mix(first.PreferredDepth, second.PreferredDepth, clampedJitter, ref stream);

            // Rarity is inherited as the plain average, with no jitter. Rarity gates the loud visual
            // effects of M9, and letting it drift upwards through breeding would turn a tank of ordinary
            // drawings into a tank of legendaries within an evening.
            float rarityScore = (first.RarityScore + second.RarityScore) * 0.5f;

            // A baby takes after whichever parent is the more sociable, because that is the parent whose
            // company it keeps. An arbitrary rule, but a consistent one, and it beats a coin toss.
            FishObjectType objectType = first.Social >= second.Social ? first.ObjectType : second.ObjectType;

            Color colour = Color.Lerp(first.DominantColour, second.DominantColour, 0.5f);

            return new FishTraits(babyIdentity, speed, curiosity, fear, social, aggression, grace, preferredDepth,
                rarityScore, FishTraits.BucketRarity(rarityScore), objectType,
                Mathf.Min(first.ObjectConfidence, second.ObjectConfidence), FishTraitSource.Inherited, colour);
        }

        /// <summary> Midpoint of the two, nudged by up to jitter in either direction, then clamped. </summary>
        private static float Mix(float first, float second, float jitter, ref DeterministicRandom stream) {
            float midpoint = (first + second) * 0.5f;
            float nudge = (stream.NextUnit() * 2f - 1f) * jitter;
            return Mathf.Clamp01(midpoint + nudge);
        }

        /// <summary>
        /// On-screen size for a baby: the average of its parents', shrunk. Design point 5 asks for a small
        /// fish whose scale is blended from the parents', which is exactly this.
        /// </summary>
        public static float BlendWorldScale(float firstScale, float secondScale, float babyFactor) {
            return (firstScale + secondScale) * 0.5f * Mathf.Clamp(babyFactor, 0.05f, 1f);
        }
    }
}
