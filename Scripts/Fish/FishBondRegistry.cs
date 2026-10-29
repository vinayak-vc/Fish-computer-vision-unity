using System.Collections.Generic;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Who is getting fond of whom. Design points 5 and 8: fish that spend sustained time near each other
    /// form a pair, and a pair eventually produces an egg.
    ///
    /// Familiarity is tracked per pair of fish, but only for pairs that have actually been seen close
    /// together. A tank of two hundred fish has twenty thousand possible pairs and almost all of them will
    /// never matter, so the table is grown lazily and pruned as familiarity decays back to nothing.
    ///
    /// A fish belongs to at most one pair. "A pair" is the design's word, and allowing a fish two partners
    /// would need a policy for which one it lays with - a complication with nothing to show for it.
    ///
    /// Plain class, clock-free, no Unity objects: the whole courtship can be tested in a loop.
    /// </summary>
    public sealed class FishBondRegistry {
        /// <summary> One fish's side of a bond. </summary>
        private struct Bond {
            public float Familiarity { get; set; }
        }

        private readonly Dictionary<long, Bond> bonds = new Dictionary<long, Bond>();
        private readonly Dictionary<int, int> partners = new Dictionary<int, int>();
        private readonly Dictionary<long, float> broodProgress = new Dictionary<long, float>();
        private readonly Dictionary<long, int> broodCounts = new Dictionary<long, int>();
        private readonly List<long> expired = new List<long>();

        private readonly float familiarityToPair;
        private readonly float familiarityDecayPerSecond;
        private readonly float broodSeconds;

        public FishBondRegistry(float secondsTogetherToPair, float decayPerSecond, float secondsPerBrood) {
            familiarityToPair = Mathf.Max(0.1f, secondsTogetherToPair);
            familiarityDecayPerSecond = Mathf.Max(0f, decayPerSecond);
            broodSeconds = Mathf.Max(0.1f, secondsPerBrood);
        }

        /// <summary> Pairs currently formed. </summary>
        public int PairCount {
            get { return partners.Count / 2; }
        }

        /// <summary> Pairs of fish that have been near each other recently enough to still be remembered. </summary>
        public int TrackedBondCount {
            get { return bonds.Count; }
        }

        /// <summary>
        /// Records that two fish are keeping company. Called once per frame per fish for its nearest
        /// neighbour, so a mutual pairing accrues familiarity twice as fast as a one-sided interest - which
        /// is the behaviour wanted: two fish that have chosen each other bond quicker than one that is
        /// merely following another around.
        /// </summary>
        public void RecordProximity(int firstFishId, int secondFishId, float deltaTime) {
            if (firstFishId == secondFishId || firstFishId <= 0 || secondFishId <= 0) {
                return;
            }

            long key = BuildKey(firstFishId, secondFishId);

            Bond bond;
            bonds.TryGetValue(key, out bond);
            bond.Familiarity += deltaTime;
            bonds[key] = bond;

            if (bond.Familiarity < familiarityToPair) {
                return;
            }

            TryFormPair(firstFishId, secondFishId, key);
        }

        /// <summary>
        /// Decays every bond that was not reinforced this frame and advances the broods of formed pairs.
        /// Ids of pairs ready to lay are written to readyToLay.
        /// </summary>
        public void Tick(float deltaTime, List<long> readyToLay) {
            if (readyToLay != null) {
                readyToLay.Clear();
            }

            DecayBonds(deltaTime);
            AdvanceBroods(deltaTime, readyToLay);
        }

        /// <summary> The partner of a fish, or 0 when it has none. </summary>
        public int GetPartner(int fishId) {
            int partner;
            return partners.TryGetValue(fishId, out partner) ? partner : 0;
        }

        public bool IsPaired(int fishId) {
            return partners.ContainsKey(fishId);
        }

        /// <summary> Familiarity between two fish, in seconds of company. Zero when they are strangers. </summary>
        public float GetFamiliarity(int firstFishId, int secondFishId) {
            Bond bond;
            return bonds.TryGetValue(BuildKey(firstFishId, secondFishId), out bond) ? bond.Familiarity : 0f;
        }

        /// <summary> How far along a pair's current brood is, 0..1. </summary>
        public float GetBroodProgress(int firstFishId, int secondFishId) {
            float progress;
            return broodProgress.TryGetValue(BuildKey(firstFishId, secondFishId), out progress) ? Mathf.Clamp01(progress / broodSeconds) : 0f;
        }

        /// <summary> Broods a pair has already produced. Feeds the blend, so successive babies differ. </summary>
        public int GetBroodCount(long pairKey) {
            int count;
            return broodCounts.TryGetValue(pairKey, out count) ? count : 0;
        }

        public void UnpackKey(long pairKey, out int firstFishId, out int secondFishId) {
            firstFishId = (int)(pairKey >> 32);
            secondFishId = (int)(pairKey & 0xFFFFFFFFL);
        }

        /// <summary> Called once a pair has laid, so the next brood starts from nothing. </summary>
        public void ConsumeBrood(long pairKey) {
            broodProgress[pairKey] = 0f;

            int count;
            broodCounts.TryGetValue(pairKey, out count);
            broodCounts[pairKey] = count + 1;
        }

        /// <summary> Forgets a fish entirely. Called when one is removed, or its partner would wait forever. </summary>
        public void Forget(int fishId) {
            int partner;
            if (partners.TryGetValue(fishId, out partner)) {
                partners.Remove(fishId);
                partners.Remove(partner);
                broodProgress.Remove(BuildKey(fishId, partner));
            }

            expired.Clear();

            foreach (KeyValuePair<long, Bond> pair in bonds) {
                int first, second;
                UnpackKey(pair.Key, out first, out second);

                if (first == fishId || second == fishId) {
                    expired.Add(pair.Key);
                }
            }

            for (int i = 0; i < expired.Count; i++) {
                bonds.Remove(expired[i]);
            }

            expired.Clear();
        }

        public void Clear() {
            bonds.Clear();
            partners.Clear();
            broodProgress.Clear();
            broodCounts.Clear();
        }

        private void TryFormPair(int firstFishId, int secondFishId, long key) {
            // Either already spoken for? Then this familiarity goes no further. A fish keeps one partner.
            if (partners.ContainsKey(firstFishId) || partners.ContainsKey(secondFishId)) {
                return;
            }

            partners[firstFishId] = secondFishId;
            partners[secondFishId] = firstFishId;

            if (!broodProgress.ContainsKey(key)) {
                broodProgress[key] = 0f;
            }
        }

        /// <summary>
        /// Familiarity fades for every pair, every frame. RecordProximity adds more than this removes for
        /// fish that are actually together, so company accrues and absence erodes - which is what makes
        /// "sustained time near each other" mean sustained rather than cumulative.
        /// </summary>
        private void DecayBonds(float deltaTime) {
            if (bonds.Count == 0 || familiarityDecayPerSecond <= 0f) {
                return;
            }

            expired.Clear();
            float decay = familiarityDecayPerSecond * deltaTime;

            // Keys are collected first: a Dictionary cannot be written while it is being enumerated.
            foreach (KeyValuePair<long, Bond> pair in bonds) {
                expired.Add(pair.Key);
            }

            for (int i = 0; i < expired.Count; i++) {
                long key = expired[i];
                Bond bond = bonds[key];
                bond.Familiarity -= decay;

                if (bond.Familiarity <= 0f) {
                    int first, second;
                    UnpackKey(key, out first, out second);

                    // A formed pair does not un-form just because they drifted apart for a moment. Design
                    // point 5 is about a bond, and a bond that evaporated the first time two fish were
                    // separated by a passing predator would never produce anything.
                    if (partners.ContainsKey(first) && partners[first] == second) {
                        bond.Familiarity = 0f;
                        bonds[key] = bond;
                        continue;
                    }

                    bonds.Remove(key);
                    continue;
                }

                bonds[key] = bond;
            }

            expired.Clear();
        }

        private void AdvanceBroods(float deltaTime, List<long> readyToLay) {
            if (broodProgress.Count == 0) {
                return;
            }

            expired.Clear();

            foreach (KeyValuePair<long, float> pair in broodProgress) {
                expired.Add(pair.Key);
            }

            for (int i = 0; i < expired.Count; i++) {
                long key = expired[i];
                int first, second;
                UnpackKey(key, out first, out second);

                // A pair whose partner has gone stops brooding rather than laying on its own.
                if (!partners.ContainsKey(first) || partners[first] != second) {
                    broodProgress.Remove(key);
                    continue;
                }

                float progress = broodProgress[key] + deltaTime;

                if (progress >= broodSeconds) {
                    if (readyToLay != null) {
                        readyToLay.Add(key);
                    }

                    // Left at the threshold rather than reset here: the caller may refuse the egg because
                    // the tank is full, and resetting would make the pair start their wait over.
                    progress = broodSeconds;
                }

                broodProgress[key] = progress;
            }

            expired.Clear();
        }

        /// <summary>
        /// One key for an unordered pair, so (3, 7) and (7, 3) are the same bond. The smaller id goes in
        /// the high half, which makes the packing canonical.
        /// </summary>
        private static long BuildKey(int firstFishId, int secondFishId) {
            int low = Mathf.Min(firstFishId, secondFishId);
            int high = Mathf.Max(firstFishId, secondFishId);
            return ((long)low << 32) | (uint)high;
        }
    }
}
