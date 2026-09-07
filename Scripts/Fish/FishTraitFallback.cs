using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Personality for a creature whose payload carried none: legacy schema v1 captures replayed from
    /// disk, and every fish that arrives as a file in the watched folder.
    ///
    /// Section 9 of the interaction contract fixes the rule, and both sides must implement it identically
    /// so an archived fish behaves the same as a live one. Never a constant - every legacy fish would
    /// become an identical clone. Never Random.value - the same fish would have a new character on every
    /// restart, and a returning visitor would not recognise their own drawing.
    ///
    /// The draw order below is part of the contract. Reordering it silently rewrites the personality of
    /// every fish already in the archive. See docs/decisions.md D-002.
    /// </summary>
    public static class FishTraitFallback {
        /// <summary>
        /// Confidence for a derived classification. Zero on purpose: nothing in a legacy payload says what
        /// was drawn, so the type is an assumption, and section 5 asks for type-specific effects to be
        /// skipped when the classifier is not sure.
        /// </summary>
        private const float DerivedObjectConfidence = 0f;

        /// <summary> Deterministic personality for an identity string, which is a capture id or a file path. </summary>
        public static FishTraits FromIdentity(string identity) {
            DeterministicRandom stream = DeterministicRandom.FromIdentity(identity);

            float speed = stream.NextUnit();
            float curiosity = stream.NextUnit();
            float fear = stream.NextUnit();
            float social = stream.NextUnit();
            float aggression = stream.NextUnit();
            float grace = stream.NextUnit();
            float preferredDepth = stream.NextUnit();
            float rarityScore = stream.NextUnit();

            return new FishTraits(identity, speed, curiosity, fear, social, aggression, grace, preferredDepth, rarityScore,
                FishTraits.BucketRarity(rarityScore), FishObjectType.Fish, DerivedObjectConfidence, FishTraitSource.DerivedFromIdentity);
        }
    }
}
