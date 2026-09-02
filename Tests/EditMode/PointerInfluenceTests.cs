using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The interaction model, as arithmetic.
    ///
    /// "A first-time visitor works out cause and effect within about five seconds" is not something a test
    /// can check. What a test can check is that a curious fish moves towards the pointer, a fearful one
    /// moves away, and neither direction quietly inverts - which is what would actually break, and which
    /// would be maddening to diagnose from the tank.
    /// </summary>
    public sealed class PointerInfluenceTests {
        private const float Tolerance = 0.0005f;

        private static readonly Vector2 Fish = Vector2.zero;
        private static readonly Vector2 PointerToTheRight = new Vector2(3f, 0f);

        [Test]
        public void ProximityIsStrongestAtThePointerAndZeroAtTheRadius() {
            Assert.AreEqual(1f, PointerInfluence.ProximityFalloff(0f, 5f), Tolerance);
            Assert.AreEqual(0.5f, PointerInfluence.ProximityFalloff(2.5f, 5f), Tolerance);
            Assert.AreEqual(0f, PointerInfluence.ProximityFalloff(5f, 5f), Tolerance);
            Assert.AreEqual(0f, PointerInfluence.ProximityFalloff(50f, 5f), Tolerance);
        }

        [Test]
        public void ACuriousFishSwimsTowardsThePointer() {
            Vector2 force = PointerInfluence.EvaluateSteering(Fish, PointerToTheRight, 1f, 0f, 0f, 6f, 2f, 3f, 0.5f);

            Assert.Greater(force.x, 0f, "Curiosity must pull the fish towards the pointer, not away from it.");
        }

        [Test]
        public void AFearfulFishFleesThePointer() {
            Vector2 force = PointerInfluence.EvaluateSteering(Fish, PointerToTheRight, 0f, 1f, 0f, 6f, 2f, 3f, 0.5f);

            Assert.Less(force.x, 0f, "Fear must push the fish away from the pointer.");
        }

        [Test]
        public void AFishThatIsBothCuriousAndFearfulStillResolvesToFlight() {
            // Contract section 6: curiosity and fear are not complements, and repulsion is deliberately the
            // stronger of the two so a nervous fish approaches and then bolts rather than freezing.
            Vector2 force = PointerInfluence.EvaluateSteering(Fish, PointerToTheRight, 1f, 1f, 0f, 6f, 2f, 3f, 0.5f);

            Assert.Less(force.x, 0f);
        }

        [Test]
        public void APointerBeyondTheInfluenceRadiusIsIgnored() {
            Vector2 force = PointerInfluence.EvaluateSteering(Fish, new Vector2(40f, 0f), 1f, 1f, 0f, 6f, 2f, 3f, 0.5f);

            Assert.AreEqual(Vector2.zero, force, "A fish across the tank must not be steering around the cursor.");
        }

        [Test]
        public void ACuriousFishStillKeepsItsPersonalSpace() {
            Vector2 veryClose = new Vector2(0.2f, 0f);
            Vector2 force = PointerInfluence.EvaluateSteering(Fish, veryClose, 1f, 0f, 0f, 6f, 2f, 3f, 1f);

            Assert.Less(force.x, 0f, "A fish parked motionless under the cursor reads as broken, not as interested.");
        }

        [Test]
        public void ComfortRisesSmoothlyAndReachesHalfAtTheTrustThreshold() {
            Assert.AreEqual(0f, PointerInfluence.Comfort(0f, 0.5f), Tolerance);
            Assert.AreEqual(0.25f, PointerInfluence.Comfort(0.25f, 0.5f), Tolerance);
            Assert.AreEqual(0.5f, PointerInfluence.Comfort(0.5f, 0.5f), Tolerance);
            Assert.AreEqual(0.75f, PointerInfluence.Comfort(0.75f, 0.5f), Tolerance);
            Assert.AreEqual(1f, PointerInfluence.Comfort(1f, 0.5f), Tolerance);
        }

        [Test]
        public void ComfortIsContinuousAcrossTheTrustThreshold() {
            float justBelow = PointerInfluence.Comfort(0.549f, 0.55f);
            float justAbove = PointerInfluence.Comfort(0.551f, 0.55f);

            Assert.Less(Mathf.Abs(justAbove - justBelow), 0.02f, "A jump here would read as a fish changing its mind for no reason.");
        }

        [Test]
        public void FullTrustInvertsAFearfulFishIntoAnApproach() {
            Vector2 afraid = PointerInfluence.EvaluateSteering(Fish, PointerToTheRight, 0f, 1f, 0f, 6f, 2f, 3f, 0.5f);
            Vector2 trusting = PointerInfluence.EvaluateSteering(Fish, PointerToTheRight, 0f, 1f, 1f, 6f, 2f, 3f, 0.5f);

            Assert.Less(afraid.x, 0f);
            Assert.Greater(trusting.x, 0f, "High affection must invert the flee response into an approach.");
        }

        [Test]
        public void PartialTrustOnlySoftensFearRatherThanInvertingIt() {
            Vector2 afraid = PointerInfluence.EvaluateSteering(Fish, PointerToTheRight, 0f, 1f, 0f, 6f, 2f, 3f, 0.5f);
            Vector2 warming = PointerInfluence.EvaluateSteering(Fish, PointerToTheRight, 0f, 1f, 0.4f, 6f, 2f, 3f, 0.5f);

            Assert.Less(warming.x, 0f, "Below the trust threshold the fish still flees.");
            Assert.Greater(warming.x, afraid.x, "But it flees less hard than it did.");
        }

        [Test]
        public void AFearfulFishStartlesMoreEasilyThanAFearlessOne() {
            float nervous = PointerInfluence.StartleThreshold(1f, 4f, 3f);
            float bold = PointerInfluence.StartleThreshold(0f, 4f, 3f);

            Assert.AreEqual(4f, nervous, Tolerance);
            Assert.AreEqual(12f, bold, Tolerance, "A fearless fish must need the full multiplier before it spooks.");
            Assert.Less(nervous, bold);
        }

        [Test]
        public void TheSameFlickScattersTheNervousAndIsIgnoredByTheBold() {
            Assert.IsTrue(PointerInfluence.ShouldStartle(6f, 2f, 6f, 1f, 4f, 3f));
            Assert.IsFalse(PointerInfluence.ShouldStartle(6f, 2f, 6f, 0f, 4f, 3f),
                "That contrast is the whole tell that personality is real.");
        }

        [Test]
        public void AFishOutsideTheInfluenceRadiusIsNeverStartled() {
            Assert.IsFalse(PointerInfluence.ShouldStartle(100f, 40f, 6f, 1f, 4f, 3f));
        }

        [Test]
        public void LingeringCloseAndSlowEarnsAffection() {
            float delta = PointerInfluence.AffectionDelta(0.5f, 0.2f, true, 3f, 1.2f, 0.2f, 0.02f, 1f);

            Assert.Greater(delta, 0f);
        }

        [Test]
        public void AFastPointerEarnsNoAffectionEvenWhenClose() {
            float delta = PointerInfluence.AffectionDelta(0.5f, 9f, true, 3f, 1.2f, 0.2f, 0.02f, 1f);

            Assert.Less(delta, 0f, "Company means slow and near. Fast and near is a threat.");
        }

        [Test]
        public void AffectionDecaysWhileThePointerIsAway() {
            float absent = PointerInfluence.AffectionDelta(float.MaxValue, 0f, false, 3f, 1.2f, 0.2f, 0.02f, 1f);
            float distant = PointerInfluence.AffectionDelta(20f, 0.1f, true, 3f, 1.2f, 0.2f, 0.02f, 1f);

            Assert.AreEqual(-0.02f, absent, Tolerance);
            Assert.AreEqual(-0.02f, distant, Tolerance);
        }

        [Test]
        public void AffectionIsEarnedFasterTheCloserAndSlowerThePointerIs() {
            float veryClose = PointerInfluence.AffectionDelta(0.1f, 0.05f, true, 3f, 1.2f, 0.2f, 0.02f, 1f);
            float barelyInRange = PointerInfluence.AffectionDelta(2.9f, 1.15f, true, 3f, 1.2f, 0.2f, 0.02f, 1f);

            Assert.Greater(veryClose, barelyInRange, "Deliberately holding still next to a fish should be rewarded.");
        }
    }
}
