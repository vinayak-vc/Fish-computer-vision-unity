using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Both depth axes. The parallax one makes background fish smaller, fainter, slower and drawn behind.
    /// The preferred-depth one keeps a fish in the part of the water column its personality asks for.
    /// They are unrelated, and the tests below are written to fail loudly if they are ever conflated.
    /// </summary>
    public sealed class FishDepthProfileTests {
        private const float Tolerance = 0.0005f;

        /// <summary> A tank ten units tall, floor at -5 and surface at +5. </summary>
        private static readonly Rect Tank = new Rect(-10f, -5f, 20f, 10f);

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

        [Test]
        public void PreferredDepthZeroIsTheSurfaceAndOneIsTheFloor() {
            // Contract section 6 runs 0 at the surface to 1 at the floor, which is the inverse of a Unity
            // y axis. Getting this backwards puts every bottom-dweller at the top of the tank.
            Assert.AreEqual(Tank.yMax, FishDepthProfile.EvaluatePreferredHeight(0f, Tank), Tolerance);
            Assert.AreEqual(Tank.yMin, FishDepthProfile.EvaluatePreferredHeight(1f, Tank), Tolerance);
            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredHeight(0.5f, Tank), Tolerance);
        }

        [Test]
        public void AFishInsideItsBandFeelsNoPull() {
            float home = FishDepthProfile.EvaluatePreferredHeight(0.5f, Tank);

            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredDepthPull(home, 0.5f, Tank, 0.4f), Tolerance);
            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredDepthPull(home + 1.5f, 0.5f, Tank, 0.4f), Tolerance,
                "A band is a region to roam in, not a line to sit on.");
        }

        [Test]
        public void AFishAboveItsBandIsPulledDownAndBelowItIsPulledUp() {
            Assert.Less(FishDepthProfile.EvaluatePreferredDepthPull(4.5f, 0.5f, Tank, 0.3f), 0f, "Too high must pull down.");
            Assert.Greater(FishDepthProfile.EvaluatePreferredDepthPull(-4.5f, 0.5f, Tank, 0.3f), 0f, "Too low must pull up.");
        }

        [Test]
        public void ThePullGrowsWithDistanceFromTheBandAndThenSaturates() {
            float slightlyOut = Mathf.Abs(FishDepthProfile.EvaluatePreferredDepthPull(3f, 0.5f, Tank, 0.3f));
            float farOut = Mathf.Abs(FishDepthProfile.EvaluatePreferredDepthPull(4.9f, 0.5f, Tank, 0.3f));

            Assert.Greater(farOut, slightlyOut);
            Assert.LessOrEqual(farOut, 1f, "Saturating keeps a fish far from home swimming there rather than being yanked.");
        }

        [Test]
        public void ASurfaceFishAndAFloorFishArePulledInOppositeDirectionsFromTheSamePlace() {
            float surfaceDweller = FishDepthProfile.EvaluatePreferredDepthPull(0f, 0f, Tank, 0.3f);
            float floorDweller = FishDepthProfile.EvaluatePreferredDepthPull(0f, 1f, Tank, 0.3f);

            Assert.Greater(surfaceDweller, 0f, "preferred_depth 0 wants to be up.");
            Assert.Less(floorDweller, 0f, "preferred_depth 1 wants to be down.");
        }

        [Test]
        public void AFullHeightBandNeverPullsAtAll() {
            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredDepthPull(Tank.yMax, 0.5f, Tank, 1f), Tolerance);
            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredDepthPull(Tank.yMin, 0.5f, Tank, 1f), Tolerance);
        }

        [Test]
        public void TheBandSpansTheFullWidthButOnlyItsShareOfTheHeight() {
            Rect band = FishDepthProfile.EvaluatePreferredBand(Tank, 0.5f, 0.4f);

            Assert.AreEqual(Tank.xMin, band.xMin, Tolerance, "A fish is free to swim the whole length of the tank.");
            Assert.AreEqual(Tank.width, band.width, Tolerance);
            Assert.AreEqual(Tank.height * 0.4f, band.height, Tolerance);
        }

        [Test]
        public void TheBandIsCentredOnTheFishesPreferredHeight() {
            Rect band = FishDepthProfile.EvaluatePreferredBand(Tank, 0.5f, 0.4f);

            Assert.AreEqual(FishDepthProfile.EvaluatePreferredHeight(0.5f, Tank), band.center.y, Tolerance);
        }

        [Test]
        public void ASurfaceDwellersBandSitsAtTheTopAndAFloorDwellersAtTheBottom() {
            Rect surface = FishDepthProfile.EvaluatePreferredBand(Tank, 0f, 0.4f);
            Rect floor = FishDepthProfile.EvaluatePreferredBand(Tank, 1f, 0.4f);

            Assert.AreEqual(Tank.yMax, surface.yMax, Tolerance);
            Assert.AreEqual(Tank.yMin, floor.yMin, Tolerance);
            Assert.Less(floor.yMax, surface.yMin, "The two must not overlap, or preferred_depth reads as doing nothing.");
        }

        [Test]
        public void ABandAtTheEdgeIsSlidInsideRatherThanSquashed() {
            Rect surface = FishDepthProfile.EvaluatePreferredBand(Tank, 0f, 0.4f);
            Rect middle = FishDepthProfile.EvaluatePreferredBand(Tank, 0.5f, 0.4f);

            Assert.AreEqual(middle.height, surface.height, Tolerance,
                "A surface dweller must keep a full band to roam in, not a sliver against the glass.");
        }

        [Test]
        public void EveryBandStaysInsideTheAquarium() {
            for (int i = 0; i <= 20; i++) {
                Rect band = FishDepthProfile.EvaluatePreferredBand(Tank, i / 20f, 0.45f);

                Assert.GreaterOrEqual(band.yMin, Tank.yMin - Tolerance, "Band at depth " + (i / 20f));
                Assert.LessOrEqual(band.yMax, Tank.yMax + Tolerance, "Band at depth " + (i / 20f));
            }
        }

        [Test]
        public void AFullHeightBandIsTheWholeAquarium() {
            Rect band = FishDepthProfile.EvaluatePreferredBand(Tank, 0.2f, 1f);

            Assert.AreEqual(Tank.yMin, band.yMin, Tolerance);
            Assert.AreEqual(Tank.yMax, band.yMax, Tolerance);
        }

        [Test]
        public void ATargetChosenInsideTheBandNeverNeedsPullingBack() {
            // The two halves of the mechanism have to agree: a destination inside the band must not be a
            // place the homing force immediately drags the fish away from.
            Rect band = FishDepthProfile.EvaluatePreferredBand(Tank, 0.8f, 0.4f);

            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredDepthPull(band.yMin, 0.8f, Tank, 0.4f), Tolerance);
            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredDepthPull(band.yMax, 0.8f, Tank, 0.4f), Tolerance);
            Assert.AreEqual(0f, FishDepthProfile.EvaluatePreferredDepthPull(band.center.y, 0.8f, Tank, 0.4f), Tolerance);
        }
    }
}
