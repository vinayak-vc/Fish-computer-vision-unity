using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// What rarity buys a drawing. Design point 7: rarity_tier gates glow, trails and a louder birth
    /// moment.
    ///
    /// Both of the contract's rarity values are used, and for different things, because they are good at
    /// different things. The continuous rarity_score drives the glow, so brightness rises smoothly and no
    /// two rare fish look identically lit. The pre-bucketed rarity_tier gates the trail, because a trail
    /// is on or off and picking a threshold here would mean hardcoding the score boundaries that section
    /// 6 of the contract deliberately buckets for us.
    /// </summary>
    public static class FishRarityProfile {
        /// <summary> Tier as a 0..1 position, so effects can scale across the four steps without a switch. </summary>
        public static float NormaliseTier(FishRarityTier tier) {
            return Mathf.Clamp01((int)tier / 3f);
        }

        /// <summary>
        /// Whether this drawing trails behind itself. On from the given tier upwards.
        ///
        /// Gated on the tier rather than the score so the thresholds stay in one place - Python's - and a
        /// change there does not silently move what an installation shows.
        /// </summary>
        public static bool HasTrail(FishRarityTier tier, FishRarityTier minimumTier) {
            return (int)tier >= (int)minimumTier;
        }

        /// <summary>
        /// Steady glow, from the continuous score through an authored curve. Ordinary drawings get none:
        /// if everything glowed, nothing would look rare.
        /// </summary>
        public static float EvaluateGlow(float rarityScore, AnimationCurve response, float maximumGlow) {
            if (response == null || response.length == 0) {
                return Mathf.Clamp01(rarityScore) * Mathf.Max(0f, maximumGlow);
            }

            return Mathf.Clamp01(response.Evaluate(Mathf.Clamp01(rarityScore))) * Mathf.Max(0f, maximumGlow);
        }

        /// <summary>
        /// Multiplier on the birth moment's length and brightness. A legendary drawing is announced; a
        /// common one just arrives.
        /// </summary>
        public static float EvaluateBirthLoudness(FishRarityTier tier, float loudnessAtLegendary) {
            return Mathf.Lerp(1f, Mathf.Max(1f, loudnessAtLegendary), NormaliseTier(tier));
        }
    }
}
