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
    }
}
