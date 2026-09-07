namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// What Python decided was drawn, from section 5 of the interaction contract. Unknown is treated as
    /// Fish everywhere a behaviour has to be chosen; the distinction is kept only so the debug overlay can
    /// show that the classifier abstained rather than guessed.
    /// </summary>
    public enum FishObjectType {
        Fish = 0,
        Plant = 1,
        Rock = 2,
        Jellyfish = 3,
        Creature = 4,
        Unknown = 5
    }

    /// <summary> Pre-bucketed rarity from the payload, so Unity does not hardcode the score thresholds. </summary>
    public enum FishRarityTier {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Legendary = 3
    }

    /// <summary> Where a fish's personality came from. Surfaced in the debug overlay, never used to branch behaviour. </summary>
    public enum FishTraitSource {
        /// <summary> Python sent a personality block and it was used verbatim. </summary>
        Payload = 0,

        /// <summary> No personality block, so every trait was derived from the identity hash. Legacy captures and folder fish. </summary>
        DerivedFromIdentity = 1
    }
}
