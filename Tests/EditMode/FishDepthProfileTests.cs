using NUnit.Framework;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers the depth mapping that makes background fish smaller, fainter, slower and drawn behind. </summary>
    public sealed class FishDepthProfileTests {
        private const float Tolerance = 0.0005f;

        [Test]
        public void ForegroundFishAreUnaffectedByDepth() {
            Assert.AreEqual(1f, FishDepthProfile.EvaluateScaleMultiplier(0f, 0.4f), Tolerance);
            Assert.AreEqual(1f, FishDepthProfile.EvaluateAlpha(0f, 0.25f), Tolerance);
            Assert.AreEqual(1f, FishDepthProfile.EvaluateSpeedMultiplier(0f, 0.3f), Tolerance);
        }

        [Test]
        public void BackgroundFishShrinkFadeAndSlowByTheConfiguredInfluence() {
            Assert.AreEqual(0.6f, FishDepthProfile.EvaluateScaleMultiplier(1f, 0.4f), Tolerance);
            Assert.AreEqual(0.75f, FishDepthProfile.EvaluateAlpha(1f, 0.25f), Tolerance);
            Assert.AreEqual(0.7f, FishDepthProfile.EvaluateSpeedMultiplier(1f, 0.3f), Tolerance);
        }

        [Test]
        public void SortingOrderRunsFromForegroundToBackground() {
            Assert.AreEqual(200, FishDepthProfile.EvaluateSortingOrder(0f, 200, 20));
            Assert.AreEqual(20, FishDepthProfile.EvaluateSortingOrder(1f, 200, 20));
            Assert.AreEqual(110, FishDepthProfile.EvaluateSortingOrder(0.5f, 200, 20));
        }

        [Test]
        public void DepthValuesOutsideZeroToOneAreClamped() {
            Assert.AreEqual(1f, FishDepthProfile.EvaluateScaleMultiplier(-3f, 0.4f), Tolerance);
            Assert.AreEqual(0.6f, FishDepthProfile.EvaluateScaleMultiplier(9f, 0.4f), Tolerance);
        }
    }
}
