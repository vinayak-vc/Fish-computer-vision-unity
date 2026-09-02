using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using ViitorCloud.FishAquarium.Fish;

namespace ViitorCloud.FishAquarium.Tests {
    /// <summary>
    /// The neighbour index that replaced the O(n squared) sweep.
    ///
    /// The failure this guards against is quiet: a grid that drops a fish does not throw, it just means
    /// that fish is invisible to everyone near it. On screen that reads as one creature occasionally
    /// swimming through a school without reacting, which nobody would think to file as an indexing bug.
    /// So the tests below check completeness as hard as they check the fast path.
    /// </summary>
    public sealed class FishSpatialHashTests {
        private static readonly Rect Tank = new Rect(-10f, -5f, 20f, 10f);

        private static Vector2[] BuildGrid(int columns, int rows) {
            Vector2[] positions = new Vector2[columns * rows];

            for (int row = 0; row < rows; row++) {
                for (int column = 0; column < columns; column++) {
                    positions[(row * columns) + column] = new Vector2(
                        Mathf.Lerp(Tank.xMin + 0.5f, Tank.xMax - 0.5f, column / (float)Mathf.Max(1, columns - 1)),
                        Mathf.Lerp(Tank.yMin + 0.5f, Tank.yMax - 0.5f, row / (float)Mathf.Max(1, rows - 1)));
                }
            }

            return positions;
        }

        [Test]
        public void AnEmptyHashAnswersNothing() {
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(new Vector2[0], 0, Tank, 2f);

            Assert.AreEqual(0, hash.Count);
            Assert.AreEqual(0, hash.Query(Vector2.zero, 5f, new int[8]));
        }

        [Test]
        public void ANullPositionArrayIsSurvivable() {
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(null, 4, Tank, 2f);

            Assert.AreEqual(0, hash.Query(Vector2.zero, 5f, new int[8]));
        }

        [Test]
        public void AFishIsFoundAtItsOwnPosition() {
            FishSpatialHash hash = new FishSpatialHash();
            Vector2[] positions = { new Vector2(3f, 1f) };
            hash.Build(positions, 1, Tank, 2f);

            int[] results = new int[8];
            int found = hash.Query(new Vector2(3f, 1f), 1f, results);

            Assert.AreEqual(1, found);
            Assert.AreEqual(0, results[0]);
        }

        [Test]
        public void EveryFishIsIndexedExactlyOnce() {
            // The counting sort is the part most likely to lose or duplicate an entry, and a wide query
            // over the whole tank is the cheapest way to prove it did neither.
            Vector2[] positions = BuildGrid(9, 7);
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(positions, positions.Length, Tank, 2f);

            int[] results = new int[positions.Length * 2];
            int found = hash.Query(Tank.center, 100f, results);

            Assert.AreEqual(positions.Length, found, "The grid must return every fish, once.");

            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < found; i++) {
                Assert.IsTrue(seen.Add(results[i]), "Index " + results[i] + " was returned twice.");
            }
        }

        [Test]
        public void EveryFishFindsEveryTrueNeighbourThatABruteForceSweepWouldFind() {
            // The point of the class is to return the same answers as the sweep it replaced, only faster.
            Vector2[] positions = BuildGrid(11, 8);
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(positions, positions.Length, Tank, 2.5f);

            int[] results = new int[positions.Length * 2];

            for (int i = 0; i < positions.Length; i++) {
                int found = hash.Query(positions[i], 2.5f, results);

                HashSet<int> candidates = new HashSet<int>();
                for (int r = 0; r < found; r++) {
                    candidates.Add(results[r]);
                }

                for (int j = 0; j < positions.Length; j++) {
                    if (Vector2.Distance(positions[i], positions[j]) > 2.5f) {
                        continue;
                    }

                    Assert.IsTrue(candidates.Contains(j), "Fish " + i + " never saw its neighbour " + j + ".");
                }
            }
        }

        [Test]
        public void DistantFishAreNotReturned() {
            Vector2[] positions = { new Vector2(-9f, -4f), new Vector2(9f, 4f) };
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(positions, 2, Tank, 1f);

            int[] results = new int[8];
            int found = hash.Query(new Vector2(-9f, -4f), 1f, results);

            Assert.AreEqual(1, found, "A fish at the far corner must not be a candidate.");
            Assert.AreEqual(0, results[0]);
        }

        [Test]
        public void QueryResultsAreCappedByTheBufferRatherThanOverflowing() {
            Vector2[] positions = new Vector2[64];
            for (int i = 0; i < positions.Length; i++) {
                positions[i] = new Vector2(0.01f * i, 0f);
            }

            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(positions, positions.Length, Tank, 2f);

            int[] small = new int[6];
            int found = hash.Query(Vector2.zero, 5f, small);

            Assert.AreEqual(6, found, "The cap bounds the per-fish cost when the whole shoal clumps.");
        }

        [Test]
        public void AFishOutsideTheAreaLandsInTheNearestCellRatherThanVanishing() {
            // Fish can sit briefly outside the bounds while entering, or while fleeing the pointer.
            Vector2[] positions = { new Vector2(-40f, -30f) };
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(positions, 1, Tank, 2f);

            int[] results = new int[8];

            Assert.AreEqual(1, hash.Query(new Vector2(Tank.xMin, Tank.yMin), 1f, results));
            Assert.AreEqual(0, results[0]);
        }

        [Test]
        public void RebuildingReplacesThePreviousContents() {
            FishSpatialHash hash = new FishSpatialHash();
            int[] results = new int[8];

            hash.Build(new[] { new Vector2(-8f, -4f) }, 1, Tank, 2f);
            hash.Build(new[] { new Vector2(8f, 4f) }, 1, Tank, 2f);

            Assert.AreEqual(1, hash.Count);
            Assert.AreEqual(0, hash.Query(new Vector2(-8f, -4f), 1f, results), "A stale entry would haunt the old cell forever.");
            Assert.AreEqual(1, hash.Query(new Vector2(8f, 4f), 1f, results));
        }

        [Test]
        public void ClearingEmptiesTheHash() {
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(new[] { new Vector2(1f, 1f) }, 1, Tank, 2f);
            hash.Clear();

            Assert.AreEqual(0, hash.Count);
            Assert.AreEqual(0, hash.Query(new Vector2(1f, 1f), 2f, new int[8]));
        }

        [Test]
        public void CellsGrowRatherThanTheGridExplodingWhenTheAxisCapBites() {
            FishSpatialHash hash = new FishSpatialHash();
            Rect enormous = new Rect(0f, 0f, 100000f, 100000f);
            hash.Build(new[] { new Vector2(50f, 50f) }, 1, enormous, 0.001f);

            Assert.LessOrEqual(hash.CellCount, FishSpatialHash.MaxCellsPerAxis * FishSpatialHash.MaxCellsPerAxis);
            Assert.Greater(hash.CellSize, 0.001f, "Cells must widen instead, or clearing the grid would cost more than the sweep this replaced.");
            Assert.AreEqual(1, hash.Query(new Vector2(50f, 50f), 5f, new int[8]), "Coverage must survive the cap.");
        }

        [Test]
        public void ADegenerateAreaIsSurvivable() {
            FishSpatialHash hash = new FishSpatialHash();
            hash.Build(new[] { Vector2.zero }, 1, new Rect(0f, 0f, 0f, 0f), 1f);

            Assert.AreEqual(1, hash.Query(Vector2.zero, 1f, new int[4]));
        }
    }
}
