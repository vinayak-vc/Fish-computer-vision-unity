using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Aquarium;
using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The ambient systems that have to make the tank worth watching with nobody touching it: the mood,
    /// the time of day, the weather, the currents and the predator.
    ///
    /// All four take deltaTime as an argument rather than reading the clock, which is the only reason any
    /// of this is testable - a five-minute day and a storm every two minutes are otherwise things you can
    /// only verify by sitting and waiting.
    /// </summary>
    public sealed class AquariumWorldSystemsTests {
        private const float Tolerance = 0.0005f;

        private static readonly Rect Tank = new Rect(-10f, -5f, 20f, 10f);

        private static AquariumEnergy BuildEnergy() {
            return new AquariumEnergy(0.35f, 0.8f, 0.12f, 0.25f);
        }

        private static void Advance(AquariumEnergy energy, float seconds, float stimulus) {
            for (int i = 0; i < Mathf.CeilToInt(seconds / 0.05f); i++) {
                energy.Tick(0.05f, stimulus);
            }
        }

        [Test]
        public void AnIdleTankSitsAtItsRestingMood() {
            AquariumEnergy energy = BuildEnergy();

            Assert.AreEqual(0.35f, energy.Current, Tolerance, "An empty tank should still be alive, not flat.");
        }

        [Test]
        public void ActivityRaisesTheMood() {
            AquariumEnergy energy = BuildEnergy();
            Advance(energy, 2f, 1f);

            Assert.Greater(energy.Current, 0.9f);
        }

        [Test]
        public void TheMoodSettlesBackWhenTheVisitorLeaves() {
            AquariumEnergy energy = BuildEnergy();
            Advance(energy, 2f, 1f);
            Advance(energy, 20f, 0f);

            Assert.AreEqual(0.35f, energy.Current, 0.01f, "It must fall back to resting, not to zero.");
        }

        [Test]
        public void TheMoodNeverFallsBelowResting() {
            AquariumEnergy energy = BuildEnergy();
            Advance(energy, 120f, 0f);

            Assert.GreaterOrEqual(energy.Current, 0.35f - Tolerance);
        }

        [Test]
        public void ATankWakesFasterThanItSettles() {
            // Not symmetry for its own sake: a tank that deflated as fast as it woke would visibly sag
            // every time somebody paused, which reads as a bug rather than as calm.
            AquariumEnergy rising = BuildEnergy();
            Advance(rising, 1f, 1f);
            float gained = rising.Current - 0.35f;

            AquariumEnergy falling = BuildEnergy();
            Advance(falling, 2f, 1f);
            float peak = falling.Current;
            Advance(falling, 1f, 0f);
            float lost = peak - falling.Current;

            Assert.Greater(gained, lost);
        }

        [Test]
        public void ACaptureLiftsTheMoodImmediately() {
            AquariumEnergy energy = BuildEnergy();
            float before = energy.Current;

            energy.RecordCapture();

            Assert.AreEqual(before + 0.25f, energy.Current, Tolerance, "A drawing is an event with no duration, so it bumps rather than holds.");
        }

        [Test]
        public void TheMoodStaysWithinRange() {
            AquariumEnergy energy = BuildEnergy();

            for (int i = 0; i < 20; i++) {
                energy.RecordCapture();
            }

            Assert.LessOrEqual(energy.Current, 1f);

            Advance(energy, 5f, 5f);
            Assert.LessOrEqual(energy.Current, 1f, "An out-of-range stimulus must not push the mood past 1.");
        }

        [Test]
        public void ResettingReturnsTheMoodToResting() {
            AquariumEnergy energy = BuildEnergy();
            Advance(energy, 3f, 1f);
            energy.Reset();

            Assert.AreEqual(0.35f, energy.Current, Tolerance);
        }

        [Test]
        public void ADayRunsFromNightToMiddayAndBack() {
            AquariumCycle cycle = new AquariumCycle(100f, 1000f, 10f, 2f);

            Assert.AreEqual(0f, cycle.TimeOfDay, Tolerance);
            float midnightLight = cycle.Daylight;

            for (int i = 0; i < 500; i++) {
                cycle.Tick(0.1f);
            }

            Assert.AreEqual(0.5f, cycle.TimeOfDay, 0.01f, "Half a day in.");
            Assert.Greater(cycle.Daylight, midnightLight + 0.5f, "Midday must be visibly brighter than midnight.");
        }

        [Test]
        public void DaylightIsSmoothRatherThanASawtooth() {
            // Dawn and dusk have to take real time to pass, or the tank snaps between two looks.
            AquariumCycle cycle = new AquariumCycle(100f, 1000f, 10f, 2f);
            float previous = cycle.Daylight;

            for (int i = 0; i < 200; i++) {
                cycle.Tick(0.25f);
                float current = cycle.Daylight;

                Assert.Less(Mathf.Abs(current - previous), 0.06f, "Daylight jumped at step " + i);
                previous = current;
            }
        }

        [Test]
        public void DaylightStaysWithinRangeAcrossSeveralDays() {
            AquariumCycle cycle = new AquariumCycle(20f, 1000f, 10f, 2f);

            for (int i = 0; i < 2000; i++) {
                cycle.Tick(0.05f);

                Assert.GreaterOrEqual(cycle.Daylight, -Tolerance);
                Assert.LessOrEqual(cycle.Daylight, 1f + Tolerance);
            }
        }

        [Test]
        public void AStormArrivesOnScheduleAndPasses() {
            AquariumCycle cycle = new AquariumCycle(1000f, 10f, 4f, 1f);

            Assert.IsFalse(cycle.IsStorming);

            for (int i = 0; i < 110; i++) {
                cycle.Tick(0.1f);
            }

            Assert.IsTrue(cycle.IsStorming, "A storm was due after ten seconds.");

            for (int i = 0; i < 50; i++) {
                cycle.Tick(0.1f);
            }

            Assert.IsFalse(cycle.IsStorming, "And it should have blown over four seconds later.");
        }

        [Test]
        public void StormIntensityRampsInAndOutRatherThanSnapping() {
            AquariumCycle cycle = new AquariumCycle(1000f, 1f, 6f, 2f);
            cycle.ForceStorm();

            Assert.AreEqual(0f, cycle.StormIntensity, Tolerance, "It starts from nothing.");

            for (int i = 0; i < 30; i++) {
                cycle.Tick(0.1f);
            }

            Assert.AreEqual(1f, cycle.StormIntensity, 0.02f, "And reaches full strength in the middle.");

            for (int i = 0; i < 29; i++) {
                cycle.Tick(0.1f);
            }

            Assert.Less(cycle.StormIntensity, 0.2f, "And is easing off by the end.");
        }

        [Test]
        public void ThereIsNoStormIntensityOutsideAStorm() {
            AquariumCycle cycle = new AquariumCycle(1000f, 1000f, 10f, 2f);

            for (int i = 0; i < 100; i++) {
                cycle.Tick(0.1f);
                Assert.AreEqual(0f, cycle.StormIntensity, Tolerance);
            }
        }

        [Test]
        public void ARampLongerThanTheStormCannotStopItPeaking() {
            // A misconfigured ramp must not produce a storm that never actually arrives.
            AquariumCycle cycle = new AquariumCycle(1000f, 1f, 4f, 999f);
            cycle.ForceStorm();

            for (int i = 0; i < 20; i++) {
                cycle.Tick(0.1f);
            }

            Assert.Greater(cycle.StormIntensity, 0.5f);
        }

        [Test]
        public void TheCurrentIsTheSameEveryTimeForAGivenPlaceAndMoment() {
            // A current that differed between runs would be one more thing that could make the same
            // drawing behave differently, which is the promise the whole installation rests on.
            Vector2 first = AquariumFlow.Sample(new Vector2(3f, 1f), 12.5f, 7f, 0.6f, 0f);
            Vector2 second = AquariumFlow.Sample(new Vector2(3f, 1f), 12.5f, 7f, 0.6f, 0f);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void TheCurrentVariesAcrossTheTank() {
            Vector2 left = AquariumFlow.Sample(new Vector2(-8f, 0f), 5f, 4f, 0.6f, 0f);
            Vector2 right = AquariumFlow.Sample(new Vector2(8f, 0f), 5f, 4f, 0.6f, 0f);

            Assert.AreNotEqual(left, right, "A uniform current would just slide the whole tank sideways.");
        }

        [Test]
        public void TheCurrentIsZeroWhenTurnedOff() {
            Assert.AreEqual(Vector2.zero, AquariumFlow.Sample(new Vector2(2f, 2f), 3f, 5f, 0f, 0f));
        }

        [Test]
        public void TheCurrentPushesSidewaysMoreThanItPushesVertically() {
            // A current that lifted fish as hard as it carried them would fight every depth band.
            //
            // Sampled over a grid rather than along a line. The horizontal and vertical components come
            // from two differently offset noise fields, so any single path through them happens to favour
            // one or the other; only over an area do the two distributions converge and leave the
            // deliberate halving of the vertical component as the difference that remains.
            float horizontal = 0f;
            float vertical = 0f;

            for (int gx = 0; gx < 40; gx++) {
                for (int gy = 0; gy < 40; gy++) {
                    Vector2 position = new Vector2((gx - 20) * 0.9f, (gy - 20) * 0.55f);
                    Vector2 flow = AquariumFlow.Sample(position, gx * 0.13f, 6f, 1f, 0f);

                    horizontal += Mathf.Abs(flow.x);
                    vertical += Mathf.Abs(flow.y);
                }
            }

            Assert.Greater(horizontal, vertical * 1.6f,
                "The vertical component is halved on purpose; over an area that should show as roughly a factor of two.");
        }

        [Test]
        public void ATurbulentCurrentIsStrongerThanACalmOne() {
            float calm = 0f;
            float stormy = 0f;

            for (int i = 0; i < 200; i++) {
                Vector2 position = new Vector2(i * 0.41f, i * 0.17f);
                calm += AquariumFlow.Sample(position, i * 0.05f, 6f, 0.5f, 0f).magnitude;
                stormy += AquariumFlow.Sample(position, i * 0.05f, 6f, 0.5f, 1.5f).magnitude;
            }

            Assert.Greater(stormy, calm);
        }

        [Test]
        public void TheMigrationDriftTurnsOverTimeAndIsMostlySideways() {
            Vector2 early = AquariumFlow.SampleMigration(0f, 100f, 1f);
            Vector2 later = AquariumFlow.SampleMigration(400f, 100f, 1f);

            Assert.AreNotEqual(early, later, "A drift that never turned would pin the shoal against one wall.");
            Assert.Greater(Mathf.Abs(early.x), Mathf.Abs(early.y), "A tank is wider than it is tall.");
        }

        [Test]
        public void APredatorArrivesCrossesAndLeaves() {
            AquariumPredator predator = new AquariumPredator(5f, 4f, 1f);

            Assert.IsFalse(predator.IsPresent);

            for (int i = 0; i < 55; i++) {
                predator.Tick(0.1f, Tank);
            }

            Assert.IsTrue(predator.IsPresent, "It was due after five seconds.");
            Assert.AreEqual(1, predator.VisitCount);

            for (int i = 0; i < 45; i++) {
                predator.Tick(0.1f, Tank);
            }

            Assert.IsFalse(predator.IsPresent, "And should be gone four seconds later.");
        }

        [Test]
        public void APredatorEntersFromBeyondTheGlass() {
            AquariumPredator predator = new AquariumPredator(100f, 4f, 1f);
            predator.Appear(Tank);

            Assert.IsTrue(predator.Position.x < Tank.xMin || predator.Position.x > Tank.xMax,
                "It should swim in from off-screen, not materialise at " + predator.Position);
        }

        [Test]
        public void APredatorCrossesTheWholeTank() {
            AquariumPredator predator = new AquariumPredator(100f, 4f, 1f);
            predator.Appear(Tank);
            float startX = predator.Position.x;

            for (int i = 0; i < 39; i++) {
                predator.Tick(0.1f, Tank);
            }

            Assert.Greater(Mathf.Abs(predator.Position.x - startX), Tank.width, "It must pass right through.");
        }

        [Test]
        public void PredatorThreatRampsInAndOut() {
            AquariumPredator predator = new AquariumPredator(100f, 6f, 2f);
            predator.Appear(Tank);

            Assert.AreEqual(0f, predator.Threat, Tolerance, "Nothing snaps into a full-tank scatter.");

            for (int i = 0; i < 30; i++) {
                predator.Tick(0.1f, Tank);
            }

            Assert.AreEqual(1f, predator.Threat, 0.02f);

            for (int i = 0; i < 28; i++) {
                predator.Tick(0.1f, Tank);
            }

            Assert.Less(predator.Threat, 0.3f);
        }

        [Test]
        public void AnAbsentPredatorIsNoThreat() {
            AquariumPredator predator = new AquariumPredator(100f, 4f, 1f);

            Assert.AreEqual(0f, predator.Threat, Tolerance);
        }

        [Test]
        public void PredatorVisitsAreRareRatherThanConstant() {
            // A threat that arrives often stops being a threat and just becomes the reason the fish are
            // permanently at the edges.
            AquariumPredator predator = new AquariumPredator(60f, 5f, 1f);

            for (int i = 0; i < 600; i++) {
                predator.Tick(0.1f, Tank);
            }

            Assert.LessOrEqual(predator.VisitCount, 1, "A minute of interval should mean at most one visit a minute.");
        }

        [Test]
        public void AResetPredatorIsGone() {
            AquariumPredator predator = new AquariumPredator(100f, 4f, 1f);
            predator.Appear(Tank);
            predator.Reset();

            Assert.IsFalse(predator.IsPresent);
            Assert.AreEqual(0f, predator.Threat, Tolerance);
        }
    }
}
