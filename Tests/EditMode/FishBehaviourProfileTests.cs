using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// What each kind of drawing does. Contract section 5, design points 21 and 22.
    ///
    /// The rule these all serve is the contract's own: "a misclassification must be survivable, never
    /// fatal". Python's classifier is a heuristic and WILL be wrong, so every test here is really asking
    /// the same question - does a wrong answer still produce a creature that swims about, or does it
    /// produce a hole in the tank?
    /// </summary>
    public sealed class FishBehaviourProfileTests {
        private const float Tolerance = 0.0005f;

        private static readonly Rect Tank = new Rect(-10f, -5f, 20f, 10f);

        private static FishTraits BuildTraits(FishObjectType type, float aggression, float confidence) {
            return new FishTraits("probe", 0.5f, 0.5f, 0.5f, 0.5f, aggression, 0.5f, 0.5f, 0.1f,
                FishRarityTier.Common, type, confidence, FishTraitSource.Payload);
        }

        [Test]
        public void PlantsAndRocksAnchorToTheFloor() {
            Assert.IsTrue(FishBehaviourProfile.Anchors(FishObjectType.Plant));
            Assert.IsTrue(FishBehaviourProfile.Anchors(FishObjectType.Rock));
        }

        [Test]
        public void EverythingElseSwims() {
            Assert.IsFalse(FishBehaviourProfile.Anchors(FishObjectType.Fish));
            Assert.IsFalse(FishBehaviourProfile.Anchors(FishObjectType.Jellyfish));
            Assert.IsFalse(FishBehaviourProfile.Anchors(FishObjectType.Creature));
            Assert.IsFalse(FishBehaviourProfile.Anchors(FishObjectType.Unknown));
        }

        [Test]
        public void ARockDoesNotMoveAndAPlantOnlySways() {
            Assert.IsFalse(FishBehaviourProfile.Swims(FishObjectType.Rock));
            Assert.IsFalse(FishBehaviourProfile.Sways(FishObjectType.Rock), "The point of a rock is that it is a rock.");

            Assert.IsFalse(FishBehaviourProfile.Swims(FishObjectType.Plant));
            Assert.IsTrue(FishBehaviourProfile.Sways(FishObjectType.Plant));
        }

        [Test]
        public void AJellyfishIgnoresFlocking() {
            // Contract section 5. A pulsing bell keeping formation with a school of fish looks wrong in a
            // way that is hard to unsee.
            Assert.IsFalse(FishBehaviourProfile.Flocks(FishObjectType.Jellyfish));
            Assert.IsTrue(FishBehaviourProfile.Pulses(FishObjectType.Jellyfish));
        }

        [Test]
        public void FishCreaturesAndUnknownsAllSchool() {
            Assert.IsTrue(FishBehaviourProfile.Flocks(FishObjectType.Fish));
            Assert.IsTrue(FishBehaviourProfile.Flocks(FishObjectType.Creature));
            Assert.IsTrue(FishBehaviourProfile.Flocks(FishObjectType.Unknown), "Contract section 5: unknown behaves as a fish.");
        }

        [Test]
        public void PlantsAndRocksDoNotSchool() {
            Assert.IsFalse(FishBehaviourProfile.Flocks(FishObjectType.Plant));
            Assert.IsFalse(FishBehaviourProfile.Flocks(FishObjectType.Rock));
        }

        [Test]
        public void OnlyACreatureCanBeAPredator() {
            Assert.IsTrue(FishBehaviourProfile.IsPredatorEligible(FishObjectType.Creature));
            Assert.IsFalse(FishBehaviourProfile.IsPredatorEligible(FishObjectType.Fish));
            Assert.IsFalse(FishBehaviourProfile.IsPredatorEligible(FishObjectType.Jellyfish));
            Assert.IsFalse(FishBehaviourProfile.IsPredatorEligible(FishObjectType.Plant));
        }

        [Test]
        public void AnAggressiveCreatureIsFrightening() {
            float threat = FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Creature, 1f, 0.9f), 0.6f);

            Assert.Greater(threat, 0.9f);
        }

        [Test]
        public void ATimidCreatureIsJustAnOddLookingFish() {
            float threat = FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Creature, 0.2f, 0.9f), 0.6f);

            Assert.AreEqual(0f, threat, Tolerance, "Eligible to be a predator is not the same as being one.");
        }

        [Test]
        public void AGuessedCreatureDoesNotEmptyTheTank() {
            // Contract section 5: below 0.5 confidence the classifier is guessing, and type-specific
            // effects are skipped. Clearing the water around it is the most disruptive effect there is,
            // so a mistaken creature must not do it.
            float confident = FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Creature, 1f, 0.9f), 0.6f);
            float guessed = FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Creature, 1f, 0.3f), 0.6f);

            Assert.Greater(confident, 0f);
            Assert.AreEqual(0f, guessed, Tolerance);
        }

        [Test]
        public void ThreatRampsFromTheThresholdRatherThanSnappingOn() {
            float justOver = FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Creature, 0.62f, 0.9f), 0.6f);
            float wellOver = FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Creature, 0.9f, 0.9f), 0.6f);

            Assert.Greater(justOver, 0f);
            Assert.Less(justOver, wellOver, "The meekest qualifying creature should only be mildly alarming.");
        }

        [Test]
        public void NothingButACreatureIsEverAThreat() {
            Assert.AreEqual(0f, FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Fish, 1f, 1f), 0.6f), Tolerance);
            Assert.AreEqual(0f, FishBehaviourProfile.EvaluateThreat(BuildTraits(FishObjectType.Jellyfish, 1f, 1f), 0.6f), Tolerance);
        }

        [Test]
        public void MissingTraitsAreSurvivable() {
            Assert.AreEqual(0f, FishBehaviourProfile.EvaluateThreat(null, 0.6f), Tolerance);
        }

        [Test]
        public void AnAnchoredDrawingSitsOnTheFloorAndNotInIt() {
            float height = FishBehaviourProfile.EvaluateAnchorHeight(Tank, 2f);

            Assert.AreEqual(Tank.yMin + 1f, height, Tolerance, "Half its own height up, so it is not buried in the gravel.");
            Assert.Greater(height, Tank.yMin);
        }

        [Test]
        public void AZeroSizedDrawingStillAnchorsCleanly() {
            Assert.AreEqual(Tank.yMin, FishBehaviourProfile.EvaluateAnchorHeight(Tank, 0f), Tolerance);
            Assert.AreEqual(Tank.yMin, FishBehaviourProfile.EvaluateAnchorHeight(Tank, -5f), Tolerance);
        }

        [Test]
        public void EveryContractTypeGetsAWorkableProfile() {
            // The survivability rule, checked exhaustively: no type may end up neither anchored nor
            // swimming, which would be a drawing that exists but does nothing at all.
            FishObjectType[] types = {
                FishObjectType.Fish, FishObjectType.Plant, FishObjectType.Rock,
                FishObjectType.Jellyfish, FishObjectType.Creature, FishObjectType.Unknown
            };

            for (int i = 0; i < types.Length; i++) {
                bool anchors = FishBehaviourProfile.Anchors(types[i]);
                bool swims = FishBehaviourProfile.Swims(types[i]);

                Assert.IsTrue(anchors || swims, types[i] + " would neither hold a spot nor swim.");
                Assert.IsFalse(anchors && swims, types[i] + " cannot do both.");
            }
        }
    }
}
