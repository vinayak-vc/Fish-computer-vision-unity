using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Food: sinking, going stale, and above all who gets it.
    ///
    /// The competition rule is the part of design point 2 that can fail invisibly. If claims resolved in
    /// tick order rather than by aggression, feeding would still look fine - a crowd would still gather
    /// and flakes would still vanish - but the trait that is supposed to decide the outcome would be
    /// doing nothing at all, and nobody watching the tank could tell.
    /// </summary>
    public sealed class FoodFieldTests {
        private const float Tolerance = 0.0005f;
        private const float FloorY = -5f;

        private const int TimidFish = 11;
        private const int AggressiveFish = 22;

        private static FoodField BuildField() {
            return new FoodField(4, 1f, 10f);
        }

        [Test]
        public void AnEmptyFieldHoldsNothing() {
            FoodField food = BuildField();

            Assert.AreEqual(0, food.ActiveCount);
            Assert.AreEqual(4, food.Capacity);

            Vector2 position;
            float distance;
            Assert.AreEqual(-1, food.FindNearest(Vector2.zero, 10f, out position, out distance));
        }

        [Test]
        public void DroppedFoodIsFindable() {
            FoodField food = BuildField();
            food.Emit(new Vector2(2f, 3f));

            Vector2 position;
            float distance;
            int index = food.FindNearest(new Vector2(2f, 1f), 5f, out position, out distance);

            Assert.AreEqual(1, food.ActiveCount);
            Assert.GreaterOrEqual(index, 0);
            Assert.AreEqual(2f, position.x, Tolerance);
            Assert.AreEqual(3f, position.y, Tolerance);
            Assert.AreEqual(2f, distance, Tolerance);
        }

        [Test]
        public void FoodBeyondTheSearchRadiusIsNotFound() {
            FoodField food = BuildField();
            food.Emit(new Vector2(40f, 0f));

            Vector2 position;
            float distance;
            Assert.AreEqual(-1, food.FindNearest(Vector2.zero, 5f, out position, out distance));
        }

        [Test]
        public void TheNearestFlakeIsTheOneReturned() {
            FoodField food = BuildField();
            food.Emit(new Vector2(4f, 0f));
            food.Emit(new Vector2(1f, 0f));
            food.Emit(new Vector2(7f, 0f));

            Vector2 position;
            float distance;
            food.FindNearest(Vector2.zero, 10f, out position, out distance);

            Assert.AreEqual(1f, position.x, Tolerance);
            Assert.AreEqual(1f, distance, Tolerance);
        }

        [Test]
        public void FoodSinks() {
            FoodField food = BuildField();
            food.Emit(new Vector2(0f, 4f));

            food.Tick(1f, FloorY);

            Vector2 position;
            float age;
            Assert.IsTrue(food.TryGetParticle(0, out position, out age));
            Assert.AreEqual(3f, position.y, Tolerance, "A sink speed of 1 over one second is one unit down.");
        }

        [Test]
        public void FoodComesToRestOnTheFloorRatherThanFallingThroughIt() {
            FoodField food = BuildField();
            food.Emit(new Vector2(0f, FloorY + 0.5f));

            food.Tick(5f, FloorY);

            Vector2 position;
            float age;
            Assert.IsTrue(food.TryGetParticle(0, out position, out age));
            Assert.AreEqual(FloorY, position.y, Tolerance, "Food a fish can no longer reach may as well not exist.");
        }

        [Test]
        public void FoodGoesStaleAndVanishes() {
            FoodField food = BuildField();
            food.Emit(Vector2.zero);

            food.Tick(11f, FloorY);

            Assert.AreEqual(0, food.ActiveCount);
        }

        [Test]
        public void FeedingRepeatedlyCannotGrowTheFieldPastItsCapacity() {
            FoodField food = BuildField();

            for (int i = 0; i < 40; i++) {
                food.Emit(new Vector2(i, 0f));
            }

            Assert.AreEqual(4, food.ActiveCount, "A visitor hammering the mouse must not allocate unboundedly.");
        }

        [Test]
        public void TheStalestFlakeIsTheOneRecycledWhenTheFieldIsFull() {
            FoodField food = BuildField();

            food.Emit(new Vector2(-8f, 0f));
            food.Tick(3f, FloorY);

            for (int i = 0; i < 4; i++) {
                food.Emit(new Vector2(20f + i, 0f));
            }

            Vector2 position;
            float distance;
            Assert.AreEqual(-1, food.FindNearest(new Vector2(-8f, -3f), 1f, out position, out distance),
                "The oldest flake should have been the one dropped to make room.");
        }

        [Test]
        public void AClaimedFlakeIsEatenAndTheEaterIsReported() {
            FoodField food = BuildField();
            food.Emit(Vector2.zero);
            food.SubmitClaim(0, 0.5f, TimidFish);

            List<int> winners = new List<int>();
            int eaten = food.ResolveClaims(winners);

            Assert.AreEqual(1, eaten);
            Assert.AreEqual(TimidFish, winners[0]);
            Assert.AreEqual(0, food.ActiveCount, "An eaten flake is gone.");
        }

        [Test]
        public void TheMostAggressiveClaimantWinsWhateverOrderTheClaimsArriveIn() {
            // The whole point of resolving after every fish has ticked. Both orderings must give the
            // aggressive fish the food, or tick order would be deciding it instead of the trait.
            FoodField timidFirst = BuildField();
            timidFirst.Emit(Vector2.zero);
            timidFirst.SubmitClaim(0, 0.1f, TimidFish);
            timidFirst.SubmitClaim(0, 0.9f, AggressiveFish);

            FoodField aggressiveFirst = BuildField();
            aggressiveFirst.Emit(Vector2.zero);
            aggressiveFirst.SubmitClaim(0, 0.9f, AggressiveFish);
            aggressiveFirst.SubmitClaim(0, 0.1f, TimidFish);

            List<int> winners = new List<int>();

            timidFirst.ResolveClaims(winners);
            Assert.AreEqual(AggressiveFish, winners[0], "Claimed timid-first, the aggressive fish must still win.");

            aggressiveFirst.ResolveClaims(winners);
            Assert.AreEqual(AggressiveFish, winners[0], "Claimed aggressive-first, likewise.");
        }

        [Test]
        public void OnlyOneFishGetsAContestedFlake() {
            FoodField food = BuildField();
            food.Emit(Vector2.zero);

            for (int i = 0; i < 6; i++) {
                food.SubmitClaim(0, 0.1f * i, 100 + i);
            }

            List<int> winners = new List<int>();
            int eaten = food.ResolveClaims(winners);

            Assert.AreEqual(1, eaten, "Six fish at one flake is one meal, not six.");
            Assert.AreEqual(105, winners[0]);
        }

        [Test]
        public void AnExactTieGoesToWhicheverFishClaimedFirst() {
            // Tick order decides only in the case where the deciding trait cannot, which is the least
            // arbitrary place to put the arbitrariness.
            FoodField food = BuildField();
            food.Emit(Vector2.zero);
            food.SubmitClaim(0, 0.5f, TimidFish);
            food.SubmitClaim(0, 0.5f, AggressiveFish);

            List<int> winners = new List<int>();
            food.ResolveClaims(winners);

            Assert.AreEqual(TimidFish, winners[0]);
        }

        [Test]
        public void SeveralFlakesCanBeEatenInOneFrame() {
            FoodField food = BuildField();
            food.Emit(new Vector2(0f, 0f));
            food.Emit(new Vector2(9f, 0f));

            food.SubmitClaim(0, 0.5f, TimidFish);
            food.SubmitClaim(1, 0.5f, AggressiveFish);

            List<int> winners = new List<int>();

            Assert.AreEqual(2, food.ResolveClaims(winners));
            Assert.Contains(TimidFish, winners);
            Assert.Contains(AggressiveFish, winners);
        }

        [Test]
        public void AnUnclaimedFlakeSurvivesResolution() {
            FoodField food = BuildField();
            food.Emit(Vector2.zero);

            List<int> winners = new List<int>();

            Assert.AreEqual(0, food.ResolveClaims(winners));
            Assert.AreEqual(1, food.ActiveCount, "Food nobody reached must still be there next frame.");
        }

        [Test]
        public void ClaimsDoNotCarryOverBetweenFrames() {
            // Claims are per frame. A fish that drifted away must not still be eating on its behalf.
            FoodField food = BuildField();
            food.Emit(Vector2.zero);
            food.Emit(new Vector2(9f, 0f));
            food.SubmitClaim(0, 0.5f, TimidFish);

            List<int> winners = new List<int>();
            food.ResolveClaims(winners);
            int eatenOnTheSecondFrame = food.ResolveClaims(winners);

            Assert.AreEqual(0, eatenOnTheSecondFrame);
            Assert.AreEqual(1, food.ActiveCount);
        }

        [Test]
        public void ClaimsOnNothingAreIgnored() {
            FoodField food = BuildField();
            List<int> winners = new List<int>();

            food.SubmitClaim(0, 1f, AggressiveFish);
            food.SubmitClaim(-1, 1f, AggressiveFish);
            food.SubmitClaim(99, 1f, AggressiveFish);

            Assert.AreEqual(0, food.ResolveClaims(winners), "Claiming an empty or out-of-range slot must not throw or invent a meal.");
        }

        [Test]
        public void AFishWithNoIdCannotClaim() {
            // Zero is the sentinel for "unclaimed"; real fish ids start at one.
            FoodField food = BuildField();
            food.Emit(Vector2.zero);
            food.SubmitClaim(0, 1f, 0);

            List<int> winners = new List<int>();

            Assert.AreEqual(0, food.ResolveClaims(winners));
            Assert.AreEqual(1, food.ActiveCount);
        }

        [Test]
        public void ClearingRemovesEveryFlakeAndEveryClaim() {
            FoodField food = BuildField();
            food.Emit(Vector2.zero);
            food.SubmitClaim(0, 1f, AggressiveFish);
            food.Clear();

            List<int> winners = new List<int>();

            Assert.AreEqual(0, food.ActiveCount);
            Assert.AreEqual(0, food.ResolveClaims(winners));
        }

        [Test]
        public void NormalisedAgeRunsFromZeroToOneAcrossAFlakesLife() {
            FoodField food = BuildField();
            food.Emit(Vector2.zero);

            Vector2 position;
            float age;
            food.TryGetParticle(0, out position, out age);
            Assert.AreEqual(0f, age, Tolerance);

            food.Tick(5f, FloorY);
            food.TryGetParticle(0, out position, out age);
            Assert.AreEqual(0.5f, age, Tolerance, "The renderer fades a flake out on this, so it has to be a fraction of its life.");
        }

        [Test]
        public void ANullWinnerListIsSurvivable() {
            FoodField food = BuildField();
            food.Emit(Vector2.zero);
            food.SubmitClaim(0, 1f, AggressiveFish);

            Assert.AreEqual(0, food.ResolveClaims(null));
        }
    }
}
