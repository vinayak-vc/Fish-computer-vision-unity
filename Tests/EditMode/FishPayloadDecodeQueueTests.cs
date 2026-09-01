using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;
using ViitorCloud.FishAquarium.Network;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary> Covers the hand-off that keeps parsing and base64 decoding off the Unity main thread. </summary>
    public sealed class FishPayloadDecodeQueueTests {
        private const int WaitTimeoutMilliseconds = 5000;

        private FishPayloadDecodeQueue queue;
        private List<FishPayloadDecodeQueue.DecodedFish> drained;
        private string samplePngBase64;

        [SetUp]
        public void SetUp() {
            queue = new FishPayloadDecodeQueue();
            drained = new List<FishPayloadDecodeQueue.DecodedFish>();

            Texture2D texture = new Texture2D(8, 4, TextureFormat.RGBA32, false);
            for (int y = 0; y < 4; y++) {
                for (int x = 0; x < 8; x++) {
                    texture.SetPixel(x, y, new Color(1f, 0.4f, 0.1f, x < 4 ? 1f : 0f));
                }
            }

            texture.Apply(false, false);
            samplePngBase64 = Convert.ToBase64String(texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [TearDown]
        public void TearDown() {
            queue.Clear();
        }

        private string BuildJson(string id, bool replay) {
            string replayField = replay ? "\"replay\":true," : string.Empty;
            return "{\"id\":\"" + id + "\",\"png_base64\":\"" + samplePngBase64 + "\",\"width\":8,\"height\":4," + replayField + "\"schema_version\":1}";
        }

        /// <summary> The decode runs on a worker, so the test waits for it rather than assuming it finished. </summary>
        private bool WaitForReady(int expected) {
            Stopwatch stopwatch = Stopwatch.StartNew();

            while (stopwatch.ElapsedMilliseconds < WaitTimeoutMilliseconds) {
                if (queue.ReadyCount >= expected) {
                    return true;
                }

                Thread.Sleep(5);
            }

            return false;
        }

        [Test]
        public void RawJsonIsParsedAndDecodedIntoPngBytes() {
            string reason;
            Assert.IsTrue(queue.Submit(BuildJson("fish_001", false), out reason), reason);
            Assert.IsTrue(WaitForReady(1), "the worker never produced a decoded payload");

            Assert.AreEqual(1, queue.Drain(8, drained));
            Assert.AreEqual("fish_001", drained[0].Id);
            Assert.AreEqual(8, drained[0].Width);
            Assert.IsFalse(drained[0].IsReplay);
            Assert.IsTrue(FishTextureLoader.HasPngSignature(drained[0].PngBytes), "the decoded bytes must be a real PNG");
        }

        [Test]
        public void TheReplayFlagSurvivesTheWorker() {
            string reason;
            Assert.IsTrue(queue.Submit(BuildJson("fish_replayed", true), out reason), reason);
            Assert.IsTrue(WaitForReady(1));

            queue.Drain(8, drained);
            Assert.IsTrue(drained[0].IsReplay);
        }

        [Test]
        public void DrainHandsOutNoMoreThanTheFrameBudget() {
            string reason;
            for (int i = 0; i < 6; i++) {
                Assert.IsTrue(queue.Submit(BuildJson("fish_" + i, true), out reason), reason);
            }

            Assert.IsTrue(WaitForReady(6));

            Assert.AreEqual(2, queue.Drain(2, drained), "a burst must be spread across frames, not landed in one");
            Assert.AreEqual(4, queue.ReadyCount);
        }

        [Test]
        public void SubmittingNothingIsRejectedWithoutQueueingWork() {
            string reason;

            Assert.IsFalse(queue.Submit(string.Empty, out reason));
            Assert.IsNotEmpty(reason);
            Assert.AreEqual(0, queue.PendingCount);
        }

        [Test]
        public void MalformedJsonIsReportedAsAFailureRatherThanThrowing() {
            string reason;
            Assert.IsTrue(queue.Submit("{ this is not json", out reason));

            Stopwatch stopwatch = Stopwatch.StartNew();
            string failure = null;
            while (stopwatch.ElapsedMilliseconds < WaitTimeoutMilliseconds && !queue.TryTakeFailure(out failure)) {
                Thread.Sleep(5);
            }

            Assert.IsNotNull(failure, "a bad payload must surface as a failure the main thread can log");
            Assert.AreEqual(0, queue.ReadyCount);
        }

        [Test]
        public void BadBase64IsReportedAsAFailure() {
            string reason;
            Assert.IsTrue(queue.Submit("{\"id\":\"fish_bad\",\"png_base64\":\"!!!not base64!!!\",\"schema_version\":1}", out reason));

            Stopwatch stopwatch = Stopwatch.StartNew();
            string failure = null;
            while (stopwatch.ElapsedMilliseconds < WaitTimeoutMilliseconds && !queue.TryTakeFailure(out failure)) {
                Thread.Sleep(5);
            }

            Assert.IsNotNull(failure);
            Assert.IsTrue(failure.Contains("fish_bad"), "the failure should name the payload that failed: " + failure);
        }

        [Test]
        public void APayloadWithNoArtworkIsDiscardedOnTheWorker() {
            string reason;
            Assert.IsTrue(queue.Submit("{\"id\":\"fish_empty\",\"png_base64\":\"\",\"schema_version\":1}", out reason));

            Stopwatch stopwatch = Stopwatch.StartNew();
            string failure = null;
            while (stopwatch.ElapsedMilliseconds < WaitTimeoutMilliseconds && !queue.TryTakeFailure(out failure)) {
                Thread.Sleep(5);
            }

            Assert.IsNotNull(failure);
            Assert.AreEqual(0, queue.ReadyCount);
        }
    }
}
