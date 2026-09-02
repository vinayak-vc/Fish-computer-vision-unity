using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers size normalisation: different source resolutions, one consistent on-screen size, aspect never distorted. </summary>
    public sealed class FishSizeNormalizerTests {
        private const float PixelsPerUnit = 100f;
        private const float TargetLongestSide = 1.5f;
        private const float Tolerance = 0.0005f;

        [Test]
        public void DifferentSourceResolutionsNormaliseToTheSameLongestSide() {
            Vector2 small = FishSizeNormalizer.CalculateNormalisedWorldSize(300, 200, PixelsPerUnit, TargetLongestSide);
            Vector2 medium = FishSizeNormalizer.CalculateNormalisedWorldSize(1200, 600, PixelsPerUnit, TargetLongestSide);
            Vector2 large = FishSizeNormalizer.CalculateNormalisedWorldSize(2000, 1500, PixelsPerUnit, TargetLongestSide);

            Assert.AreEqual(TargetLongestSide, Mathf.Max(small.x, small.y), Tolerance);
            Assert.AreEqual(TargetLongestSide, Mathf.Max(medium.x, medium.y), Tolerance);
            Assert.AreEqual(TargetLongestSide, Mathf.Max(large.x, large.y), Tolerance);
        }

        [Test]
        public void AspectRatioIsPreserved() {
            Vector2 normalised = FishSizeNormalizer.CalculateNormalisedWorldSize(800, 400, PixelsPerUnit, TargetLongestSide);

            Assert.AreEqual(2f, normalised.x / normalised.y, Tolerance, "An 800x400 drawing must stay 2:1.");
        }

        [Test]
        public void TallImagesAreNormalisedByTheirHeight() {
            Vector2 normalised = FishSizeNormalizer.CalculateNormalisedWorldSize(200, 900, PixelsPerUnit, TargetLongestSide);

            Assert.AreEqual(TargetLongestSide, normalised.y, Tolerance);
            Assert.Less(normalised.x, normalised.y);
        }

        [Test]
        public void ScaleIsUniformSoTheDrawingIsNeverStretched() {
            float scale = FishSizeNormalizer.CalculateUniformScale(640, 160, PixelsPerUnit, TargetLongestSide);
            Vector2 sourceSize = FishSizeNormalizer.CalculateSpriteWorldSize(640, 160, PixelsPerUnit);
            Vector2 scaled = sourceSize * scale;

            Assert.AreEqual(sourceSize.x / sourceSize.y, scaled.x / scaled.y, Tolerance);
        }

        [Test]
        public void SpriteWorldSizeFollowsPixelsPerUnit() {
            Vector2 worldSize = FishSizeNormalizer.CalculateSpriteWorldSize(400, 200, 200f);

            Assert.AreEqual(2f, worldSize.x, Tolerance);
            Assert.AreEqual(1f, worldSize.y, Tolerance);
        }

        [Test]
        public void MaskAreaMapsOntoTheConfiguredSizeRange() {
            Assert.AreEqual(0f, FishSizeNormalizer.NormaliseForegroundArea(40000, 40000, 400000), Tolerance);
            Assert.AreEqual(1f, FishSizeNormalizer.NormaliseForegroundArea(400000, 40000, 400000), Tolerance);
            Assert.AreEqual(0.5f, FishSizeNormalizer.NormaliseForegroundArea(220000, 40000, 400000), Tolerance);
        }

        [Test]
        public void MaskAreasOutsideTheRangeAreClampedRatherThanExtrapolated() {
            Assert.AreEqual(0f, FishSizeNormalizer.NormaliseForegroundArea(500, 40000, 400000), Tolerance);
            Assert.AreEqual(1f, FishSizeNormalizer.NormaliseForegroundArea(9000000, 40000, 400000), Tolerance,
                "A drawing that fills the sheet must be the largest fish, not a fish the size of the tank.");
        }

        [Test]
        public void AMissingMaskAreaAsksTheCallerToFallBack() {
            // Contract section 9 replays legacy payloads, and this is the signal that means "no measurement
            // here, use the deterministic identity roll" rather than "make everything a middling size".
            Assert.Less(FishSizeNormalizer.NormaliseForegroundArea(0, 40000, 400000), 0f);
            Assert.Less(FishSizeNormalizer.NormaliseForegroundArea(-1, 40000, 400000), 0f);
        }

        [Test]
        public void AnInvertedOrDegenerateConfiguredRangeIsSurvivable() {
            Assert.AreEqual(0.5f, FishSizeNormalizer.NormaliseForegroundArea(220000, 400000, 40000), Tolerance,
                "An inspector edit that swaps the two bounds must not make every fish the same size.");
            Assert.Less(FishSizeNormalizer.NormaliseForegroundArea(220000, 40000, 40000), 0f);
        }

        [Test]
        public void TwoDrawingsOfTheSamePngSizeStillDifferInWorldSize() {
            // The capture station crops and pads to its own rules, so PNG dimensions are a poor size signal.
            // Sizing from the mask is what keeps a fish drawn small on the paper small in the tank.
            float small = FishSizeNormalizer.NormaliseForegroundArea(60000, 40000, 400000);
            float large = FishSizeNormalizer.NormaliseForegroundArea(380000, 40000, 400000);

            float smallSide = Mathf.Lerp(1.7f, 2.7f, small);
            float largeSide = Mathf.Lerp(1.7f, 2.7f, large);

            Assert.Greater(largeSide - smallSide, 0.5f, "The size difference has to be visible, not theoretical.");
        }
    }
}
