using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Moves JSON parsing and base64 decoding off the Unity main thread.
    ///
    /// A capture payload carries up to about a megabyte of base64, and the replay burst delivers ten of
    /// them at once. Measured here, LitJson costs roughly 24 microseconds per KB, so parsing inline would
    /// stall the frame for tens of milliseconds per fish. The worker produces a byte array and a few
    /// scalars, none of which need the Unity API; texture creation stays on the main thread where it must be.
    ///
    /// LitJson guards its type-metadata caches with locks, so concurrent workers are safe.
    /// </summary>
    public sealed class FishPayloadDecodeQueue {
        /// <summary> A payload that has been turned into PNG bytes and is waiting for the main thread. </summary>
        public struct DecodedFish {
            public string Id { get; set; }
            public byte[] PngBytes { get; set; }
            public bool IsReplay { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int SchemaVersion { get; set; }
        }

        private readonly ConcurrentQueue<DecodedFish> ready = new ConcurrentQueue<DecodedFish>();
        private readonly ConcurrentQueue<string> failures = new ConcurrentQueue<string>();

        private int decodesInFlight;
        private int decodedTotal;

        /// <summary> Payloads handed to a worker but not yet decoded. </summary>
        public int PendingCount {
            get { return Volatile.Read(ref decodesInFlight); }
        }

        public int ReadyCount {
            get { return ready.Count; }
        }

        public int DecodedTotal {
            get { return Volatile.Read(ref decodedTotal); }
        }

        /// <summary>
        /// Call from the main thread with the raw JSON of one capture event. Only a null check happens here;
        /// parsing and decoding are queued to a worker.
        /// </summary>
        public bool Submit(string rawJson, out string reason) {
            if (string.IsNullOrEmpty(rawJson)) {
                reason = "empty payload";
                return false;
            }

            reason = string.Empty;
            Interlocked.Increment(ref decodesInFlight);
            ThreadPool.QueueUserWorkItem(DecodeOnWorkerThread, rawJson);
            return true;
        }

        /// <summary> Submits an already-parsed message. Used by the self-test and by the edit-mode tests. </summary>
        public bool Submit(FishCaptureMessage message, out string reason) {
            if (message == null) {
                reason = "null payload";
                return false;
            }

            if (!message.IsUsable(out reason)) {
                return false;
            }

            Interlocked.Increment(ref decodesInFlight);
            ThreadPool.QueueUserWorkItem(DecodeMessageOnWorkerThread, message);
            return true;
        }

        /// <summary>
        /// Main thread. Moves at most maxResults decoded payloads out of the queue, so a burst is spread
        /// across frames instead of landing in one.
        /// </summary>
        public int Drain(int maxResults, List<DecodedFish> results) {
            if (results == null) {
                return 0;
            }

            results.Clear();

            while (results.Count < maxResults) {
                DecodedFish decoded;
                if (!ready.TryDequeue(out decoded)) {
                    break;
                }

                results.Add(decoded);
            }

            return results.Count;
        }

        /// <summary> Main thread. Pulls decode failures out for logging; workers never touch the Unity API. </summary>
        public bool TryTakeFailure(out string failure) {
            return failures.TryDequeue(out failure);
        }

        public void Clear() {
            DecodedFish discarded;
            while (ready.TryDequeue(out discarded)) {
            }

            string discardedFailure;
            while (failures.TryDequeue(out discardedFailure)) {
            }
        }

        private void DecodeOnWorkerThread(object state) {
            string rawJson = (string)state;
            FishCaptureMessage message = null;

            try {
                message = BestHTTP.JSON.LitJson.JsonMapper.ToObject<FishCaptureMessage>(rawJson);
            } catch (Exception exception) {
                failures.Enqueue("could not parse a capture payload: " + exception.Message);
                Interlocked.Decrement(ref decodesInFlight);
                return;
            }

            string reason = string.Empty;
            if (message == null || !message.IsUsable(out reason)) {
                failures.Enqueue("discarded a capture payload: " + (message == null ? "parser returned nothing" : reason));
                Interlocked.Decrement(ref decodesInFlight);
                return;
            }

            DecodeMessage(message);
        }

        private void DecodeMessageOnWorkerThread(object state) {
            DecodeMessage((FishCaptureMessage)state);
        }

        private void DecodeMessage(FishCaptureMessage message) {
            try {
                byte[] bytes = Convert.FromBase64String(message.png_base64);

                DecodedFish decoded = new DecodedFish();
                decoded.Id = message.id;
                decoded.PngBytes = bytes;
                decoded.IsReplay = message.replay;
                decoded.Width = message.width;
                decoded.Height = message.height;
                decoded.SchemaVersion = message.schema_version;

                ready.Enqueue(decoded);
                Interlocked.Increment(ref decodedTotal);
            } catch (Exception exception) {
                failures.Enqueue(message.id + " - base64 decode failed: " + exception.Message);
            } finally {
                Interlocked.Decrement(ref decodesInFlight);
            }
        }
    }
}
