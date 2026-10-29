using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;
using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Rarity effects and anatomy landmarks. Design points 7 and 26.
    ///
    /// The coordinate tests are the important half. Section 7 of the contract says to read the convention
    /// twice: landmarks are normalised against the PNG with the origin at the TOP-LEFT and +y DOWN, and
    /// Unity's sprites start at the BOTTOM-LEFT. Getting the flip wrong puts every eye on the fish's
    /// belly, and the symptom reads as a detection failure rather than a coordinate bug - so it gets
    /// investigated in Python, where there is nothing wrong.
    /// </summary>
    public sealed class FishRarityAndAnatomyTests {
        private const float Tolerance = 0.0005f;

        private static FishAnatomy BuildAnatomy(float confidence, float eyeX, float eyeY, float eyeR) {
            FishAnatomy anatomy = new FishAnatomy();
            anatomy.confidence = confidence;

            FishAnatomyEye eye = new FishAnatomyEye();
            eye.x = eyeX;
            eye.y = eyeY;
            eye.r = eyeR;
            anatomy.eyes = new FishAnatomyEye[] { eye };

            FishAnatomyPoint tail = new FishAnatomyPoint();
            tail.x = 0.93f;
            tail.y = 0.46f;
            anatomy.tail = tail;

            return anatomy;
        }

        [Test]
        public void TheYAxisIsFlippedOutOfPythonsConvention() {
            // A landmark at the TOP of the PNG (y near 0, OpenCV) must land near the TOP of the sprite
            // (v near 1, Unity). This single assertion is the one that stops every eye ending up on the
            // fish's belly.
            Assert.AreEqual(0.9f, FishAnatomyLandmarks.ToSpriteUv(0.5f, 0.1f).y, Tolerance);
            Assert.AreEqual(0.1f, FishAnatomyLandmarks.ToSpriteUv(0.5f, 0.9f).y, Tolerance);
        }

        [Test]
        public void TheXAxisIsNotFlipped() {
            Assert.AreEqual(0.25f, FishAnatomyLandmarks.ToSpriteUv(0.25f, 0.5f).x, Tolerance);
        }

        [Test]
        public void TheCentreIsUnchangedByTheFlip() {
            Vector2 centre = FishAnatomyLandmarks.ToSpriteUv(0.5f, 0.5f);

            Assert.AreEqual(0.5f, centre.x, Tolerance);
            Assert.AreEqual(0.5f, centre.y, Tolerance);
        }

        [Test]
        public void LandmarksOutsideTheImageAreClamped() {
            Vector2 uv = FishAnatomyLandmarks.ToSpriteUv(4f, -3f);

            Assert.GreaterOrEqual(uv.x, 0f);
            Assert.LessOrEqual(uv.x, 1f);
            Assert.GreaterOrEqual(uv.y, 0f);
            Assert.LessOrEqual(uv.y, 1f);
        }

        [Test]
        public void ALowConfidenceBlockIsTreatedAsAbsent() {
            // Section 7: below 0.4 the block is not to be used, however populated it looks.
            Assert.IsTrue(FishAnatomyLandmarks.IsUsable(BuildAnatomy(0.6f, 0.2f, 0.3f, 0.04f)));
            Assert.IsFalse(FishAnatomyLandmarks.IsUsable(BuildAnatomy(0.2f, 0.2f, 0.3f, 0.04f)));
            Assert.IsFalse(FishAnatomyLandmarks.IsUsable(null), "anatomy is null until Python phase 2, and may stay null.");
        }

        [Test]
        public void AUsableLandmarkIsPreferredOverGeometry() {
            Vector2 uv;
            float radius;
            FishAnatomyLandmarks.ResolveEye(BuildAnatomy(0.8f, 0.21f, 0.34f, 0.04f), 0f, out uv, out radius);

            Assert.AreEqual(0.21f, uv.x, Tolerance);
            Assert.AreEqual(1f - 0.34f, uv.y, Tolerance, "Flipped, not passed through.");
            Assert.AreEqual(0.04f, radius, Tolerance);
        }

        [Test]
        public void AMissingLandmarkStillProducesAnEye() {
            // The fallback is the path that actually runs: anatomy is null today, and children's drawings
            // defeat landmark detection routinely even once it ships. A fish with no eye looks dead.
            Vector2 uv;
            float radius;
            FishAnatomyLandmarks.ResolveEye(null, 0f, out uv, out radius);

            Assert.Greater(radius, 0f);
            Assert.GreaterOrEqual(uv.x, 0f);
            Assert.LessOrEqual(uv.x, 1f);
        }

        [Test]
        public void AnEmptyEyeListFallsBackRatherThanThrowing() {
            FishAnatomy anatomy = new FishAnatomy();
            anatomy.confidence = 0.9f;
            anatomy.eyes = new FishAnatomyEye[0];

            Vector2 uv;
            float radius;
            FishAnatomyLandmarks.ResolveEye(anatomy, 0f, out uv, out radius);

            Assert.Greater(radius, 0f, "Zero or two eyes are both allowed by section 7.");
        }

        [Test]
        public void ADetectedEyeWithNoSizeFallsBackToADefault() {
            Vector2 uv;
            float radius;
            FishAnatomyLandmarks.ResolveEye(BuildAnatomy(0.9f, 0.2f, 0.3f, 0f), 0f, out uv, out radius);

            Assert.Greater(radius, 0f, "A detected eye with no radius would be drawn as nothing at all.");
        }

        [Test]
        public void TheFallbackHeadIsAtTheLeadingEndOfTheMajorAxis() {
            // Section 7's geometric default. orientation 0 means the body lies along +x, so the head is
            // at the right-hand end.
            Vector2 head = FishAnatomyLandmarks.FallbackHeadUv(0f);

            Assert.Greater(head.x, 0.9f);
            Assert.AreEqual(0.5f, head.y, 0.01f);
        }

        [Test]
        public void TheFallbackHeadFollowsTheOrientationAndFlipsWithIt() {
            // orientation_deg is measured in image coordinates, where +y points down, so a positive angle
            // is a nose-down angle once it reaches Unity space.
            Vector2 positiveAngle = FishAnatomyLandmarks.FallbackHeadUv(45f);
            Vector2 negativeAngle = FishAnatomyLandmarks.FallbackHeadUv(-45f);

            Assert.Less(positiveAngle.y, 0.5f);
            Assert.Greater(negativeAngle.y, 0.5f);
        }

        [Test]
        public void TheFallbackEyeSitsOnTheHeadRatherThanTheMiddle() {
            Vector2 eye = FishAnatomyLandmarks.FallbackEyeUv(0f);

            Assert.Greater(eye.x, 0.5f, "A fifth of the way back from the nose is still the front half.");
            Assert.Less(eye.x, 1f);
        }

        [Test]
        public void TheFallbackEyeIsOffTheCentreLine() {
            Vector2 eye = FishAnatomyLandmarks.FallbackEyeUv(0f);

            Assert.AreNotEqual(0.5f, eye.y, "An eye exactly on the axis reads as being down the fish's throat.");
        }

        [Test]
        public void EveryFallbackEyeStaysInsideTheSprite() {
            for (int degrees = -90; degrees <= 90; degrees += 5) {
                Vector2 eye = FishAnatomyLandmarks.FallbackEyeUv(degrees);

                Assert.GreaterOrEqual(eye.x, 0f, "at " + degrees);
                Assert.LessOrEqual(eye.x, 1f, "at " + degrees);
                Assert.GreaterOrEqual(eye.y, 0f, "at " + degrees);
                Assert.LessOrEqual(eye.y, 1f, "at " + degrees);
            }
        }

        [Test]
        public void TheTailIsOppositeTheHead() {
            Vector2 head = FishAnatomyLandmarks.FallbackHeadUv(0f);
            Vector2 tail = FishAnatomyLandmarks.ResolveTailUv(null, 0f);

            Assert.Less(tail.x, head.x);
            Assert.Less(tail.x, 0.1f);
        }

        [Test]
        public void AUsableTailLandmarkIsFlippedToo() {
            Vector2 tail = FishAnatomyLandmarks.ResolveTailUv(BuildAnatomy(0.8f, 0.2f, 0.3f, 0.04f), 0f);

            Assert.AreEqual(0.93f, tail.x, Tolerance);
            Assert.AreEqual(1f - 0.46f, tail.y, Tolerance);
        }

        [Test]
        public void OrdinaryDrawingsDoNotGlow() {
            float common = FishRarityProfile.EvaluateGlow(0.1f, AnimationCurve.Linear(0f, 0f, 1f, 1f), 0.7f);

            Assert.Less(common, 0.1f, "If everything glowed, nothing would look rare.");
        }

        [Test]
        public void GlowRisesWithTheRarityScore() {
            AnimationCurve response = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            float ordinary = FishRarityProfile.EvaluateGlow(0.2f, response, 0.7f);
            float extraordinary = FishRarityProfile.EvaluateGlow(0.98f, response, 0.7f);

            Assert.Greater(extraordinary, ordinary);
            Assert.LessOrEqual(extraordinary, 0.7f, "Capped, so a legendary drawing is not a white blob.");
        }

        [Test]
        public void AMissingGlowCurveFallsBackToTheRawScore() {
            // OnValidate does not run in a player, so a curve that deserialised empty must not silently
            // turn every rare drawing ordinary.
            Assert.Greater(FishRarityProfile.EvaluateGlow(1f, null, 0.7f), 0.6f);
        }

        [Test]
        public void TrailsAreGatedOnTheTierRatherThanTheScore() {
            // Section 6 pre-buckets the tier so Unity does not hardcode the score thresholds.
            Assert.IsFalse(FishRarityProfile.HasTrail(FishRarityTier.Common, FishRarityTier.Rare));
            Assert.IsFalse(FishRarityProfile.HasTrail(FishRarityTier.Uncommon, FishRarityTier.Rare));
            Assert.IsTrue(FishRarityProfile.HasTrail(FishRarityTier.Rare, FishRarityTier.Rare));
            Assert.IsTrue(FishRarityProfile.HasTrail(FishRarityTier.Legendary, FishRarityTier.Rare));
        }

        [Test]
        public void ARarerDrawingIsAnnouncedMoreLoudly() {
            float common = FishRarityProfile.EvaluateBirthLoudness(FishRarityTier.Common, 2.2f);
            float legendary = FishRarityProfile.EvaluateBirthLoudness(FishRarityTier.Legendary, 2.2f);

            Assert.AreEqual(1f, common, Tolerance, "A common drawing just arrives.");
            Assert.AreEqual(2.2f, legendary, Tolerance);
        }

        [Test]
        public void BirthLoudnessNeverQuietensADrawing() {
            for (int tier = 0; tier <= 3; tier++) {
                Assert.GreaterOrEqual(FishRarityProfile.EvaluateBirthLoudness((FishRarityTier)tier, 0.1f), 1f,
                    "A misconfigured value must not make a birth quieter than normal.");
            }
        }

        [Test]
        public void TierNormalisationCoversTheFourSteps() {
            Assert.AreEqual(0f, FishRarityProfile.NormaliseTier(FishRarityTier.Common), Tolerance);
            Assert.AreEqual(1f, FishRarityProfile.NormaliseTier(FishRarityTier.Legendary), Tolerance);
            Assert.Greater(FishRarityProfile.NormaliseTier(FishRarityTier.Rare), FishRarityProfile.NormaliseTier(FishRarityTier.Uncommon));
        }

        [Test]
        public void ADrawingsDominantColourComesFromPythonRatherThanTheTexture() {
            // "No image analysis in Unity. Ever." Python sends dominant_colors; Unity only parses it.
            FishFeatures features = new FishFeatures();
            FishDominantColor strongest = new FishDominantColor();
            strongest.hex = "#2f6fb8";
            strongest.ratio = 0.44f;
            features.dominant_colors = new FishDominantColor[] { strongest };

            Color colour = FishTraitResolver.ResolveDominantColour(features);

            Assert.AreEqual(0x2f / 255f, colour.r, 0.005f);
            Assert.AreEqual(0x6f / 255f, colour.g, 0.005f);
            Assert.AreEqual(0xb8 / 255f, colour.b, 0.005f);
        }

        [Test]
        public void AMissingOrUnparseableColourFallsBackToWhite() {
            Assert.AreEqual(Color.white, FishTraitResolver.ResolveDominantColour(null));

            FishFeatures features = new FishFeatures();
            Assert.AreEqual(Color.white, FishTraitResolver.ResolveDominantColour(features));

            FishDominantColor broken = new FishDominantColor();
            broken.hex = "not a colour";
            features.dominant_colors = new FishDominantColor[] { broken };
            Assert.AreEqual(Color.white, FishTraitResolver.ResolveDominantColour(features));
        }

        [Test]
        public void HexColoursParseWithOrWithoutAHash() {
            Color withHash;
            Color withoutHash;

            Assert.IsTrue(FishTraitResolver.TryParseHexColour("#ffffff", out withHash));
            Assert.IsTrue(FishTraitResolver.TryParseHexColour("ffffff", out withoutHash));
            Assert.AreEqual(withHash, withoutHash);

            Color mixedCase;
            Assert.IsTrue(FishTraitResolver.TryParseHexColour("#AbCdEf", out mixedCase));
        }

        [Test]
        public void AMalformedHexIsRefusedRatherThanGuessed() {
            Color parsed;

            Assert.IsFalse(FishTraitResolver.TryParseHexColour(null, out parsed));
            Assert.IsFalse(FishTraitResolver.TryParseHexColour("#fff", out parsed), "Three digits is not the contract's format.");
            Assert.IsFalse(FishTraitResolver.TryParseHexColour("#gggggg", out parsed));
        }
    }
}
