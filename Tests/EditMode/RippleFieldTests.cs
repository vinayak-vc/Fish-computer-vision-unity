using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Click shockwaves: the force law, the decay, the danger zone, and what happens under a rapid clicker. </summary>
    public sealed class RippleFieldTests {
        private static RippleField BuildField() {
            return new RippleField(4, 2f, 8f, 6f, 2f);
        }

        [Test]
        public void AFieldWithNoRipplesExertsNoForce() {
            RippleField field = BuildField();

            Assert.AreEqual(0, field.ActiveCount);
            Assert.AreEqual(Vector2.zero, field.Evaluate(new Vector2(1f, 1f)));
        }

        [Test]
        public void ARipplePushesOutwardsFromItsOrigin() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            Vector2 toTheRight = field.Evaluate(new Vector2(2f, 0f));
            Vector2 above = field.Evaluate(new Vector2(0f, 2f));

            Assert.Greater(toTheRight.x, 0f, "A fish to the right of a click must be pushed further right.");
            Assert.Greater(above.y, 0f);
        }

        [Test]
        public void ForceFallsOffWithDistance() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            float near = field.Evaluate(new Vector2(1f, 0f)).magnitude;
            float far = field.Evaluate(new Vector2(4f, 0f)).magnitude;

            Assert.Greater(near, far, "Contract of the effect: F = strength / distance.");
        }

        [Test]
        public void ForceIsZeroBeyondTheRippleRadius() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            Assert.AreEqual(Vector2.zero, field.Evaluate(new Vector2(20f, 0f)));
        }

        [Test]
        public void AFishExactlyOnTheClickPointGetsAFiniteShove() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            Vector2 force = field.Evaluate(Vector2.zero);

            Assert.IsFalse(float.IsNaN(force.x) || float.IsInfinity(force.x), "Dividing by zero distance must not reach the transform.");
            Assert.Greater(force.magnitude, 0f);
        }

        [Test]
        public void ARippleWeakensAsItAges() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            float fresh = field.Evaluate(new Vector2(2f, 0f)).magnitude;
            field.Tick(1f);
            float aged = field.Evaluate(new Vector2(2f, 0f)).magnitude;

            Assert.Less(aged, fresh);
        }

        [Test]
        public void ARippleRetiresOnceItsLifetimeIsUp() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            field.Tick(2.5f);

            Assert.AreEqual(0, field.ActiveCount);
            Assert.AreEqual(Vector2.zero, field.Evaluate(new Vector2(2f, 0f)));
        }

        [Test]
        public void RipplesAccumulate() {
            RippleField field = BuildField();
            field.Emit(new Vector2(-1f, 0f));
            field.Emit(new Vector2(-2f, 0f));

            Assert.AreEqual(2, field.ActiveCount);
            Assert.Greater(field.Evaluate(Vector2.zero).x, 0f, "Two clicks to the left must push a fish right harder than one.");
        }

        [Test]
        public void ARapidClickerCannotGrowTheFieldPastItsCapacity() {
            RippleField field = BuildField();

            for (int i = 0; i < 50; i++) {
                field.Emit(new Vector2(i, 0f));
            }

            Assert.AreEqual(4, field.ActiveCount, "Capacity is fixed so a visitor hammering the mouse cannot allocate unboundedly.");
        }

        [Test]
        public void TheOldestRippleIsTheOneReplacedWhenTheFieldIsFull() {
            RippleField field = BuildField();

            field.Emit(Vector2.zero);
            field.Tick(1.5f);

            for (int i = 0; i < 4; i++) {
                field.Emit(new Vector2(10f + i, 0f));
            }

            // The first ripple was the most decayed, so it is the one that should have been recycled.
            Assert.AreEqual(Vector2.zero, field.Evaluate(new Vector2(1f, 0f)));
        }

        [Test]
        public void TheDangerZoneCoversThePointClickedAndNotTheWholeRipple() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            Assert.IsTrue(field.IsInDangerZone(new Vector2(1f, 0f)));
            Assert.IsFalse(field.IsInDangerZone(new Vector2(5f, 0f)), "The danger zone is smaller than the ripple's reach.");
        }

        [Test]
        public void TheDangerZoneExpiresWithItsRipple() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);

            Assert.IsTrue(field.IsInDangerZone(Vector2.zero));
            field.Tick(2.5f);

            Assert.IsFalse(field.IsInDangerZone(Vector2.zero), "A fish must not route around a click forever.");
        }

        [Test]
        public void ClearingRemovesEveryRipple() {
            RippleField field = BuildField();
            field.Emit(Vector2.zero);
            field.Emit(new Vector2(1f, 1f));

            field.Clear();

            Assert.AreEqual(0, field.ActiveCount);
            Assert.AreEqual(Vector2.zero, field.Evaluate(Vector2.zero));
        }
    }
}
