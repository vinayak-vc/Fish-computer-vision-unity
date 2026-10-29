using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Courtship, laying and inheritance. Design points 5 and 8.
    ///
    /// The distinction worth guarding is sustained company against cumulative company. Familiarity that
    /// only ever accrued would eventually pair every fish that had once swum past another, and the tank
    /// would fill with babies from couples that were never together. The decay is what makes the
    /// requirement mean what it says.
    /// </summary>
    public sealed class FishSocialBondTests {
        private const float Tolerance = 0.0005f;

        private const int FishA = 1;
        private const int FishB = 2;
        private const int FishC = 3;

        private static FishBondRegistry BuildRegistry() {
            return new FishBondRegistry(3f, 0.5f, 5f);
        }

        private static FishTraits BuildTraits(string identity, float value, Color colour) {
            return new FishTraits(identity, value, value, value, value, value, value, value, value,
                FishTraits.BucketRarity(value), FishObjectType.Fish, 0.9f, FishTraitSource.Payload, colour);
        }

        /// <summary> Simulates two fish keeping each other company, which credits the bond from both sides. </summary>
        private static void KeepCompany(FishBondRegistry registry, int first, int second, float seconds) {
            List<long> ready = new List<long>();
            int steps = Mathf.CeilToInt(seconds / 0.1f);

            for (int i = 0; i < steps; i++) {
                registry.RecordProximity(first, second, 0.1f);
                registry.RecordProximity(second, first, 0.1f);
                registry.Tick(0.1f, ready);
            }
        }

        [Test]
        public void StrangersAreNotPaired() {
            FishBondRegistry registry = BuildRegistry();

            Assert.AreEqual(0, registry.PairCount);
            Assert.AreEqual(0f, registry.GetFamiliarity(FishA, FishB), Tolerance);
            Assert.AreEqual(0, registry.GetPartner(FishA));
        }

        [Test]
        public void SustainedCompanyFormsAPair() {
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            Assert.AreEqual(1, registry.PairCount);
            Assert.AreEqual(FishB, registry.GetPartner(FishA));
            Assert.AreEqual(FishA, registry.GetPartner(FishB));
        }

        [Test]
        public void APassingGlanceDoesNotFormAPair() {
            // A fish that swam past another for half a second is not courting it.
            FishBondRegistry registry = BuildRegistry();
            List<long> ready = new List<long>();

            for (int i = 0; i < 5; i++) {
                registry.RecordProximity(FishA, FishB, 0.1f);
                registry.Tick(0.1f, ready);
            }

            Assert.AreEqual(0, registry.PairCount);
        }

        [Test]
        public void FamiliarityFadesWhenFishDriftApart() {
            FishBondRegistry registry = BuildRegistry();
            List<long> ready = new List<long>();

            registry.RecordProximity(FishA, FishB, 1f);
            float earned = registry.GetFamiliarity(FishA, FishB);

            for (int i = 0; i < 5; i++) {
                registry.Tick(0.1f, ready);
            }

            Assert.Less(registry.GetFamiliarity(FishA, FishB), earned,
                "Company has to be sustained; cumulative company would pair the whole tank eventually.");
        }

        [Test]
        public void TheBondKeyIsTheSameWhicheverWayRound() {
            FishBondRegistry registry = BuildRegistry();
            registry.RecordProximity(FishA, FishB, 1f);

            Assert.AreEqual(registry.GetFamiliarity(FishA, FishB), registry.GetFamiliarity(FishB, FishA), Tolerance);
            Assert.AreEqual(1, registry.TrackedBondCount, "(A, B) and (B, A) are one bond, not two.");
        }

        [Test]
        public void AFishKeepsOnlyOnePartner() {
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);
            KeepCompany(registry, FishA, FishC, 6f);

            Assert.AreEqual(FishB, registry.GetPartner(FishA), "A is already spoken for.");
            Assert.AreEqual(0, registry.GetPartner(FishC));
            Assert.AreEqual(1, registry.PairCount);
        }

        [Test]
        public void AFormedPairSurvivesBeingSeparated() {
            // A bond that evaporated the first time a predator drove two fish apart would never produce
            // anything at all.
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            List<long> ready = new List<long>();
            for (int i = 0; i < 200; i++) {
                registry.Tick(0.1f, ready);
            }

            Assert.AreEqual(FishB, registry.GetPartner(FishA));
        }

        [Test]
        public void APairEventuallyBecomesReadyToLay() {
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            List<long> ready = new List<long>();
            for (int i = 0; i < 60; i++) {
                registry.Tick(0.1f, ready);

                if (ready.Count > 0) {
                    break;
                }
            }

            Assert.AreEqual(1, ready.Count, "Five seconds of brooding should have produced an egg.");

            int first, second;
            registry.UnpackKey(ready[0], out first, out second);
            Assert.AreEqual(FishA, first);
            Assert.AreEqual(FishB, second);
        }

        [Test]
        public void BroodProgressRunsFromZeroToOne() {
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            float early = registry.GetBroodProgress(FishA, FishB);

            List<long> ready = new List<long>();
            for (int i = 0; i < 25; i++) {
                registry.Tick(0.1f, ready);
            }

            float later = registry.GetBroodProgress(FishA, FishB);

            Assert.Greater(later, early);
            Assert.LessOrEqual(later, 1f);
        }

        [Test]
        public void APairThatHasLaidStartsANewBrood() {
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            List<long> ready = new List<long>();
            for (int i = 0; i < 60 && ready.Count == 0; i++) {
                registry.Tick(0.1f, ready);
            }

            long key = ready[0];
            Assert.AreEqual(0, registry.GetBroodCount(key));

            registry.ConsumeBrood(key);

            Assert.AreEqual(1, registry.GetBroodCount(key));
            Assert.AreEqual(0f, registry.GetBroodProgress(FishA, FishB), Tolerance, "The next brood starts from nothing.");
        }

        [Test]
        public void AnUnconsumedBroodStaysReadyRatherThanBeingLost() {
            // The aquarium may refuse an egg because the nursery is full. That should delay the pair, not
            // make them start their wait over.
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            List<long> ready = new List<long>();
            for (int i = 0; i < 60 && ready.Count == 0; i++) {
                registry.Tick(0.1f, ready);
            }

            Assert.AreEqual(1, ready.Count);

            registry.Tick(0.1f, ready);
            Assert.AreEqual(1, ready.Count, "Still ready next frame, because nobody took the egg.");
        }

        [Test]
        public void ForgettingAFishUnpairsItsPartner() {
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            registry.Forget(FishA);

            Assert.AreEqual(0, registry.GetPartner(FishB), "A partner waiting for a removed fish would never brood again.");
            Assert.AreEqual(0, registry.PairCount);
        }

        [Test]
        public void ForgettingAFishDropsItsBondsToo() {
            FishBondRegistry registry = BuildRegistry();
            registry.RecordProximity(FishA, FishB, 1f);
            registry.RecordProximity(FishA, FishC, 1f);

            registry.Forget(FishA);

            Assert.AreEqual(0f, registry.GetFamiliarity(FishA, FishB), Tolerance);
            Assert.AreEqual(0f, registry.GetFamiliarity(FishA, FishC), Tolerance);
        }

        [Test]
        public void AFishCannotBondWithItself() {
            FishBondRegistry registry = BuildRegistry();
            registry.RecordProximity(FishA, FishA, 10f);

            Assert.AreEqual(0, registry.PairCount);
            Assert.AreEqual(0, registry.TrackedBondCount);
        }

        [Test]
        public void ClearingForgetsEveryone() {
            FishBondRegistry registry = BuildRegistry();
            KeepCompany(registry, FishA, FishB, 4f);

            registry.Clear();

            Assert.AreEqual(0, registry.PairCount);
            Assert.AreEqual(0, registry.TrackedBondCount);
        }

        [Test]
        public void ABabyTakesAfterBothParents() {
            FishTraits mother = BuildTraits("mother", 0.2f, Color.red);
            FishTraits father = BuildTraits("father", 0.8f, Color.blue);

            FishTraits baby = FishTraitBlend.Blend(mother, father, 0, 0f, "baby");

            Assert.AreEqual(0.5f, baby.Speed, 0.01f, "With no jitter a baby is the exact midpoint.");
            Assert.AreEqual(0.5f, baby.Grace, 0.01f);
            Assert.AreEqual(FishTraitSource.Inherited, baby.Source);
        }

        [Test]
        public void ABabysColourIsBlendedFromItsParents() {
            FishTraits mother = BuildTraits("mother", 0.5f, Color.red);
            FishTraits father = BuildTraits("father", 0.5f, Color.blue);

            FishTraits baby = FishTraitBlend.Blend(mother, father, 0, 0f, "baby");

            Assert.AreEqual(0.5f, baby.DominantColour.r, 0.01f);
            Assert.AreEqual(0.5f, baby.DominantColour.b, 0.01f);
        }

        [Test]
        public void SiblingsAreNotIdentical() {
            // A pure average would make every baby of one pair the same, and an evening of breeding would
            // converge the whole tank on one middling personality.
            FishTraits mother = BuildTraits("mother", 0.3f, Color.white);
            FishTraits father = BuildTraits("father", 0.7f, Color.white);

            FishTraits firstBorn = FishTraitBlend.Blend(mother, father, 0, 0.2f, "baby_0");
            FishTraits secondBorn = FishTraitBlend.Blend(mother, father, 1, 0.2f, "baby_1");

            Assert.AreNotEqual(firstBorn.Speed, secondBorn.Speed);
        }

        [Test]
        public void TheSameBroodAlwaysProducesTheSameBaby() {
            FishTraits mother = BuildTraits("mother", 0.3f, Color.white);
            FishTraits father = BuildTraits("father", 0.7f, Color.white);

            FishTraits first = FishTraitBlend.Blend(mother, father, 2, 0.2f, "baby_2");
            FishTraits second = FishTraitBlend.Blend(mother, father, 2, 0.2f, "baby_2");

            Assert.AreEqual(first.Speed, second.Speed);
            Assert.AreEqual(first.Fear, second.Fear);
        }

        [Test]
        public void RarityIsInheritedWithoutDrift() {
            // Rarity gates the loud effects of M9. Letting it wander upwards through breeding would turn a
            // tank of ordinary drawings into a tank of legendaries in an evening.
            FishTraits mother = BuildTraits("mother", 0.9f, Color.white);
            FishTraits father = BuildTraits("father", 0.9f, Color.white);

            FishTraits baby = FishTraitBlend.Blend(mother, father, 0, 0.5f, "baby");

            Assert.AreEqual(0.9f, baby.RarityScore, Tolerance);
        }

        [Test]
        public void EveryBlendedTraitStaysInRange() {
            FishTraits mother = BuildTraits("mother", 0f, Color.white);
            FishTraits father = BuildTraits("father", 1f, Color.white);

            for (int brood = 0; brood < 50; brood++) {
                FishTraits baby = FishTraitBlend.Blend(mother, father, brood, 0.5f, "baby_" + brood);

                Assert.GreaterOrEqual(baby.Speed, 0f);
                Assert.LessOrEqual(baby.Speed, 1f);
                Assert.GreaterOrEqual(baby.Fear, 0f);
                Assert.LessOrEqual(baby.Fear, 1f);
            }
        }

        [Test]
        public void ABabyIsSmallerThanItsParents() {
            float scale = FishTraitBlend.BlendWorldScale(2f, 3f, 0.45f);

            Assert.AreEqual(2.5f * 0.45f, scale, Tolerance);
            Assert.Less(scale, 2f, "Design point 5 asks for a small fish.");
        }

        [Test]
        public void BlendingWithAMissingParentIsSurvivable() {
            FishTraits present = BuildTraits("present", 0.4f, Color.white);

            Assert.AreEqual(present, FishTraitBlend.Blend(present, null, 0, 0.1f, "baby"));
            Assert.AreEqual(present, FishTraitBlend.Blend(null, present, 0, 0.1f, "baby"));
            Assert.IsNotNull(FishTraitBlend.Blend(null, null, 0, 0.1f, "baby"));
        }

        [Test]
        public void AnEggCarriesEverythingItsBabyNeeds() {
            // By hatching time a parent may be gone - removed to make room, or lost to a restart - so the
            // egg has to hold the blend rather than look its parents up.
            EggField field = new EggField(2, 3f);
            FishTraits blended = BuildTraits("baby", 0.5f, Color.green);

            Assert.IsTrue(field.TryLay(new Vector2(1f, 2f), blended, "parent_sprite", Color.green, 0.8f));

            EggField.Egg egg;
            Assert.IsTrue(field.TryGetEgg(0, out egg));
            Assert.AreEqual(blended, egg.Traits);
            Assert.AreEqual("parent_sprite", egg.SpriteKey);
            Assert.AreEqual(0.8f, egg.WorldScale, Tolerance);
        }

        [Test]
        public void AnEggHatchesOnceItHasIncubated() {
            EggField field = new EggField(2, 2f);
            field.TryLay(Vector2.zero, BuildTraits("baby", 0.5f, Color.white), "parent", Color.white, 1f);

            List<int> hatched = new List<int>();

            Assert.AreEqual(0, field.CollectHatched(1f, hatched), "Not yet.");
            Assert.AreEqual(1, field.CollectHatched(1.5f, hatched), "Now.");
            Assert.AreEqual(0, hatched[0]);
        }

        [Test]
        public void AHatchedEggStaysUntilTheBabyExists() {
            // The aquarium may refuse the baby because the tank is full; the egg must not vanish first.
            EggField field = new EggField(2, 1f);
            field.TryLay(Vector2.zero, BuildTraits("baby", 0.5f, Color.white), "parent", Color.white, 1f);

            List<int> hatched = new List<int>();
            field.CollectHatched(2f, hatched);

            Assert.AreEqual(1, field.ActiveCount, "Still there until the caller removes it.");

            field.Remove(hatched[0]);
            Assert.AreEqual(0, field.ActiveCount);
        }

        [Test]
        public void ANurseryCannotOverflow() {
            EggField field = new EggField(3, 5f);

            for (int i = 0; i < 10; i++) {
                field.TryLay(Vector2.zero, BuildTraits("baby", 0.5f, Color.white), "parent", Color.white, 1f);
            }

            Assert.AreEqual(3, field.ActiveCount, "An egg about to hatch is a fish; it must not be recycled to make room.");
        }

        [Test]
        public void AnEggRipensVisibly() {
            EggField field = new EggField(1, 4f);
            field.TryLay(new Vector2(2f, 3f), BuildTraits("baby", 0.5f, Color.cyan), "parent", Color.cyan, 1f);

            List<int> hatched = new List<int>();
            field.CollectHatched(2f, hatched);

            Vector2 position;
            Color tint;
            float ripeness;

            Assert.IsTrue(field.TryGetVisual(0, out position, out tint, out ripeness));
            Assert.AreEqual(0.5f, ripeness, 0.01f, "Halfway through incubation.");
            Assert.AreEqual(Color.cyan, tint);
        }
    }
}
