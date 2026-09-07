using System.Collections.Generic;
using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// Which sectors a mission may be sent to: the ring just beyond what the Core already reaches.
    ///
    /// <b>Always measured from the current radius</b>, never from an absolute distance. The radius
    /// is passed in on every call and nothing here remembers it, so extending the Core's reach moves
    /// the ring outward with no other number to correct - which is what a test pins. The 30 cells
    /// are a settable property for the same reason, not a constant buried in a comparison.
    ///
    /// Membership is tested on the sector's <b>centre</b>, not its cells. A 12-cell sector straddles
    /// the boundary constantly, and a per-cell rule would make eligibility both fuzzy ("how much of
    /// it has to be inside?") and expensive.
    /// </summary>
    public sealed class SectorMissionRange
    {
        /// <summary>How far past the Core's current radius a destination may lie, in cells.</summary>
        public const float DefaultRangeBeyondRadiusCells = 30f;

        /// <summary>
        /// Below this many destinations the ring widens. The directive's own risk: once every sector
        /// in the ring has been visited and the radius has not grown, there is nothing to offer and
        /// the mission system goes quiet with no explanation. Widening is the parade chosen over
        /// waiting for the player to research their way out of it.
        /// </summary>
        public const int DefaultMinimumDestinations = 3;

        public float RangeBeyondRadiusCells { get; set; } = DefaultRangeBeyondRadiusCells;

        public int MinimumDestinations { get; set; } = DefaultMinimumDestinations;

        /// <summary>
        /// Fills <paramref name="into"/> with every undiscovered sector in the ring, widening it in
        /// steps until it holds at least <see cref="MinimumDestinations"/> or the map is used up.
        ///
        /// A sector already discovered is not a destination: a mission there would reveal a disc
        /// that is already revealed. That is also what lets the ring run out, hence the widening.
        ///
        /// The caller owns the list, so a repeated query allocates nothing.
        /// </summary>
        public SectorRangeResult Destinations(
            SectorGrid grid,
            DiscoveryRuntime discovery,
            Vector2 coreCenterCells,
            float coreRadiusCells,
            List<int> into)
        {
            into?.Clear();

            if (grid == null || grid.Count == 0) return new SectorRangeResult(0, 0f, false, true);

            float step = Mathf.Max(1f, RangeBeyondRadiusCells);
            float outer = Mathf.Max(0f, coreRadiusCells) + step;

            // Past this, every sector centre on the map is inside the ring and widening again would
            // change nothing - which is what tells the caller the map itself is exhausted, not that
            // the ring was badly sized.
            float mapSpan = grid.MapSizeCells;
            float furthest = Vector2.Distance(coreCenterCells, Vector2.zero);
            furthest = Mathf.Max(furthest, Vector2.Distance(coreCenterCells, new Vector2(mapSpan, 0f)));
            furthest = Mathf.Max(furthest, Vector2.Distance(coreCenterCells, new Vector2(0f, mapSpan)));
            furthest = Mathf.Max(furthest, Vector2.Distance(coreCenterCells, new Vector2(mapSpan, mapSpan)));

            bool widened = false;

            while (true)
            {
                int found = Collect(grid, discovery, coreCenterCells, coreRadiusCells, outer, into);

                if (found >= MinimumDestinations) return new SectorRangeResult(found, outer, widened, false);
                if (outer >= furthest) return new SectorRangeResult(found, outer, widened, true);

                outer += step;
                widened = true;
            }
        }

        /// <summary>
        /// Sectors whose centre falls in (inner, outer] and that are wholly unknown. Walks only the
        /// sector rows and columns the outer radius can touch, so the cost follows the ring rather
        /// than the size of the map.
        /// </summary>
        static int Collect(
            SectorGrid grid,
            DiscoveryRuntime discovery,
            Vector2 coreCenterCells,
            float inner,
            float outer,
            List<int> into)
        {
            into?.Clear();

            int size = grid.SectorSizeCells;
            int minColumn = Mathf.Max(0, Mathf.FloorToInt((coreCenterCells.x - outer) / size));
            int maxColumn = Mathf.Min(grid.Columns - 1, Mathf.CeilToInt((coreCenterCells.x + outer) / size));
            int minRow = Mathf.Max(0, Mathf.FloorToInt((coreCenterCells.y - outer) / size));
            int maxRow = Mathf.Min(grid.Columns - 1, Mathf.CeilToInt((coreCenterCells.y + outer) / size));

            float innerSquared = inner * inner;
            float outerSquared = outer * outer;
            int found = 0;

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int column = minColumn; column <= maxColumn; column++)
                {
                    int index = grid.IndexAt(column, row);
                    if (index < 0) continue;

                    Vector2 offset = grid.CenterCells(index) - coreCenterCells;
                    float distanceSquared = offset.sqrMagnitude;

                    if (distanceSquared <= innerSquared || distanceSquared > outerSquared) continue;
                    if (!grid.IsWhollyUnknown(index, discovery)) continue;

                    into?.Add(index);
                    found++;
                }
            }

            return found;
        }
    }

    /// <summary>
    /// What a range query actually found. Reported rather than left implicit so the mission system
    /// can never go silently empty: <see cref="Widened"/> says the ring had to grow past its
    /// setting, and <see cref="Exhausted"/> says the map itself has no unknown sector left.
    /// </summary>
    public readonly struct SectorRangeResult
    {
        public readonly int Count;
        public readonly float OuterRadiusCells;
        public readonly bool Widened;
        public readonly bool Exhausted;

        public SectorRangeResult(int count, float outerRadiusCells, bool widened, bool exhausted)
        {
            Count = count;
            OuterRadiusCells = outerRadiusCells;
            Widened = widened;
            Exhausted = exhausted;
        }
    }
}
