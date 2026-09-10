using Game.Core;
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

        /// <summary>Iron, copper, coal - the three the world generator knows how to place. A count rather than an enum, because the catalog names no resource: it says "the second one", and the caller resolves it against its own definitions.</summary>
        public const int ResourceKindCount = 3;

        public SectorGrid Grid { get; }
        public int Seed { get; }

        /// <summary>
        /// The seed is required and has no default, for the reason SectorGrid has none for its sector
        /// size: a default here would be a second copy of a setting, free to disagree with the asset.
        /// </summary>
        public SectorCatalog(SectorGrid grid, int seed)
        {
            Grid = grid;
            Seed = seed;
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

            // One in eight sectors is bare. An empty sector has to be possible, or "there is
            // something in every direction" becomes the same as "direction does not matter".
            uint featureDraw = Hash(Seed, index, FeatureSalt) % 8;
            SectorFeature feature =
                featureDraw == 0 ? SectorFeature.None :
                featureDraw <= 4 ? SectorFeature.OreCluster :
                featureDraw <= 6 ? SectorFeature.Wreck :
                                   SectorFeature.Nest;

            int depositCount = feature == SectorFeature.None ? 0 : 2 + (int)(Hash(Seed, index, DepositSalt) % 4);
            var deposits = new GridCoord[depositCount];

            for (int i = 0; i < depositCount; i++)
            {
                // Anywhere in the square, corners included - no attempt to keep them inside the
                // revealed disc. The ones that fall outside it are the point.
                uint draw = Hash(Seed, index, DepositSalt + (uint)(i + 1) * 0x9E3779B9u);
                int x = origin.X + (int)(draw % (uint)width);
                int y = origin.Y + (int)(draw / 65536u % (uint)height);
                deposits[i] = new GridCoord(x, y);
            }

            // One ore for the whole sector, drawn once. A mixture would make every destination
            // interchangeable - see SectorContents.ResourceIndex.
            int resource = (int)(Hash(Seed, index, ResourceSalt) % (uint)ResourceKindCount);

            return new SectorContents(feature, featureCell, deposits, resource);
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
