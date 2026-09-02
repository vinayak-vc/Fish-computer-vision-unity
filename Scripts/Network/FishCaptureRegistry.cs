using System;
using System.Collections.Concurrent;

namespace ViitorCloud.FishAquarium.Network {
    /// <summary>
    /// Remembers which capture ids this session has already processed, so a repeat arrival is discarded
    /// silently. Required by section 8 of the interaction contract: on every connect, Python re-sends the
    /// most recent captures, and without this a restart of either side duplicates the whole tank.
    ///
    /// Claimed from the decode worker rather than the main thread, immediately after the JSON parse and
    /// before the base64 decode, so a duplicate costs one parse instead of a parse plus a megabyte of
    /// base64 plus a texture upload. ConcurrentDictionary makes TryClaim atomic, which is what makes that
    /// safe; two workers racing on the same id cannot both win.
    ///
    /// A claim lasts for the session even if the aquarium then refuses the fish. This answers "have I
    /// already processed this capture", not "is this fish currently alive" - see docs/decisions.md D-003.
    /// </summary>
    public sealed class FishCaptureRegistry {
        private readonly ConcurrentDictionary<string, byte> claimed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        /// <summary> Ids seen since startup, or since the last Clear. </summary>
        public int Count {
            get { return claimed.Count; }
        }

        /// <summary>
        /// Claims an id for this session. Returns true the first time an id is seen and false every time
        /// after, which is the caller's signal to drop the payload without logging an error - a replayed
        /// duplicate is normal traffic, not a fault.
        ///
        /// An empty id is refused: it cannot be deduplicated meaningfully, and FishCaptureMessage.IsUsable
        /// has already rejected the payload by the time this would matter.
        /// </summary>
        public bool TryClaim(string captureId) {
            if (string.IsNullOrEmpty(captureId)) {
                return false;
            }

            return claimed.TryAdd(captureId, 0);
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
        }
    }
}
