using System;

using UnityEngine;

namespace ViitorCloud.FishAquarium.Fish {
    /// <summary>
    /// Uniform grid over the aquarium that answers "which fish are near this point" without touching every
    /// fish in the tank.
    ///
    /// The neighbour query used to be a straight sweep over the whole population, which is O(n squared)
    /// across a frame. That was affordable at the authored ceiling of thirty fish and is not at the two
    /// hundred the flocking milestone asks for. This is a broad phase only: it returns the occupants of
    /// every cell the query circle touches, and the caller does the exact distance test. Keeping the
    /// narrow phase out of here is what lets one query serve both the separation radius and the wider
    /// neighbour radius.
    ///
    /// Rebuilt from scratch every frame, because a fish moves every frame and incremental updates would
    /// cost more bookkeeping than the rebuild costs outright. After the first few frames it allocates
    /// nothing: the three arrays are grown to fit and then reused, which matters because this runs inside
    /// the frame budget of an installation meant to stay up for days.
    ///
    /// A counting sort puts the entries in cell order, so a cell's occupants are contiguous in memory and
    /// the query walks them without chasing pointers.
    /// </summary>
    public sealed class FishSpatialHash {
        /// <summary>
        /// Ceiling on the grid resolution. A large aquarium with a small neighbour radius would otherwise
        /// ask for a grid of millions of cells, and clearing it each frame would cost more than the sweep
        /// this class exists to replace. When the cap bites, the cell size grows instead.
        /// </summary>
        public const int MaxCellsPerAxis = 128;

        private int[] cellStart = new int[0];
        private int[] cellCursor = new int[0];
        private int[] entries = new int[0];

        private float cellSize = 1f;
        private float originX;
        private float originY;
        private int columns = 1;
        private int rows = 1;
        private int cellCount;
        private int entryCount;

        /// <summary> Fish currently indexed. </summary>
        public int Count {
            get { return entryCount; }
        }

        public int CellCount {
            get { return cellCount; }
        }

        /// <summary> Side length actually used, which is the requested size or larger if the axis cap bit. </summary>
        public float CellSize {
            get { return cellSize; }
        }

        /// <summary>
        /// Indexes the first count entries of positions. preferredCellSize should be the largest radius the
        /// caller will query with: smaller cells mean more of them to clear, larger cells mean more
        /// candidates to reject, and matching the query radius is the usual balance.
        /// </summary>
        public void Build(Vector2[] positions, int count, Rect area, float preferredCellSize) {
            entryCount = 0;

            if (positions == null || count <= 0) {
                ResetGrid();
                return;
            }

            entryCount = Mathf.Min(count, positions.Length);
            ConfigureGrid(area, preferredCellSize);
            EnsureCapacity(entryCount);

            Array.Clear(cellStart, 0, cellCount + 1);

            // Counting sort, offset by one so the prefix sum lands each cell's start in cellStart[cell].
            for (int i = 0; i < entryCount; i++) {
                cellStart[CellIndexOf(positions[i]) + 1]++;
            }

            for (int cell = 1; cell <= cellCount; cell++) {
                cellStart[cell] += cellStart[cell - 1];
            }

            Array.Copy(cellStart, cellCursor, cellCount);

            for (int i = 0; i < entryCount; i++) {
                int cell = CellIndexOf(positions[i]);
                entries[cellCursor[cell]] = i;
                cellCursor[cell]++;
            }
        }

        /// <summary>
        /// Writes the indices of every fish in the cells the query circle touches into results, and returns
        /// how many were written.
        ///
        /// These are candidates, not confirmed neighbours: a cell overlaps the circle's bounding box, so
        /// some of what comes back is outside the radius and the caller must measure. Writing stops when
        /// results is full, which deliberately bounds the per-fish cost when the whole shoal piles into one
        /// corner - the frame budget matters more there than a perfectly complete neighbour list.
        /// </summary>
        public int Query(Vector2 position, float radius, int[] results) {
            if (results == null || results.Length == 0 || entryCount == 0) {
                return 0;
            }

            float clampedRadius = Mathf.Max(0f, radius);

            int minColumn = ColumnOf(position.x - clampedRadius);
            int maxColumn = ColumnOf(position.x + clampedRadius);
            int minRow = RowOf(position.y - clampedRadius);
            int maxRow = RowOf(position.y + clampedRadius);

            int found = 0;

            for (int row = minRow; row <= maxRow; row++) {
                int rowOffset = row * columns;

                for (int column = minColumn; column <= maxColumn; column++) {
                    int cell = rowOffset + column;
                    int start = cellStart[cell];
                    int end = cellStart[cell + 1];

                    for (int i = start; i < end; i++) {
                        results[found] = entries[i];
                        found++;

                        if (found == results.Length) {
                            return found;
                        }
                    }
                }
            }

            return found;
        }

        public void Clear() {
            entryCount = 0;

            if (cellStart.Length > 0) {
                Array.Clear(cellStart, 0, cellStart.Length);
            }
        }

        /// <summary>
        /// Sizes the grid to the aquarium. The requested cell size is honoured unless it would need more
        /// than MaxCellsPerAxis columns or rows, in which case the cells grow to cover the area with the
        /// cap's worth of them - a coarser grid returns more candidates but never misses one.
        /// </summary>
        private void ConfigureGrid(Rect area, float preferredCellSize) {
            float requested = Mathf.Max(0.01f, preferredCellSize);
            float width = Mathf.Max(0.01f, area.width);
            float height = Mathf.Max(0.01f, area.height);

            columns = Mathf.Clamp(Mathf.CeilToInt(width / requested), 1, MaxCellsPerAxis);
            rows = Mathf.Clamp(Mathf.CeilToInt(height / requested), 1, MaxCellsPerAxis);

            // Never smaller than the area divided by the cell counts, or the far edge would fall outside
            // the grid and the fish standing there would be invisible to every query.
            cellSize = Mathf.Max(requested, Mathf.Max(width / columns, height / rows));

            originX = area.xMin;
            originY = area.yMin;
            cellCount = columns * rows;
        }

        private void EnsureCapacity(int requiredEntries) {
            if (cellStart.Length < cellCount + 1) {
                cellStart = new int[cellCount + 1];
                cellCursor = new int[cellCount];
            }

            if (entries.Length < requiredEntries) {
                entries = new int[Mathf.NextPowerOfTwo(Mathf.Max(8, requiredEntries))];
            }
        }

        private void ResetGrid() {
            if (cellStart.Length < 2) {
                cellStart = new int[2];
                cellCursor = new int[1];
            }

            columns = 1;
            rows = 1;
            cellCount = 1;
            Array.Clear(cellStart, 0, 2);
        }

        private int CellIndexOf(Vector2 position) {
            return (RowOf(position.y) * columns) + ColumnOf(position.x);
        }

        /// <summary> Clamped, so a fish that has drifted outside the bounds lands in the nearest edge cell rather than out of range. </summary>
        private int ColumnOf(float x) {
            return Mathf.Clamp((int)((x - originX) / cellSize), 0, columns - 1);
        }

        private int RowOf(float y) {
            return Mathf.Clamp((int)((y - originY) / cellSize), 0, rows - 1);
        }
    }
}
