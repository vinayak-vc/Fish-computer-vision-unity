using NUnit.Framework;

using ViitorCloud.FishAquarium.Fish;
using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// How a payload becomes a personality, including every way the payload can be incomplete.
    ///
    /// The fallback path matters more than it looks: contract section 9 says legacy captures with no
    /// personality block WILL be replayed on any installation with history on disk, and getting the
    /// fallback wrong produces either a tank of identical clones or fish that change character on every
    /// restart. Both are silent failures.
    /// </summary>
    public sealed class FishTraitResolutionTests {
        private const float Tolerance = 0.0005f;

        private static FishCaptureMessage BuildMessage(bool withPersonality) {
            FishCaptureMessage message = new FishCaptureMessage();
            message.id = "fish_20260901_182346_001";
            message.png_base64 = "iVBORw0KGgo=";
            message.schema_version = withPersonality ? 2 : 1;
            message.foreground_area = 311227;

            if (!withPersonality) {
                return message;
            }

            message.object_type = "fish";
            message.object_confidence = 0.81f;

            FishPersonality personality = new FishPersonality();
            personality.speed = 0.81f;
            personality.curiosity = 0.90f;
            personality.fear = 0.22f;
            personality.social = 0.68f;
            personality.aggression = 0.14f;
            personality.grace = 0.55f;
            personality.preferred_depth = 0.40f;
            personality.rarity_score = 0.12f;
            personality.rarity_tier = "common";
            message.personality = personality;

            return message;
        }

        private static FishCaptureMessage BuildMessageOfType(string objectType) {
            FishCaptureMessage message = BuildMessage(true);
            message.object_type = objectType;
            return message;
        }

        [Test]
        public void APersonalityBlockIsUsedVerbatim() {
            FishTraits traits = FishTraitResolver.Resolve(BuildMessage(true));

            Assert.AreEqual(0.81f, traits.Speed, Tolerance);
            Assert.AreEqual(0.90f, traits.Curiosity, Tolerance);
            Assert.AreEqual(0.22f, traits.Fear, Tolerance);
            Assert.AreEqual(0.68f, traits.Social, Tolerance);
            Assert.AreEqual(0.14f, traits.Aggression, Tolerance);
            Assert.AreEqual(0.55f, traits.Grace, Tolerance);
            Assert.AreEqual(0.40f, traits.PreferredDepth, Tolerance);
            Assert.AreEqual(0.12f, traits.RarityScore, Tolerance);
            Assert.AreEqual(FishTraitSource.Payload, traits.Source);
        }

        [Test]
        public void TraitsOutsideZeroToOneAreClamped() {
            FishCaptureMessage message = BuildMessage(true);
            message.personality.speed = 4.2f;
            message.personality.fear = -1f;

            FishTraits traits = FishTraitResolver.Resolve(message);

            Assert.AreEqual(1f, traits.Speed, Tolerance, "A trait is documented as 0..1; a sender bug must not become a rocket fish.");
            Assert.AreEqual(0f, traits.Fear, Tolerance);
        }

        [Test]
        public void AMissingPersonalityFallsBackToTheIdentityHash() {
            FishTraits traits = FishTraitResolver.Resolve(BuildMessage(false));

            Assert.AreEqual(FishTraitSource.DerivedFromIdentity, traits.Source);
            Assert.AreEqual("fish_20260901_182346_001", traits.Identity);
        }

        [Test]
        public void TheFallbackIsStableAcrossRestarts() {
            FishTraits first = FishTraitResolver.Resolve(BuildMessage(false));
            FishTraits second = FishTraitResolver.Resolve(BuildMessage(false));

            Assert.AreEqual(first.Speed, second.Speed, "A legacy fish must not have a new character on every restart.");
            Assert.AreEqual(first.Curiosity, second.Curiosity);
            Assert.AreEqual(first.PreferredDepth, second.PreferredDepth);
        }

        [Test]
        public void TheFallbackDoesNotCloneEveryLegacyFish() {
            FishTraits a = FishTraitFallback.FromIdentity("fish_a");
            FishTraits b = FishTraitFallback.FromIdentity("fish_b");

            Assert.AreNotEqual(a.Speed, b.Speed, "Falling back to a constant would make every legacy fish identical.");
        }

        [Test]
        public void TheFallbackFillsEveryTraitWithADistinctDraw() {
            FishTraits traits = FishTraitFallback.FromIdentity("fish_20260901_182346_001");

            float[] values = { traits.Speed, traits.Curiosity, traits.Fear, traits.Social, traits.Aggression, traits.Grace, traits.PreferredDepth, traits.RarityScore };

            for (int i = 0; i < values.Length; i++) {
                Assert.GreaterOrEqual(values[i], 0f);
                Assert.LessOrEqual(values[i], 1f);

                for (int j = i + 1; j < values.Length; j++) {
                    Assert.AreNotEqual(values[i], values[j], "Traits " + i + " and " + j + " share a draw, so the stream is not advancing.");
                }
            }
        }

        [Test]
        public void ALegacyPayloadStillKeepsAClassificationWhenItCarriesOne() {
            FishCaptureMessage message = BuildMessage(false);
            message.object_type = "jellyfish";
            message.object_confidence = 0.7f;

            FishTraits traits = FishTraitResolver.Resolve(message);

            Assert.AreEqual(FishObjectType.Jellyfish, traits.ObjectType);
            Assert.AreEqual(FishTraitSource.DerivedFromIdentity, traits.Source);
            Assert.IsTrue(traits.IsClassificationConfident);
        }

        [Test]
        public void ANullMessageProducesUsableTraitsRatherThanThrowing() {
            FishTraits traits = FishTraitResolver.Resolve(null);

            Assert.IsNotNull(traits);
            Assert.AreEqual(FishObjectType.Fish, traits.ObjectType);
        }

        [Test]
        public void EveryContractObjectTypeIsRecognised() {
            Assert.AreEqual(FishObjectType.Fish, FishTraitResolver.ParseObjectType("fish"));
            Assert.AreEqual(FishObjectType.Plant, FishTraitResolver.ParseObjectType("plant"));
            Assert.AreEqual(FishObjectType.Rock, FishTraitResolver.ParseObjectType("rock"));
            Assert.AreEqual(FishObjectType.Jellyfish, FishTraitResolver.ParseObjectType("jellyfish"));
            Assert.AreEqual(FishObjectType.Creature, FishTraitResolver.ParseObjectType("creature"));
            Assert.AreEqual(FishObjectType.Unknown, FishTraitResolver.ParseObjectType("unknown"));
        }

        [Test]
        public void AnAbsentObjectTypeReadsAsFish() {
            Assert.AreEqual(FishObjectType.Fish, FishTraitResolver.ParseObjectType(null), "Contract section 9: the legacy fallback is fish.");
            Assert.AreEqual(FishObjectType.Fish, FishTraitResolver.ParseObjectType(string.Empty));
        }

        [Test]
        public void AnUnrecognisedObjectTypeIsSurvivable() {
            Assert.AreEqual(FishObjectType.Unknown, FishTraitResolver.ParseObjectType("kraken"),
                "A classifier this build does not know about must not be fatal.");
        }

        [Test]
        public void ObjectTypeParsingIgnoresCase() {
            Assert.AreEqual(FishObjectType.Plant, FishTraitResolver.ParseObjectType("Plant"));
            Assert.AreEqual(FishObjectType.Rock, FishTraitResolver.ParseObjectType("ROCK"));
        }

        [Test]
        public void PlantsAndRocksAreNotSwimmers() {
            Assert.IsFalse(FishTraitResolver.Resolve(BuildMessageOfType("plant")).IsSwimmer);
            Assert.IsFalse(FishTraitResolver.Resolve(BuildMessageOfType("rock")).IsSwimmer);
            Assert.IsTrue(FishTraitResolver.Resolve(BuildMessageOfType("jellyfish")).IsSwimmer);
            Assert.IsTrue(FishTraitResolver.Resolve(BuildMessageOfType("unknown")).IsSwimmer, "Contract section 5: unknown is treated as a fish.");
        }

        [Test]
        public void ALowConfidenceClassificationIsFlaggedButStillSpawnsItsType() {
            FishCaptureMessage message = BuildMessageOfType("plant");
            message.object_confidence = 0.3f;

            FishTraits traits = FishTraitResolver.Resolve(message);

            Assert.AreEqual(FishObjectType.Plant, traits.ObjectType, "A wrong plant is a better outcome than a rejected drawing.");
            Assert.IsFalse(traits.IsClassificationConfident, "Below 0.5 the classifier is guessing, so type-specific VFX must be skipped.");
        }

        [Test]
        public void APreBucketedRarityTierIsUsedAsSent() {
            Assert.AreEqual(FishRarityTier.Legendary, FishTraitResolver.ParseRarityTier("legendary", 0.1f),
                "Contract section 6 pre-buckets the tier so Unity does not second-guess the thresholds.");
        }

        [Test]
        public void AMissingRarityTierIsBucketedFromTheScore() {
            Assert.AreEqual(FishRarityTier.Common, FishTraitResolver.ParseRarityTier(null, 0.54f));
            Assert.AreEqual(FishRarityTier.Uncommon, FishTraitResolver.ParseRarityTier(null, 0.55f));
            Assert.AreEqual(FishRarityTier.Rare, FishTraitResolver.ParseRarityTier(null, 0.80f));
            Assert.AreEqual(FishRarityTier.Legendary, FishTraitResolver.ParseRarityTier(null, 0.95f));
        }

        [Test]
        public void AnUnrecognisedRarityTierFallsBackToTheScore() {
            Assert.AreEqual(FishRarityTier.Rare, FishTraitResolver.ParseRarityTier("mythic", 0.85f));
        }
    }
}
