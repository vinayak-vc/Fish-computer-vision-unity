using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// The personality of one creature: eight traits, each 0..1, plus what Python thought was drawn.
    /// Immutable by construction, because a trait that could change after spawn would break the promise
    /// the whole installation rests on - the same drawing always produces the same creature.
    ///
    /// These are inputs, never behaviour. What speed 0.81 means in world units per second is a Unity
    /// decision and lives in the trait response curves on AquariumConfig, where an artist can see it
    /// change. Nothing here is derived from the PNG; see docs/interaction_contract.md section 1.
    /// </summary>
    public sealed class FishTraits {
        /// <summary> Rarity thresholds, from section 6 of the interaction contract. </summary>
        private const float UncommonThreshold = 0.55f;
        private const float RareThreshold = 0.80f;
        private const float LegendaryThreshold = 0.95f;

        /// <summary> Below this the classifier is guessing, so type-specific VFX are skipped. </summary>
        public const float ConfidentClassificationThreshold = 0.5f;

        public FishTraits(string identity, float speed, float curiosity, float fear, float social, float aggression, float grace, float preferredDepth, float rarityScore, FishRarityTier rarityTier, FishObjectType objectType, float objectConfidence, FishTraitSource source) {
            Identity = string.IsNullOrEmpty(identity) ? string.Empty : identity;
            Speed = Mathf.Clamp01(speed);
            Curiosity = Mathf.Clamp01(curiosity);
            Fear = Mathf.Clamp01(fear);
            Social = Mathf.Clamp01(social);
            Aggression = Mathf.Clamp01(aggression);
            Grace = Mathf.Clamp01(grace);
            PreferredDepth = Mathf.Clamp01(preferredDepth);
            RarityScore = Mathf.Clamp01(rarityScore);
            RarityTier = rarityTier;
            ObjectType = objectType;
            ObjectConfidence = Mathf.Clamp01(objectConfidence);
            Source = source;
        }

        /// <summary> Capture id, or the source file path for a folder fish. The seed every derived value comes from. </summary>
        public string Identity { get; private set; }

        /// <summary> 0 sluggish, 1 darting. Drives max velocity and acceleration. </summary>
        public float Speed { get; private set; }

        /// <summary> 0 ignores the pointer, 1 investigates everything. Attraction to the pointer, food and new arrivals. </summary>
        public float Curiosity { get; private set; }

        /// <summary> 0 unflappable, 1 panics easily. Repulsion weight, flee radius and startle threshold. </summary>
        public float Fear { get; private set; }

        /// <summary> 0 solitary, 1 schools tightly. Boid cohesion and alignment weight. </summary>
        public float Social { get; private set; }

        /// <summary> 0 never chases, 1 chases and nips. Food competition and predator eligibility. </summary>
        public float Aggression { get; private set; }

        /// <summary> 0 jerky, 1 gliding. Steering damping, turn-rate limit and tail amplitude. </summary>
        public float Grace { get; private set; }

        /// <summary>
        /// Vertical home band: 0 hugs the surface, 1 hugs the floor.
        /// Not to be confused with FishData.Depth, which is parallax - how far into the scene the fish is
        /// drawn. They are different axes and mapping one onto the other looks obviously wrong.
        /// </summary>
        public float PreferredDepth { get; private set; }

        /// <summary> 0 ordinary, 1 extraordinary. Gate for glow, trails and a birth fanfare. </summary>
        public float RarityScore { get; private set; }

        public FishRarityTier RarityTier { get; private set; }

        public FishObjectType ObjectType { get; private set; }

        /// <summary> How sure Python's classifier was. Section 5: below 0.5 it is guessing. </summary>
        public float ObjectConfidence { get; private set; }

        public FishTraitSource Source { get; private set; }

        /// <summary>
        /// True when the classification is firm enough to act on visually. A low-confidence drawing still
        /// spawns as its named type - a wrong plant is a better outcome than a rejected drawing - but skips
        /// type-specific effects, so a misclassification is survivable rather than fatal.
        /// </summary>
        public bool IsClassificationConfident {
            get { return ObjectConfidence >= ConfidentClassificationThreshold; }
        }

        /// <summary>
        /// Whether this creature swims. Unknown is treated as a fish, per section 5 of the contract.
        /// Plants and rocks anchor to the floor instead; that behaviour arrives with M8.
        /// </summary>
        public bool IsSwimmer {
            get { return ObjectType != FishObjectType.Plant && ObjectType != FishObjectType.Rock; }
        }

        /// <summary> Buckets a rarity score when the payload did not carry a pre-bucketed tier. </summary>
        public static FishRarityTier BucketRarity(float rarityScore) {
            if (rarityScore >= LegendaryThreshold) {
                return FishRarityTier.Legendary;
            }

            if (rarityScore >= RareThreshold) {
                return FishRarityTier.Rare;
            }

            if (rarityScore >= UncommonThreshold) {
                return FishRarityTier.Uncommon;
            }

            return FishRarityTier.Common;
        }

        public override string ToString() {
            return "spd " + Speed.ToString("F2") + " cur " + Curiosity.ToString("F2") + " fea " + Fear.ToString("F2") +
                   " soc " + Social.ToString("F2") + " agg " + Aggression.ToString("F2") + " gra " + Grace.ToString("F2") +
                   " dep " + PreferredDepth.ToString("F2") + " " + RarityTier + " " + ObjectType +
                   (Source == FishTraitSource.DerivedFromIdentity ? " (derived)" : string.Empty);
        }
    }
}
