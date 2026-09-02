using System.Threading.Tasks;

using NUnit.Framework;

using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// Section 8 of the interaction contract: a replayed capture the aquarium already holds is discarded
    /// silently. Without this, every reconnect duplicates the ten most recent drawings, and an unattended
    /// installation that reconnects overnight wakes up full of copies.
    /// </summary>
    public sealed class FishCaptureRegistryTests {
        [Test]
        public void AnIdIsClaimedOnceAndRefusedAfterwards() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            Assert.IsTrue(registry.TryClaim("fish_001"));
            Assert.IsFalse(registry.TryClaim("fish_001"), "The second arrival of a capture must be discarded.");
            Assert.IsFalse(registry.TryClaim("fish_001"));
        }

        [Test]
        public void DifferentIdsAreAllAccepted() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            Assert.IsTrue(registry.TryClaim("fish_001"));
            Assert.IsTrue(registry.TryClaim("fish_002"));
            Assert.AreEqual(2, registry.Count);
        }

        [Test]
        public void IdsAreComparedExactlyRatherThanLoosely() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            Assert.IsTrue(registry.TryClaim("fish_001"));
            Assert.IsTrue(registry.TryClaim("FISH_001"), "Capture ids are opaque; two ids that differ by case are two captures.");
        }

        [Test]
        public void AnEmptyIdIsRefused() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            Assert.IsFalse(registry.TryClaim(null));
            Assert.IsFalse(registry.TryClaim(string.Empty));
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void ClaimedIdsCanBeQueriedWithoutClaimingThem() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            Assert.IsFalse(registry.IsClaimed("fish_001"));
            registry.TryClaim("fish_001");

            Assert.IsTrue(registry.IsClaimed("fish_001"));
            Assert.AreEqual(1, registry.Count, "IsClaimed must not itself claim.");
        }

        [Test]
        public void ForgettingAnIdLetsItBeIngestedAgain() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            registry.TryClaim("fish_001");
            registry.Forget("fish_001");

            Assert.IsTrue(registry.TryClaim("fish_001"));
        }

        [Test]
        public void ClearingForgetsEverything() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            registry.TryClaim("fish_001");
            registry.TryClaim("fish_002");
            registry.Clear();

            Assert.AreEqual(0, registry.Count);
            Assert.IsTrue(registry.TryClaim("fish_001"));
        }

        [Test]
        public void OnlyOneOfManyRacingWorkersWinsAnId() {
            // Claims happen on ThreadPool workers, and the replay burst arrives as ten payloads at once.
            // A non-atomic check-then-add here would let two workers both decode the same capture.
            FishCaptureRegistry registry = new FishCaptureRegistry();
            int winners = 0;

            Parallel.For(0, 512, index => {
                if (registry.TryClaim("fish_contended")) {
                    System.Threading.Interlocked.Increment(ref winners);
                }
            });

            Assert.AreEqual(1, winners, "Exactly one worker may claim an id, however many race for it.");
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void ConcurrentClaimsOfDistinctIdsAreAllRecorded() {
            FishCaptureRegistry registry = new FishCaptureRegistry();

            Parallel.For(0, 512, index => {
                registry.TryClaim("fish_" + index.ToString("D4"));
            });

            Assert.AreEqual(512, registry.Count);
        }
    }
}
