using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Alignment, cohesion and the pull of a new arrival.
    ///
    /// Every one of these returns a direction, never a magnitude, and that is the property worth guarding:
    /// if cohesion returned the raw offset to the centre of the school, a fish with forty neighbours would
    /// be yanked forty times harder than one with two, and dense schools would collapse into a point.
    /// </summary>
    public sealed class FishFlockingTests {
        private const float Tolerance = 0.0005f;

        [Test]
        public void AlignmentIsZeroWithoutNeighbours() {
            Assert.AreEqual(Vector2.zero, FishFlocking.AlignmentDirection(Vector2.zero, 0));
            Assert.AreEqual(Vector2.zero, FishFlocking.AlignmentDirection(new Vector2(3f, 0f), 0));
        }

        [Test]
        public void AlignmentPointsAlongTheAverageHeadingOfTheSchool() {
            Vector2 alignment = FishFlocking.AlignmentDirection(Vector2.right * 4f, 4);

            Assert.AreEqual(1f, alignment.x, Tolerance);
            Assert.AreEqual(0f, alignment.y, Tolerance);
        }

        [Test]
        public void AlignmentIsAlwaysUnitLengthHoweverManyNeighbours() {
            Vector2 few = FishFlocking.AlignmentDirection(new Vector2(2f, 2f), 2);
            Vector2 many = FishFlocking.AlignmentDirection(new Vector2(40f, 40f), 40);

            Assert.AreEqual(1f, few.magnitude, Tolerance);
            Assert.AreEqual(1f, many.magnitude, Tolerance);
            Assert.AreEqual(few.x, many.x, Tolerance, "A bigger school must not pull harder, only in the same direction.");
        }

        [Test]
        public void AlignmentIsZeroWhenTheSchoolCancelsItselfOut() {
            // Two fish swimming at each other average to nothing. There is no heading to match, and
            // normalising a near-zero vector would hand back amplified floating-point noise.
            Assert.AreEqual(Vector2.zero, FishFlocking.AlignmentDirection(Vector2.zero, 2));
        }

        [Test]
        public void CohesionIsZeroWithoutNeighbours() {
            Assert.AreEqual(Vector2.zero, FishFlocking.CohesionDirection(Vector2.zero, 0, Vector2.zero));
        }

        [Test]
        public void CohesionPointsAtTheCentreOfTheSchool() {
            // Two neighbours at x = 4 and x = 6 put the centre at x = 5, which is to the right of a fish at 0.
            Vector2 cohesion = FishFlocking.CohesionDirection(new Vector2(10f, 0f), 2, Vector2.zero);

            Assert.AreEqual(1f, cohesion.x, Tolerance);
            Assert.AreEqual(0f, cohesion.y, Tolerance);
        }

        [Test]
        public void CohesionIsAlwaysUnitLengthHoweverFarTheSchoolIs() {
            Vector2 near = FishFlocking.CohesionDirection(new Vector2(1f, 0f), 1, Vector2.zero);
            Vector2 far = FishFlocking.CohesionDirection(new Vector2(90f, 0f), 1, Vector2.zero);

            Assert.AreEqual(1f, near.magnitude, Tolerance);
            Assert.AreEqual(1f, far.magnitude, Tolerance, "Returning the raw offset would collapse dense schools into a point.");
        }

        [Test]
        public void CohesionIsZeroForAFishAlreadyAtTheCentre() {
            Assert.AreEqual(Vector2.zero, FishFlocking.CohesionDirection(new Vector2(6f, 4f), 2, new Vector2(3f, 2f)));
        }

        [Test]
        public void ACuriousFishIsDrawnTowardsANewArrival() {
            Vector2 pull = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 1f, 1f, 5f);

            Assert.Greater(pull.x, 0f);
            Assert.AreEqual(0f, pull.y, Tolerance);
        }

        [Test]
        public void AnIncuriousFishIgnoresANewArrival() {
            Assert.AreEqual(Vector2.zero, FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 1f, 0f, 5f),
                "The ones that do come over should read as interested, not as the whole tank lurching.");
        }

        [Test]
        public void AFishThatIsNoLongerNewDrawsNobody() {
            Assert.AreEqual(Vector2.zero, FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 0f, 1f, 5f));
        }

        [Test]
        public void InterestFadesAsTheArrivalGetsOlder() {
            float fresh = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 1f, 1f, 5f).magnitude;
            float stale = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 0.25f, 1f, 5f).magnitude;

            Assert.Greater(fresh, stale);
        }

        [Test]
        public void InterestFadesWithDistanceAndStopsAtTheRadius() {
            float near = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(1f, 0f), 1f, 1f, 5f).magnitude;
            float far = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(4f, 0f), 1f, 1f, 5f).magnitude;

            Assert.Greater(near, far);
            Assert.AreEqual(Vector2.zero, FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(9f, 0f), 1f, 1f, 5f));
        }

        [Test]
        public void AMoreCuriousFishInvestigatesHarder() {
            float keen = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 1f, 1f, 5f).magnitude;
            float mild = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 1f, 0.3f, 5f).magnitude;

            Assert.Greater(keen, mild);
        }

        [Test]
        public void AFishSittingOnTheArrivalIsNotGivenAnInfiniteShove() {
            Vector2 pull = FishFlocking.NoveltyAttraction(Vector2.zero, Vector2.zero, 1f, 1f, 5f);

            Assert.AreEqual(Vector2.zero, pull);
            Assert.IsFalse(float.IsNaN(pull.x));
        }

        [Test]
        public void OutOfRangeTraitValuesAreClamped() {
            Vector2 clamped = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 9f, 9f, 5f);
            Vector2 maximum = FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), 1f, 1f, 5f);

            Assert.AreEqual(maximum.x, clamped.x, Tolerance);
            Assert.AreEqual(Vector2.zero, FishFlocking.NoveltyAttraction(Vector2.zero, new Vector2(2f, 0f), -1f, 1f, 5f));
        }
    }
}
