using Game.Core;

namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// What a sector holds, derived from the world seed and the sector's index at the moment it is
    /// asked for - not at world generation. There is nothing to pre-generate for the hundreds of
    /// sectors a run will never visit, and the map beyond the Core is unpopulated precisely because
    /// its contents were waiting to be asked for.
    ///
    /// The feature sits at the centre, so it always falls inside the disc a mission reveals. The
    /// deposits do not: they are scattered anywhere in the square, so some land in the disc and show
    /// up while others stay in the hidden corners. That gap is deliberate - the player sees there is
    /// something here, and that they have not seen all of it.
    /// </summary>
    public readonly struct SectorContents
    {
        public readonly SectorFeature Feature;

        /// <summary>The sector's middle cell - where <see cref="Feature"/> stands.</summary>
        public readonly GridCoord FeatureCell;

        /// <summary>Scattered across the whole square, corners included. Never null; empty when the sector has nothing.</summary>
        public readonly GridCoord[] DepositCells;

        public SectorContents(SectorFeature feature, GridCoord featureCell, GridCoord[] depositCells)
        {
            Feature = feature;
            FeatureCell = featureCell;
            DepositCells = depositCells ?? System.Array.Empty<GridCoord>();
        }
    }
}
