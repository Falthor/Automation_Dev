using Game.Core;
using Game.Data;
using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// Where a sector's contents come from.
    ///
    /// <b>Nothing is stored.</b> There is no list of sectors, no dictionary, no cache: every answer
    /// is a pure function of the world seed and the sector's index, computed when asked. Lazy by
    /// construction rather than by bookkeeping - a run only ever asks about the sectors a wandering
    /// robot has actually opened.
    ///
    /// <b>The seed is the terrain's</b>, which is the only one a save restores
    /// (SaveData.TerrainSeed). WorldGenerator.ResourceSeed would have been the intuitive choice and
    /// is the wrong one: it is not persisted, so a loaded game would move every deposit it had not
    /// yet materialised. Same seed, same sector, same contents, whatever the order of discovery and
    /// whatever happened in between - which is what a test pins.
    /// </summary>
    public sealed class SectorCatalog
    {
        // Distinct salts so a sector's feature, its deposit count and its ore are independent draws
        // rather than three views of the same number.
        const uint FeatureSalt = 0xC2B2AE35;
        const uint DepositSalt = 0x27D4EB2F;
        const uint ResourceSalt = 0x165667B1;

        // The patch's size and its growth are separate draws from its seed cell, so moving one does
        // not move the others.
        const uint ClusterSizeSalt = 0x7F4A7C15;
        const uint ClusterGrowthSalt = 0x2545F491;

        /// <summary>Iron, copper, coal - the three the world generator knows how to place. A count rather than an enum, because the catalog names no resource: it says "the second one", and the caller resolves it against its own definitions.</summary>
        public const int ResourceKindCount = 3;

        public SectorGrid Grid { get; }
        public int Seed { get; }

        /// <summary>How much ore a sector holds, and how that grows with distance - see OreClusterProfile.</summary>
        public OreClusterProfile Clusters { get; }

        /// <summary>
        /// What cluster size is measured from.
        ///
        /// <b>The catalog knows the Core again, and this time it is earning it.</b> It used to hold a
        /// core centre for a risk gradient nothing read, and that was deleted; a cluster that grows
        /// with distance genuinely needs it. Still a pure function of the world: the Core's position
        /// is fixed when the world is generated and restored with it.
        /// </summary>
        public Vector2 CoreCentreCells { get; }

        /// <summary>
        /// How many cells apart two grown deposits must stand - an ore deposit's own footprint (every
        /// OreDepositDefinition ships 2x2 today), never 1x1. <see cref="GrowCluster"/> used to step
        /// its random walk one cell at a time, which packed deposit origins edge to edge: each one's
        /// real footprint then overlapped its neighbour's, and SectorMaterialisation silently dropped
        /// every cell that landed on ground an earlier deposit's footprint had already claimed - a
        /// "ten-tile" cluster read as a handful of deposits jammed into each other. Handed in rather
        /// than read off an OreDepositDefinition here, for the same reason the cluster profile is: the
        /// catalog names no resource (SectorContents.ResourceIndex's own doc), and every shipped
        /// definition agreeing on 2x2 is a fact about the assets, not a reason for this type to know
        /// what one looks like.
        /// </summary>
        public Vector2Int DepositFootprintCells { get; }

        /// <summary>
        /// The seed, the Core's centre, the cluster profile and the deposit footprint are required and
        /// have no defaults, for the reason SectorGrid has none for its sector size: a default here
        /// would be a second copy of a setting, free to disagree with the asset.
        /// </summary>
        public SectorCatalog(SectorGrid grid, int seed, Vector2 coreCentreCells, OreClusterProfile clusters,
            Vector2Int depositFootprintCells)
        {
            Grid = grid;
            Seed = seed;
            CoreCentreCells = coreCentreCells;
            Clusters = clusters;
            DepositFootprintCells = new Vector2Int(Mathf.Max(1, depositFootprintCells.x), Mathf.Max(1, depositFootprintCells.y));
        }

        /// <summary>
        /// What the sector holds. Allocates the deposit array, so call it when a sector is
        /// discovered - not every frame, and not for sectors nobody has reached.
        /// </summary>
        public SectorContents ContentsOf(int index)
        {
            if (Grid == null || !Grid.ContainsIndex(index)) return new SectorContents(SectorFeature.None, new GridCoord(0, 0), null);

            GridCoord origin = Grid.OriginOf(index);

            // The sector's real extent, which is not always its full size: 300 is not a whole number
            // of 16s, so the sectors along two edges of the map are clipped. Scattering across the
            // nominal square instead would drop contents outside the map entirely - and it would only
            // show up on a map size that does not divide evenly, which the previous sector size did.
            int width = Mathf.Min(Grid.SectorSizeCells, Grid.MapSizeCells - origin.X);
            int height = Mathf.Min(Grid.SectorSizeCells, Grid.MapSizeCells - origin.Y);
            if (width <= 0 || height <= 0) return new SectorContents(SectorFeature.None, origin, null);

            var featureCell = new GridCoord(origin.X + width / 2, origin.Y + height / 2);

            // Most sectors hold nothing, and the setting says exactly how often one does: one in
            // OreClusterOneSectorIn, with no factor hidden in here to reason around. SectorFeature
            // has no member for a wreck or a nest any more - a wreck is placed on its own rings
            // around the Core (Game.Gameplay.Wrecks.WreckField), independent of the sector grid.
            uint featureDraw = Hash(Seed, index, FeatureSalt) % (uint)Clusters.OneSectorIn;
            SectorFeature feature = featureDraw == 0 ? SectorFeature.OreCluster : SectorFeature.None;

            GridCoord[] deposits = feature == SectorFeature.OreCluster
                ? GrowCluster(index, origin, width, height)
                : System.Array.Empty<GridCoord>();

            // One ore for the whole sector, drawn once. A mixture would make every destination
            // interchangeable - see SectorContents.ResourceIndex.
            int resource = (int)(Hash(Seed, index, ResourceSalt) % (uint)ResourceKindCount);

            return new SectorContents(feature, featureCell, deposits, resource);
        }

        /// <summary>
        /// Grows one contiguous patch of ore inside the sector.
        ///
        /// <b>Its size comes from how far out it is</b> (see OreClusterProfile), which is what makes
        /// walking further worth doing.
        ///
        /// <b>Contiguous is the point, and it is what the old derivation got wrong.</b> Independent
        /// draws put two to five cells anywhere in a 256-cell square, which reads as litter: nothing
        /// to aim an Extractor at, nothing that looks like a deposit, and multiplied over every
        /// sector a robot crosses it turns the map into scenery. A patch of six to ten touching cells
        /// is a find.
        ///
        /// Grown rather than stamped, so no two look alike: a cell already in the patch is picked, a
        /// direction is drawn, and the neighbour joins if it is free and still inside the sector. Each
        /// step moves a whole <see cref="DepositFootprintCells"/>, not one cell - two deposit origins
        /// packed any closer would have their real footprints overlap, which is exactly the bug this
        /// spacing exists to avoid (see the field's own doc). "Touching" therefore means adjacent at
        /// that spacing: a ten-deposit patch reads as a field of ten side-by-side 2x2 plots, not ten
        /// deposits stacked on ~three. The attempt budget bounds it - a seed in a corner runs out of
        /// room in two directions and the patch simply ends smaller, which is truthful about the
        /// sector's edge rather than pushed back inside it.
        ///
        /// Deterministic throughout: every draw is a <see cref="DeterministicHash"/> of the seed, the
        /// sector and the step, so the same sector grows the same patch in every session and after
        /// every reload.
        /// </summary>
        GridCoord[] GrowCluster(int index, GridCoord origin, int width, int height)
        {
            // The band widens with distance - see OreClusterProfile. Measured to the sector's
            // centre, so every cell of one cluster is drawn from the same band rather than the patch
            // changing size as it grows across a threshold.
            float distance = Vector2.Distance(Grid.CenterCells(index), CoreCentreCells);
            int floor = Clusters.MinTilesAt(distance);
            int ceiling = Clusters.MaxTilesAt(distance);

            int wanted = floor + (int)(Hash(Seed, index, ClusterSizeSalt) % (uint)(ceiling - floor + 1));

            int stepX = DepositFootprintCells.x;
            int stepY = DepositFootprintCells.y;

            // The whole square, corners included - a patch that straddles the inscribed disc is what
            // makes coming back over the same ground at another angle worth something. Confined so a
            // full footprint from the seed itself still lands inside the sector, the same margin the
            // walk below keeps for every cell it adds.
            int seedRangeX = Mathf.Max(1, width - stepX + 1);
            int seedRangeY = Mathf.Max(1, height - stepY + 1);
            uint seedDraw = Hash(Seed, index, DepositSalt);
            var cells = new GridCoord[wanted];
            cells[0] = new GridCoord(
                origin.X + (int)(seedDraw % (uint)seedRangeX),
                origin.Y + (int)(seedDraw / 65536u % (uint)seedRangeY));

            int count = 1;
            int maxX = origin.X + width - stepX + 1;
            int maxY = origin.Y + height - stepY + 1;

            for (int attempt = 0; count < wanted && attempt < wanted * 8; attempt++)
            {
                uint draw = Hash(Seed, index, ClusterGrowthSalt + (uint)(attempt + 1) * 0x9E3779B9u);

                GridCoord from = cells[(int)(draw % (uint)count)];
                int direction = (int)(draw / 4096u % 4u);

                var candidate = new GridCoord(
                    from.X + (direction == 0 ? stepX : direction == 1 ? -stepX : 0),
                    from.Y + (direction == 2 ? stepY : direction == 3 ? -stepY : 0));

                if (candidate.X < origin.X || candidate.X >= maxX) continue;
                if (candidate.Y < origin.Y || candidate.Y >= maxY) continue;

                bool already = false;
                for (int i = 0; i < count; i++)
                {
                    if (cells[i] == candidate) { already = true; break; }
                }

                if (!already) cells[count++] = candidate;
            }

            if (count == wanted) return cells;

            // Ran out of room. Hand back what grew rather than a half-empty array.
            var trimmed = new GridCoord[count];
            System.Array.Copy(cells, trimmed, count);
            return trimmed;
        }

        /// <summary>
        /// The shared runtime-stable mixer (Game.Core). It used to be written out here; it moved when
        /// the terrain came to need exactly the same guarantee, and moved rather than being copied
        /// for the reason a hash is always worth sharing - two copies are two things that can drift,
        /// and a drifting hash silently recomposes a world under buildings that were saved.
        ///
        /// The arithmetic is unchanged, and a test pins it: moving it must not have moved a single
        /// deposit.
        /// </summary>
        static uint Hash(int seed, int index, uint salt) => DeterministicHash.Mix(seed, index, salt);
    }
}
