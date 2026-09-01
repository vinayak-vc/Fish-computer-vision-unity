using System.Collections.Generic;

using NUnit.Framework;

using ViitorCloud.FishAquarium.Input;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers duplicate protection, the per-frame load cap and the wait-for-a-complete-file rule. </summary>
    public sealed class FishLoadQueueTests {
        private const float Stability = 0.2f;
        private const float Timeout = 10f;

        private FishTestFileFixture fixture;
        private FishLoadQueue queue;
        private List<string> ready;
        private List<string> timedOut;

        [SetUp]
        public void SetUp() {
            fixture = new FishTestFileFixture();
            queue = new FishLoadQueue();
            ready = new List<string>();
            timedOut = new List<string>();
        }

        [TearDown]
        public void TearDown() {
            fixture.Dispose();
        }

        [Test]
        public void TheSameFileIsOnlyEverHandedOutOnce() {
            string path = fixture.WritePng("fish_001.png", 16, 16);

            queue.Enqueue(path);
            queue.Enqueue(path);
            queue.Enqueue(path);

            queue.DrainReady(0f, Stability, Timeout, 16, ready, timedOut);
            queue.DrainReady(Stability + 0.1f, Stability, Timeout, 16, ready, timedOut);

            Assert.AreEqual(1, ready.Count, "A path already seen this session must never be queued again.");
        }

        [Test]
        public void ADrainedFileIsNotReturnedOnALaterDrain() {
            string path = fixture.WritePng("fish_001.png", 16, 16);
            queue.Enqueue(path);

            queue.DrainReady(0f, Stability, Timeout, 16, ready, timedOut);
            queue.DrainReady(Stability + 0.1f, Stability, Timeout, 16, ready, timedOut);
            Assert.AreEqual(1, ready.Count);

            queue.DrainReady(Stability + 1f, Stability, Timeout, 16, ready, timedOut);
            Assert.AreEqual(0, ready.Count);
        }

        [Test]
        public void AFileIsHeldBackUntilItsSizeHasStoppedChanging() {
            string path = fixture.WritePng("fish_001.png", 16, 16);
            queue.Enqueue(path);

            queue.DrainReady(0f, Stability, Timeout, 16, ready, timedOut);
            Assert.AreEqual(0, ready.Count, "The first sighting only records the size; nothing is ready yet.");

            queue.DrainReady(0.05f, Stability, Timeout, 16, ready, timedOut);
            Assert.AreEqual(0, ready.Count, "The stability window has not elapsed yet.");

            queue.DrainReady(Stability + 0.1f, Stability, Timeout, 16, ready, timedOut);
            Assert.AreEqual(1, ready.Count);
            Assert.AreEqual(path, ready[0]);
        }

        [Test]
        public void MaxResultsSpreadsABurstOfFilesAcrossFrames() {
            for (int i = 0; i < 5; i++) {
                queue.Enqueue(fixture.WritePng("fish_00" + i + ".png", 16, 16));
            }

            queue.DrainReady(0f, Stability, Timeout, 2, ready, timedOut);
            queue.DrainReady(Stability + 0.1f, Stability, Timeout, 2, ready, timedOut);

            Assert.AreEqual(2, ready.Count, "Only the configured number of files may be handed over per call.");
            Assert.AreEqual(3, queue.PendingCount);
        }

        [Test]
        public void AFileThatNeverAppearsIsAbandonedAfterTheTimeout() {
            queue.Enqueue(fixture.FolderPath + "/never_written.png");

            queue.DrainReady(0f, Stability, Timeout, 16, ready, timedOut);
            Assert.AreEqual(0, timedOut.Count);

            queue.DrainReady(Timeout + 1f, Stability, Timeout, 16, ready, timedOut);

            Assert.AreEqual(0, ready.Count);
            Assert.AreEqual(1, timedOut.Count);
            Assert.AreEqual(0, queue.PendingCount);
        }

        [Test]
        public void ResetKnownPathsAllowsTheSameFileToBeIngestedAgain() {
            string path = fixture.WritePng("fish_001.png", 16, 16);

            queue.Enqueue(path);
            queue.DrainReady(0f, Stability, Timeout, 16, ready, timedOut);
            queue.DrainReady(Stability + 0.1f, Stability, Timeout, 16, ready, timedOut);
            Assert.AreEqual(1, ready.Count);

            queue.ResetKnownPaths();
            queue.Enqueue(path);
            queue.DrainReady(0f, Stability, Timeout, 16, ready, timedOut);
            queue.DrainReady(Stability + 0.1f, Stability, Timeout, 16, ready, timedOut);

            Assert.AreEqual(1, ready.Count);
        }

        [Test]
        public void ProcessedCountTracksDistinctFilesOnly() {
            string path = fixture.WritePng("fish_001.png", 16, 16);

            queue.MarkProcessed(path);
            queue.MarkProcessed(path);

            Assert.AreEqual(1, queue.ProcessedCount);
        }

        [Test]
        public void HasSeenReportsPathsRegardlessOfSeparatorStyle() {
            string path = fixture.WritePng("fish_001.png", 16, 16);
            queue.Enqueue(path);

            Assert.IsTrue(queue.HasSeen(path));
            Assert.IsTrue(queue.HasSeen(path.ToUpperInvariant()));
            Assert.IsFalse(queue.HasSeen(fixture.FolderPath + "/other.png"));
        }
    }
}
