using System.Collections.Generic;
using Game.Gameplay.Missions;
using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.Sectors
{
    /// <summary>Why a sector is or is not a valid destination. Named rather than boolean so the UI can say what is wrong instead of only greying a sector out.</summary>
    public enum SectorEligibility
    {
        Eligible,

        /// <summary>Inside the Core's current radius for a prospection, or short of the exploration threshold for a far exploration.</summary>
        TooClose,

        /// <summary>Past the exploration threshold, so it belongs to the other reconnaissance.</summary>
        TooFar,

        /// <summary>Already discovered. A mission there would reveal a disc that is already revealed.</summary>
        AlreadyKnown,

        NotASector,

        /// <summary>Asked of a recovery. A recovery targets a point of interest inside ground already known, so no band applies to it - the caller decides its own rule rather than being given a wrong answer here.</summary>
        NotABandMission
    }

    /// <summary>
    /// Which sectors a mission may be sent to. Two bands, because there are two kinds of mission and
    /// they want opposite things.
    ///
    /// <b>Mining runs from the Core's current radius out to the exploration threshold</b>, and it
    /// narrows as the Core reaches further: ground already covered needs no mission to reach.
    ///
    /// <b>Exploration starts at the threshold and has no outer edge</b>, because what it is looking
    /// for - a site where a secondary Core could stand - only exists out there.
    ///
    /// <b>The threshold is derived, never entered.</b> It is what keeps two territories from touching:
    /// two maximum Core radii back to back, plus the gap wanted between them. Writing the resulting
    /// number in as a constant is what the directive forbids, and for a concrete reason - the day the
    /// maximum radius moves, the threshold has to follow with no second figure to correct.
    ///
    /// This replaces a single ring of "current radius + 30", which matched neither kind of mission: it
    /// sat entirely inside what is now the mining band and could never reach an exploration target at
    /// all. It also had to widen itself when it ran dry; both bands here are hundreds of cells deep or
    /// unbounded, so that machinery is gone with it.
    ///
    /// Membership is tested on the sector's <b>centre</b>, not its cells. A sector straddles a
    /// boundary constantly, and a per-cell rule would make eligibility both fuzzy ("how much of it has
    /// to be inside?") and expensive.
    /// </summary>
    public sealed class SectorMissionRange
    {
        /// <summary>The maximum radius one Core will ever reach, in cells. Handed in from its owner (CoreRuntime.ExtendedActionRadiusCells) rather than copied.</summary>
        public float MaxCoreRadiusCells { get; }

        /// <summary>The empty ground wanted between two Cores' maximum radii. A balance value: how far apart two territories should feel, which no other number can answer.</summary>
        public float TerritorySpacingCells { get; }

        /// <summary>
        /// Where mining stops and exploration starts: two maximum radii back to back, plus the gap.
        ///
        /// A derived value, so it is a property and never a field anyone can set. At the shipped
        /// maximum radius of 32 and a gap of 90 that is 154 cells; the day a Core reaches 80, it
        /// becomes 250 on its own.
        /// </summary>
        public float ExplorationMinimumCells => 2f * MaxCoreRadiusCells + TerritorySpacingCells;

        public SectorMissionRange(float maxCoreRadiusCells, float territorySpacingCells)
        {
            MaxCoreRadiusCells = Mathf.Max(0f, maxCoreRadiusCells);
            TerritorySpacingCells = Mathf.Max(0f, territorySpacingCells);
        }

        /// <summary>The band a kind of mission may be sent into, as (inner, outer] in cells from the Core. The outer edge of exploration is infinite rather than the map's corner, so the band does not change shape with the map.</summary>
        public void BandFor(MissionKind kind, float coreRadiusCells, out float inner, out float outer)
        {
            if (kind == MissionKind.Prospection)
            {
                inner = Mathf.Max(0f, coreRadiusCells);
                outer = ExplorationMinimumCells;
                return;
            }

            inner = ExplorationMinimumCells;
            outer = float.PositiveInfinity;
        }

        /// <summary>
        /// Whether one sector is a valid destination, and why not when it is not.
        ///
        /// This is the primary operation, not the enumeration below: a mission is launched by
        /// designating a sector on the map, so the question the game actually asks is about one
        /// sector. Enumerating every eligible sector would mean four hundred thousand of them for an
        /// exploration mission on the shipped map.
        /// </summary>
        public SectorEligibility EligibilityOf(
            MissionKind kind,
            SectorGrid grid,
            DiscoveryRuntime discovery,
            Vector2 coreCenterCells,
            float coreRadiusCells,
            int index)
        {
            if (kind == MissionKind.Recuperation) return SectorEligibility.NotABandMission;
            if (grid == null || !grid.ContainsIndex(index)) return SectorEligibility.NotASector;

            BandFor(kind, coreRadiusCells, out float inner, out float outer);

            float distance = Vector2.Distance(grid.CenterCells(index), coreCenterCells);
            if (distance <= inner) return SectorEligibility.TooClose;
            if (distance > outer) return SectorEligibility.TooFar;

            return grid.IsWhollyUnknown(index, discovery) ? SectorEligibility.Eligible : SectorEligibility.AlreadyKnown;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with eligible sectors, up to <paramref name="limit"/>.
        ///
        /// <b>Bounded, deliberately.</b> The old ring held a handful of sectors and could be listed
        /// whole; the exploration band holds most of a 390 625-sector map, and a caller that wants
        /// "somewhere to go" wants a few, not all of them. The caller owns the list, so a repeated
        /// query allocates nothing.
        /// </summary>
        public SectorRangeResult Destinations(
            MissionKind kind,
            SectorGrid grid,
            DiscoveryRuntime discovery,
            Vector2 coreCenterCells,
            float coreRadiusCells,
            List<int> into,
            int limit = 32)
        {
            into?.Clear();

            BandFor(kind, coreRadiusCells, out float inner, out float outer);
            if (grid == null || grid.Count == 0) return new SectorRangeResult(0, inner, outer, true);

            int size = grid.SectorSizeCells;

            // Only the sector rows and columns the band can touch, so a mining query costs the band
            // rather than the map. An exploration band reaches the whole map, and the limit is what
            // stops that walk early.
            float reach = float.IsPositiveInfinity(outer) ? grid.MapSizeCells * 2f : outer;
            int minColumn = Mathf.Max(0, Mathf.FloorToInt((coreCenterCells.x - reach) / size));
            int maxColumn = Mathf.Min(grid.Columns - 1, Mathf.CeilToInt((coreCenterCells.x + reach) / size));
            int minRow = Mathf.Max(0, Mathf.FloorToInt((coreCenterCells.y - reach) / size));
            int maxRow = Mathf.Min(grid.Columns - 1, Mathf.CeilToInt((coreCenterCells.y + reach) / size));

            float innerSquared = inner * inner;
            float outerSquared = float.IsPositiveInfinity(outer) ? float.PositiveInfinity : outer * outer;
            int found = 0;

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int column = minColumn; column <= maxColumn; column++)
                {
                    int index = grid.IndexAt(column, row);
                    if (index < 0) continue;

                    float distanceSquared = (grid.CenterCells(index) - coreCenterCells).sqrMagnitude;
                    if (distanceSquared <= innerSquared || distanceSquared > outerSquared) continue;
                    if (!grid.IsWhollyUnknown(index, discovery)) continue;

                    into?.Add(index);
                    found++;

                    if (found >= limit) return new SectorRangeResult(found, inner, outer, false);
                }
            }

            // Nothing found means the band itself has nothing left to offer - every sector in it is
            // known, or the geometry leaves it empty. Reported rather than left implicit, so the
            // mission system can say so instead of going quiet.
            return new SectorRangeResult(found, inner, outer, found == 0);
        }
    }

    /// <summary>What a range query found, and the band it looked in.</summary>
    public readonly struct SectorRangeResult
    {
        public readonly int Count;
        public readonly float InnerRadiusCells;
        public readonly float OuterRadiusCells;

        /// <summary>No eligible sector in the band at all - every one of them is known, or the band is geometrically empty. The one state a mission system must never reach silently.</summary>
        public readonly bool Exhausted;

        public SectorRangeResult(int count, float innerRadiusCells, float outerRadiusCells, bool exhausted)
        {
            Count = count;
            InnerRadiusCells = innerRadiusCells;
            OuterRadiusCells = outerRadiusCells;
            Exhausted = exhausted;
        }
    }
}
