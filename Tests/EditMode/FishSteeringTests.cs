using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers the movement rules: turning, reaching targets, avoiding walls and neighbours, and never flipping upside down. </summary>
    public sealed class FishSteeringTests {
        private const float Tolerance = 0.001f;

        private static readonly Rect Area = new Rect(-10f, -5f, 20f, 10f);

        [Test]
        public void HeadingAndDirectionRoundTrip() {
            Assert.AreEqual(0f, FishSteering.HeadingDegrees(Vector2.right), Tolerance);
            Assert.AreEqual(90f, FishSteering.HeadingDegrees(Vector2.up), Tolerance);
            Assert.AreEqual(-90f, FishSteering.HeadingDegrees(Vector2.down), Tolerance);

            Vector2 direction = FishSteering.DirectionFromHeading(90f);
            Assert.AreEqual(0f, direction.x, Tolerance);
            Assert.AreEqual(1f, direction.y, Tolerance);
        }

        [Test]
        public void TurningIsCappedByTurnSpeed() {
            float stepped = FishSteering.StepHeading(0f, 90f, 30f, 1f);

            Assert.AreEqual(30f, stepped, Tolerance, "A fish must not snap to its new heading.");
        }

        [Test]
        public void TurningNeverOvershootsTheDesiredHeading() {
            float stepped = FishSteering.StepHeading(0f, 10f, 500f, 1f);

            Assert.AreEqual(10f, stepped, Tolerance);
        }

        [Test]
        public void TurningTakesTheShortestWayRound() {
            float stepped = FishSteering.StepHeading(170f, -170f, 100f, 0.1f);

            Assert.AreEqual(180f, Mathf.Abs(stepped), Tolerance, "Turning from 170 to -170 must cross 180, not swing back through zero.");
        }

        [Test]
        public void TargetsAreConsideredReachedWithinTheReachDistance() {
            Assert.IsTrue(FishSteering.HasReachedTarget(new Vector2(1f, 1f), new Vector2(1.2f, 1f), 0.5f));
            Assert.IsFalse(FishSteering.HasReachedTarget(new Vector2(1f, 1f), new Vector2(3f, 1f), 0.5f));
        }

        [Test]
        public void ChosenTargetsStayInsideTheAquarium() {
            for (int i = 0; i < 200; i++) {
                Vector2 current = new Vector2(Random.Range(Area.xMin, Area.xMax), Random.Range(Area.yMin, Area.yMax));
                Vector2 target = FishSteering.ChooseTarget(Area, current, 3f, Random.value, Random.value);

                Assert.GreaterOrEqual(target.x, Area.xMin - Tolerance);
                Assert.LessOrEqual(target.x, Area.xMax + Tolerance);
                Assert.GreaterOrEqual(target.y, Area.yMin - Tolerance);
                Assert.LessOrEqual(target.y, Area.yMax + Tolerance);
            }
        }

        [Test]
        public void ANearbyRandomTargetIsPushedOutToTheMinimumTravelDistance() {
            Vector2 current = Vector2.zero;
            Vector2 target = FishSteering.ChooseTarget(Area, current, 4f, 0.5f, 0.5f);

            Assert.GreaterOrEqual(Vector2.Distance(current, target), 4f - Tolerance, "A target on top of the fish must be pushed away.");
        }

        [Test]
        public void SeparationPushesAwayFromANeighbour() {
            Vector2 push = FishSteering.SeparationContribution(new Vector2(1f, 0f), new Vector2(0.5f, 0f), 1f);

            Assert.Greater(push.x, 0f, "A neighbour on the left must push the fish to the right.");
            Assert.AreEqual(0f, push.y, Tolerance);
        }

        [Test]
        public void SeparationIsZeroBeyondTheRadius() {
            Vector2 push = FishSteering.SeparationContribution(Vector2.zero, new Vector2(5f, 0f), 1f);

            Assert.AreEqual(Vector2.zero, push);
        }

        [Test]
        public void SeparationStrengthensAsFishGetCloser() {
            float near = FishSteering.SeparationContribution(Vector2.zero, new Vector2(0.2f, 0f), 1f).magnitude;
            float far = FishSteering.SeparationContribution(Vector2.zero, new Vector2(0.8f, 0f), 1f).magnitude;

            Assert.Greater(near, far);
        }

        [Test]
        public void BoundsAvoidancePushesBackTowardsTheCentre() {
            Vector2 nearLeft = FishSteering.BoundsAvoidance(new Vector2(Area.xMin + 0.2f, 0f), Area, 1f);
            Vector2 nearTop = FishSteering.BoundsAvoidance(new Vector2(0f, Area.yMax - 0.2f), Area, 1f);
            Vector2 middle = FishSteering.BoundsAvoidance(Vector2.zero, Area, 1f);

            Assert.Greater(nearLeft.x, 0f);
            Assert.Less(nearTop.y, 0f);
            Assert.AreEqual(Vector2.zero, middle);
        }

        [Test]
        public void LeftFacingArtworkIsMirroredOnlyWhenSwimmingRight() {
            Assert.IsTrue(FishSteering.ShouldFlipHorizontally(true, false), "A left-facing drawing must be mirrored to swim right.");
            Assert.IsFalse(FishSteering.ShouldFlipHorizontally(false, false), "A left-facing drawing already faces left, so swimming left needs no mirror.");
        }

        [Test]
        public void RightFacingArtworkIsMirroredOnlyWhenSwimmingLeft() {
            Assert.IsFalse(FishSteering.ShouldFlipHorizontally(true, true));
            Assert.IsTrue(FishSteering.ShouldFlipHorizontally(false, true));
        }

        [Test]
        public void TheSpriteNoseAlwaysEndsUpPointingTheWayTheFishSwims() {
            // visualFacesPositiveX, the value VisualAngle needs, is the direction of travel under either
            // artwork orientation. This is the invariant that breaks when the artwork flag is wrong.
            bool[] artworkFacesRightCases = { true, false };

            for (int c = 0; c < artworkFacesRightCases.Length; c++) {
                bool artworkFacesRight = artworkFacesRightCases[c];

                for (int m = 0; m < 2; m++) {
                    bool movingRight = m == 0;
                    bool flipped = FishSteering.ShouldFlipHorizontally(movingRight, artworkFacesRight);
                    bool noseAlongPositiveX = artworkFacesRight ? !flipped : flipped;

                    Assert.AreEqual(movingRight, noseAlongPositiveX, "artworkFacesRight=" + artworkFacesRight + " movingRight=" + movingRight);
                }
            }
        }

        [Test]
        public void TheFishIsNeverRotatedUpsideDown() {
            for (int heading = -180; heading <= 180; heading++) {
                float facingRight = FishSteering.VisualAngle(heading, true, 32f);
                float facingLeft = FishSteering.VisualAngle(heading, false, 32f);

                Assert.LessOrEqual(Mathf.Abs(facingRight), 32f + Tolerance);
                Assert.LessOrEqual(Mathf.Abs(facingLeft), 32f + Tolerance);
            }
        }

        [Test]
        public void TiltIsMirroredWhenTheSpriteIsFlipped() {
            float facingRight = FishSteering.VisualAngle(30f, true, 45f);
            float facingLeft = FishSteering.VisualAngle(150f, false, 45f);

            Assert.AreEqual(30f, facingRight, Tolerance);
            Assert.AreEqual(-30f, facingLeft, Tolerance, "A fish swimming up and to the left must still point its nose up.");
        }

        [Test]
        public void AFishSteeringTowardsATargetEventuallyReachesIt() {
            Vector2 position = new Vector2(-8f, -4f);
            Vector2 target = new Vector2(7f, 3f);
            float heading = 180f;
            float speed = 2f;
            float deltaTime = 1f / 60f;

            for (int step = 0; step < 1200; step++) {
                Vector2 desired = (target - position).normalized;
                heading = FishSteering.StepHeading(heading, FishSteering.HeadingDegrees(desired), 90f, deltaTime);
                position += FishSteering.DirectionFromHeading(heading) * speed * deltaTime;
                position = FishSteering.ClampInside(position, Area);

                if (FishSteering.HasReachedTarget(position, target, 0.45f)) {
                    Assert.Pass();
                }
            }

            Assert.Fail("The fish should have reached its target within 20 simulated seconds.");
        }

        [Test]
        public void ClampInsideKeepsAPositionWithinTheArea() {
            Vector2 clamped = FishSteering.ClampInside(new Vector2(50f, -50f), Area);

            Assert.AreEqual(Area.xMax, clamped.x, Tolerance);
            Assert.AreEqual(Area.yMin, clamped.y, Tolerance);
        }
    }
}
