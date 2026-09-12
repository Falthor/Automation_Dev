using Game.Core;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Absolute-cell math for the single-cell footprint shared by Splitter and Crossroad. The shape
    /// is rotationally symmetric, so these never change with FacingRotation - only which side plays
    /// which role (entry/exit) does, on the runtime types themselves.
    ///
    /// <b>Kept as a named pair although both are now one line.</b> Every side both runtimes and both
    /// of TransportSystem's dedicated steps address goes through here, so how a cross piece reaches
    /// its neighbours is stated once rather than as an offset repeated at a dozen call sites - which
    /// is exactly what made moving from the old "+" footprint to a single cell a two-line change.
    /// </summary>
    public static class CrossFootprint
    {
        /// <summary>The piece's own cell, whichever side is asked: it occupies one.</summary>
        public static GridCoord ArmCell(GridCoord origin, Direction direction) => origin;

        /// <summary>The cell one step beyond - where a neighbour must sit to count as touching this side.</summary>
        public static GridCoord NeighborCell(GridCoord origin, Direction direction) => ArmCell(origin, direction) + direction.ToOffset();
    }
}
