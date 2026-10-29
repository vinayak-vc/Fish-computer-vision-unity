using System;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Translates one capture payload into the immutable FishTraits the rest of the aquarium reads.
    /// This is the only place the snake_case wire shape meets domain code.
    ///
    /// Every optional block is allowed to be absent or null: section 3 rule 2 of the interaction contract
    /// requires it, and legacy schema v1 captures replayed from disk will exercise the path on any
    /// installation that has been running for a while. A missing personality block is not an error, it is
    /// the documented fallback in section 9, and FishTraitFallback supplies it deterministically.
    /// </summary>
    public static class FishTraitResolver {
        /// <summary> Traits for one payload. A null message, or one without a personality block, falls back to the identity hash. </summary>
        public static FishTraits Resolve(FishCaptureMessage message) {
            if (message == null) {
                return FishTraitFallback.FromIdentity(string.Empty);
            }

            FishObjectType objectType = ParseObjectType(message.object_type);
            FishPersonality personality = message.personality;

            if (personality == null) {
                FishTraits derived = FishTraitFallback.FromIdentity(message.id);

                // The classification is the one thing a v1 payload can still carry usefully, so keep it
                // rather than discarding it along with the missing personality.
                if (message.object_type == null) {
                    return derived;
                }

                return new FishTraits(derived.Identity, derived.Speed, derived.Curiosity, derived.Fear, derived.Social,
                    derived.Aggression, derived.Grace, derived.PreferredDepth, derived.RarityScore, derived.RarityTier,
                    objectType, message.object_confidence, FishTraitSource.DerivedFromIdentity);
            }

            FishRarityTier rarityTier = ParseRarityTier(personality.rarity_tier, personality.rarity_score);

            return new FishTraits(message.id, personality.speed, personality.curiosity, personality.fear,
                personality.social, personality.aggression, personality.grace, personality.preferred_depth,
                personality.rarity_score, rarityTier, objectType, message.object_confidence, FishTraitSource.Payload,
                ResolveDominantColour(message.features));
        }

        /// <summary>
        /// The drawing's strongest colour, from features.dominant_colors[0]. White when the payload had no
        /// features block, no colours, or an unparseable hex value.
        ///
        /// Python computed this from the segmentation mask, which is the whole point: Unity must not
        /// average a flattened PNG to guess at it. Only used for presentation - a baby's tint.
        /// </summary>
        public static Color ResolveDominantColour(FishFeatures features) {
            if (features == null || features.dominant_colors == null || features.dominant_colors.Length == 0) {
                return Color.white;
            }

            // Descending by ratio per contract section 4, so the first entry is the strongest.
            FishDominantColor strongest = features.dominant_colors[0];

            Color parsed;
            if (strongest == null || !TryParseHexColour(strongest.hex, out parsed)) {
                return Color.white;
            }

            return parsed;
        }

        /// <summary>
        /// Parses "#rrggbb" by hand rather than through ColorUtility.
        ///
        /// Trait resolution runs on a decode worker, and UnityEngine statics are not documented as
        /// thread-safe. Six hex digits is arithmetic, so doing it here removes the question entirely.
        /// </summary>
        public static bool TryParseHexColour(string hex, out Color colour) {
            colour = Color.white;

            if (string.IsNullOrEmpty(hex)) {
                return false;
            }

            int start = hex[0] == '#' ? 1 : 0;
            if (hex.Length - start < 6) {
                return false;
            }

            int red, green, blue;
            if (!TryParseHexByte(hex, start, out red) ||
                !TryParseHexByte(hex, start + 2, out green) ||
                !TryParseHexByte(hex, start + 4, out blue)) {
                return false;
            }

            colour = new Color(red / 255f, green / 255f, blue / 255f, 1f);
            return true;
        }

        private static bool TryParseHexByte(string hex, int index, out int value) {
            value = 0;

            int high = HexDigit(hex[index]);
            int low = HexDigit(hex[index + 1]);

            if (high < 0 || low < 0) {
                return false;
            }

            value = (high * 16) + low;
            return true;
        }

        private static int HexDigit(char character) {
            if (character >= '0' && character <= '9') {
                return character - '0';
            }

            if (character >= 'a' && character <= 'f') {
                return (character - 'a') + 10;
            }

            if (character >= 'A' && character <= 'F') {
                return (character - 'A') + 10;
            }

            return -1;
        }

        /// <summary>
        /// Section 5 of the contract. A null or empty value means a legacy payload that predates
        /// classification, and section 9 says to read that as a fish. A value this build does not
        /// recognise becomes Unknown, which behaves as a fish but tells the debug overlay the truth.
        /// </summary>
        public static FishObjectType ParseObjectType(string value) {
            if (string.IsNullOrEmpty(value)) {
                return FishObjectType.Fish;
            }

            if (string.Equals(value, "fish", StringComparison.OrdinalIgnoreCase)) {
                return FishObjectType.Fish;
            }

            if (string.Equals(value, "plant", StringComparison.OrdinalIgnoreCase)) {
                return FishObjectType.Plant;
            }

            if (string.Equals(value, "rock", StringComparison.OrdinalIgnoreCase)) {
                return FishObjectType.Rock;
            }

            if (string.Equals(value, "jellyfish", StringComparison.OrdinalIgnoreCase)) {
                return FishObjectType.Jellyfish;
            }

            if (string.Equals(value, "creature", StringComparison.OrdinalIgnoreCase)) {
                return FishObjectType.Creature;
            }

            return FishObjectType.Unknown;
        }

        /// <summary> Uses the pre-bucketed tier when the payload sent one, and buckets the score itself when it did not. </summary>
        public static FishRarityTier ParseRarityTier(string value, float rarityScore) {
            if (string.IsNullOrEmpty(value)) {
                return FishTraits.BucketRarity(rarityScore);
            }

            if (string.Equals(value, "common", StringComparison.OrdinalIgnoreCase)) {
                return FishRarityTier.Common;
            }

            if (string.Equals(value, "uncommon", StringComparison.OrdinalIgnoreCase)) {
                return FishRarityTier.Uncommon;
            }

            if (string.Equals(value, "rare", StringComparison.OrdinalIgnoreCase)) {
                return FishRarityTier.Rare;
            }

            if (string.Equals(value, "legendary", StringComparison.OrdinalIgnoreCase)) {
                return FishRarityTier.Legendary;
            }

            return FishTraits.BucketRarity(rarityScore);
        }
    }
}
