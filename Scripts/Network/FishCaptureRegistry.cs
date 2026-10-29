using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Remembers which capture ids have already been processed, so a repeat arrival is discarded
    /// silently. Required by section 8 of the interaction contract: on every connect Python re-sends the
    /// most recent captures, and without this a restart of either side duplicates the whole tank.
    ///
    /// Claimed from the decode worker rather than the main thread, immediately after the JSON parse and
    /// before the base64 decode, so a duplicate costs one parse instead of a parse plus a megabyte of
    /// base64 plus a texture upload. ConcurrentDictionary makes TryClaim atomic, which is what makes that
    /// safe; two workers racing on the same id cannot both win.
    ///
    /// **Bounded, and it has to be.** Persistence carries these ids across restarts - otherwise a
    /// restarted aquarium restores its fish from disk and then re-admits Python's whole replay window as
    /// brand new arrivals, duplicating the tank on every restart and undoing the point of M0. An
    /// unbounded set would grow for the life of an installation and be written to disk every save, so the
    /// oldest ids are evicted past a cap.
    ///
    /// Eviction is safe because Python only ever replays the most recent `publish.replay_last` captures,
    /// ten by default. Anything evicted is far older than that and will never be sent again. The claim
    /// order is kept alongside the set purely so the eviction has something to evict and so persistence
    /// can write out the newest ids rather than an arbitrary selection.
    /// </summary>
    public sealed class FishCaptureRegistry {
        /// <summary>
        /// Ids remembered before the oldest is dropped. Three orders of magnitude more than the replay
        /// window it needs to cover, so the safety margin is not a judgement call.
        /// </summary>
        public const int DefaultCapacity = 4096;

        private readonly ConcurrentDictionary<string, byte> claimed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> claimOrder = new ConcurrentQueue<string>();
        private readonly int capacity;

        public FishCaptureRegistry() : this(DefaultCapacity) {
        }

        public FishCaptureRegistry(int idCapacity) {
            capacity = Math.Max(1, idCapacity);
        }

        /// <summary> Ids currently remembered. Never more than the capacity. </summary>
        public int Count {
            get { return claimed.Count; }
        }

        public int Capacity {
            get { return capacity; }
        }

        /// <summary>
        /// Claims an id. Returns true the first time an id is seen and false every time after, which is
        /// the caller's signal to drop the payload without logging an error - a replayed duplicate is
        /// normal traffic, not a fault.
        ///
        /// An empty id is refused: it cannot be deduplicated meaningfully, and FishCaptureMessage.IsUsable
        /// has already rejected the payload by the time this would matter.
        /// </summary>
        public bool TryClaim(string captureId) {
            if (string.IsNullOrEmpty(captureId)) {
                return false;
            }

            if (!claimed.TryAdd(captureId, 0)) {
                return false;
            }

            claimOrder.Enqueue(captureId);
            EvictOldest();
            return true;
        }

        public bool IsClaimed(string captureId) {
            if (string.IsNullOrEmpty(captureId)) {
                return false;
            }

            return claimed.ContainsKey(captureId);
        }

        /// <summary> Forgets one id so the same capture can be ingested again. Development aid. </summary>
        public void Forget(string captureId) {
            if (string.IsNullOrEmpty(captureId)) {
                return;
            }

            byte discarded;
            claimed.TryRemove(captureId, out discarded);
        }

        /// <summary> Forgets every id, so the next replay burst is accepted in full. Development aid. </summary>
        public void Clear() {
            claimed.Clear();

            string discarded;
            while (claimOrder.TryDequeue(out discarded)) {
            }
        }

        private void EvictOldest() {
            while (claimed.Count > capacity) {
                string oldest;
                if (!claimOrder.TryDequeue(out oldest)) {
                    return;
                }

                byte discarded;
                claimed.TryRemove(oldest, out discarded);
            }
        }
    }
}
