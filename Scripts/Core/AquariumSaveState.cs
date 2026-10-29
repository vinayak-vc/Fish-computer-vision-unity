using System;
using System.Collections.Generic;

namespace ViitorCloud.FishAquarium.Core {
    /// <summary>
    /// One fish as it is written to disk. Everything needed to rebuild the creature without Python:
    /// its identity, the personality that was assigned to it, and the life it has had since.
    ///
    /// Traits are saved rather than re-derived. Python owns the birth certificate but only keeps the
    /// recent replay window, so by the time an installation has been running a week most of its fish
    /// have no live source for their personality. Saving them is also what makes a restart silent - the
    /// deterministic fallback would produce a different creature for any fish that arrived with a real
    /// personality block.
    ///
    /// Public fields with plain names, because this is a serialisation shape and LitJson maps by member
    /// name. Nothing outside the persistence layer reads it.
    /// </summary>
    [Serializable]
    public sealed class FishSaveRecord {
        public string captureId;

        public float speed;
        public float curiosity;
        public float fear;
        public float social;
        public float aggression;
        public float grace;
        public float preferredDepth;
        public float rarityScore;

        /// <summary> Stored as the enum's integer value: the names are ours, not the wire's. </summary>
        public int rarityTier;

        public int objectType;
        public float objectConfidence;

        /// <summary> Whether the personality came from Python or from the identity hash. Preserved so a restore is faithful. </summary>
        public int traitSource;

        /// <summary> Mask area Python measured. Without it a restored fish would come back a different size. </summary>
        public int foregroundArea;

        public float positionX;
        public float positionY;

        public float affection;
        public float ageSeconds;
        public int foodEaten;
    }

    /// <summary>
    /// Version of the save format.
    ///
    /// Kept in its own static class rather than as a constant on AquariumSaveState, because LitJson
    /// reflects over a type's public fields without filtering out static ones - so a public const sitting
    /// on the serialised type is picked up as a member to write and throws "cannot set a constant field"
    /// on every load. A static class is never mapped, so this is invisible to the serialiser.
    /// </summary>
    public static class AquariumSaveVersion {
        /// <summary> Bumped when the shape below changes incompatibly. A file from the future is ignored rather than misread. </summary>
        public const int Current = 1;
    }

    /// <summary>
    /// The whole saved aquarium. Contract section 1: Unity owns the life, so this file is the authority
    /// on everything except the artwork and the birth certificate.
    /// </summary>
    [Serializable]
    public sealed class AquariumSaveState {
        public int version = AquariumSaveVersion.Current;

        public List<FishSaveRecord> fish = new List<FishSaveRecord>();

        /// <summary>
        /// Capture ids already processed, oldest first. Without these a restarted aquarium restores its
        /// fish from disk and then admits Python's whole replay window as brand new arrivals, duplicating
        /// the tank on every restart - the exact failure M0 exists to prevent.
        /// </summary>
        public List<string> knownCaptureIds = new List<string>();

        /// <summary> Date the "created today" tally belongs to, as yyyy-MM-dd. Discarded if it is not today. </summary>
        public string capturesTodayDate = string.Empty;

        public int capturesTodayCount;
        public int latestSequence;
    }
}
