using UnityEngine;

namespace Game.Gameplay.Sites
{
    /// <summary>
    /// How fast a construction site's front segment assembles once its material has arrived - and
    /// therefore when it starts working, since a segment is not operational until its assembly
    /// reaches 1 (ConstructionSiteRuntime.CanMaterializeNextSegment).
    ///
    /// That is why the speed lives here rather than with the look of the effect in
    /// NanoConstructionSettings: a gas power plant supplied current, pulled coal off its belt and
    /// burned it for the five seconds it spent visibly assembling, because "built" meant "its last
    /// plate landed" while the player was still watching it materialise. A number that decides when
    /// a building becomes real is not a rendering setting.
    ///
    /// A speed in footprint cells per second, not a duration: a 9-cell power plant takes nine times
    /// as long as a 1-cell belt rather than exactly as long, which is what keeps a long conveyor
    /// drag - whose segments assemble in series, in placement order - from being interminable.
    /// </summary>
    public static class SegmentAssembly
    {
        /// <summary>1.8 reproduces the value tuned by eye on the gas power plant: 0.2 progress/s over its 9 cells.</summary>
        public const float CellsPerSecond = 1.8f;

        /// <summary>Floor on how fast anything may assemble, so a small building cannot pop into existence in a single frame. Inert at the shipped speed - a 1-cell belt already takes 0.56 s - and here as a guard for future retuning.</summary>
        public const float MinDurationSeconds = 0.25f;

        /// <summary>
        /// Progress units per second for a building covering <paramref name="footprintCells"/> grid
        /// cells. The area is the building's <b>logical footprint</b>, never the visual AABB the
        /// dissolve shader's _BuildBounds carries: a sprite may deliberately overflow its footprint,
        /// and sizing the assembly speed off what is drawn would make an overhanging roof slow the
        /// building down.
        /// </summary>
        public static float RateFor(int footprintCells)
        {
            float cells = Mathf.Max(1, footprintCells);
            return Mathf.Min(CellsPerSecond / cells, 1f / MinDurationSeconds);
        }
    }
}
