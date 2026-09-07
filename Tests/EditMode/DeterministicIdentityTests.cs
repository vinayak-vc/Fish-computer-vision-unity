using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Core;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The determinism guarantee, tested at its root.
    ///
    /// Section 6 of the interaction contract promises that the same drawing always produces the same
    /// creature, and section 9 extends that to drawings whose personality has to be derived here. Both rest
    /// on these two types. A regression in either would not crash anything and would not show up in play:
    /// it would quietly give returning visitors a different fish, which is the one failure the whole
    /// feature exists to prevent.
    ///
    /// The exact expected values below are pinned on purpose. They are the contract with the Python side,
    /// written out in docs/decisions.md D-002, and a change to them is a change to every archived fish.
    /// </summary>
    public sealed class DeterministicIdentityTests {
        private const string SampleId = "fish_20260901_182346_001";

        [Test]
        public void TheSameIdAlwaysHashesToTheSameValue() {
            Assert.AreEqual(StableHash.Of(SampleId), StableHash.Of(SampleId));
        }

        [Test]
        public void DifferentIdsHashDifferently() {
            Assert.AreNotEqual(StableHash.Of("fish_a"), StableHash.Of("fish_b"));
        }

        [Test]
        public void TheHashMatchesTheFnv1aValuesPythonWillReproduce() {
            // FNV-1a 64 over the UTF-8 bytes, verified against the reference vectors for the algorithm.
            // If these change, docs/decisions.md D-002 and the Python implementation must change with them.
            Assert.AreEqual(0xCBF29CE484222325UL, StableHash.Of(string.Empty), "An empty id must hash to the offset basis.");
            Assert.AreEqual(0xAF63DC4C8601EC8CUL, StableHash.Of("a"));
            Assert.AreEqual(0x85944171F73967E8UL, StableHash.Of("foobar"));
        }

        [Test]
        public void ANullIdIsHashedRatherThanThrowing() {
            Assert.AreEqual(0xCBF29CE484222325UL, StableHash.Of(null), "A malformed payload must still produce a usable creature.");
        }

        [Test]
        public void NonAsciiIdsAreHashedAsUtf8() {
            // The bytes must be UTF-8 so Python's id.encode('utf-8') agrees. This would differ under UTF-16.
            Assert.AreEqual(StableHash.Of("\u00e9"), StableHash.Of("\u00e9"));
            Assert.AreNotEqual(StableHash.Of("\u00e9"), StableHash.Of("e"));
        }

        [Test]
        public void TwoStreamsFromOneSeedProduceIdenticalSequences() {
            DeterministicRandom first = DeterministicRandom.FromIdentity(SampleId);
            DeterministicRandom second = DeterministicRandom.FromIdentity(SampleId);

            for (int i = 0; i < 32; i++) {
                Assert.AreEqual(first.NextUnit(), second.NextUnit(), "Draw " + i + " diverged.");
            }
        }

        [Test]
        public void AStreamAdvancesRatherThanRepeating() {
            DeterministicRandom stream = DeterministicRandom.FromIdentity(SampleId);

            float first = stream.NextUnit();
            float second = stream.NextUnit();

            Assert.AreNotEqual(first, second, "A stream that returned the same value twice would clone every trait.");
        }

        [Test]
        public void EveryDrawLiesInTheUnitInterval() {
            DeterministicRandom stream = DeterministicRandom.FromIdentity(SampleId);

            for (int i = 0; i < 2000; i++) {
                float value = stream.NextUnit();

                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f, "NextUnit is documented as [0, 1), and a trait of exactly 1 would clamp differently.");
            }
        }

        [Test]
        public void DrawsAreSpreadAcrossTheUnitInterval() {
            // Not a statistical test, just a guard against a stream that collapses into one corner - which
            // is what a broken mix step looks like, and which would make every fish behave identically.
            int[] buckets = new int[10];
            DeterministicRandom stream = DeterministicRandom.FromIdentity(SampleId);

            for (int i = 0; i < 10000; i++) {
                buckets[Mathf.Min(9, (int)(stream.NextUnit() * 10f))]++;
            }

            for (int i = 0; i < buckets.Length; i++) {
                Assert.Greater(buckets[i], 500, "Bucket " + i + " is nearly empty; the stream is not spreading.");
            }
        }

        [Test]
        public void DomainSeparatedStreamsDoNotTrackEachOther() {
            DeterministicRandom traits = DeterministicRandom.FromIdentity(SampleId);
            DeterministicRandom presentation = DeterministicRandom.FromIdentity(SampleId, "presentation");

            Assert.AreNotEqual(traits.NextUnit(), presentation.NextUnit(),
                "A fish whose swimming depth tracked its speed because both came off one draw would look like a bug.");
        }

        [Test]
        public void DomainSeparatedStreamsAreThemselvesStable() {
            DeterministicRandom first = DeterministicRandom.FromIdentity(SampleId, "presentation");
            DeterministicRandom second = DeterministicRandom.FromIdentity(SampleId, "presentation");

            Assert.AreEqual(first.NextUnit(), second.NextUnit());
        }

        [Test]
        public void DistinctIdsProduceDistinctFirstDraws() {
            HashSet<float> seen = new HashSet<float>();

            for (int i = 0; i < 500; i++) {
                DeterministicRandom stream = DeterministicRandom.FromIdentity("fish_" + i.ToString("D4"));
                seen.Add(stream.NextUnit());
            }

            Assert.AreEqual(500, seen.Count, "Five hundred drawings must not share a personality.");
        }

        [Test]
        public void RangeDrawsStayInsideTheirBounds() {
            DeterministicRandom stream = DeterministicRandom.FromIdentity(SampleId);

            for (int i = 0; i < 500; i++) {
                float value = stream.NextRange(-4f, 9f);

                Assert.GreaterOrEqual(value, -4f);
                Assert.Less(value, 9f);
            }
        }
    }
}
