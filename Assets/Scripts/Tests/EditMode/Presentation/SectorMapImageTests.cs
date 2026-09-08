using System.Collections.Generic;
using Game.Core;
using Game.Grid;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// The zoomed-out map's terrain: one tile per discovered chunk, one texel per cell, nothing
    /// anywhere else.
    ///
    /// Three properties carry it, and each fails quietly. Nothing may be drawn where nothing is
    /// discovered, or the black stops being absence and becomes a surface. A rebuild must cost the
    /// chunk that changed rather than everything ever seen. And a refresh that discovered nothing must
    /// not upload, or a still frame pays for a texture upload forever.
    /// </summary>
    public class SectorMapImageTests
    {
        const int MapSize = 10000;
        const int SectorSize = 16;
        const int ChunkSize = 64;

        static SectorGrid NewGrid(int mapSize = MapSize) => new SectorGrid(mapSize, SectorSize);

        static DiscoveryRuntime NewDiscovery(int mapSize = MapSize) => new DiscoveryRuntime(mapSize, ChunkSize);

        /// <summary>
        /// The rewrite's whole point. One texture for a 10 000-cell world is 400 MB per cell; one tile
        /// per discovered chunk is 16 KB each, and only for chunks that exist.
        /// </summary>
        [Test]
        public void ATileIsOneChunk_OneTexelPerCell()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            Assert.AreEqual(0, image.Tiles.Count, "an untouched world has no tiles at all");

            grid.RevealInscribedDisc(grid.IndexAt(312, 312), discovery);
            image.Refresh();

            Assert.AreEqual(1, image.Tiles.Count);
            Assert.AreEqual(ChunkSize, image.Tiles[0].Texture.width);
            Assert.AreEqual(ChunkSize, image.Tiles[0].Texture.height);
            Assert.AreEqual(ChunkSize, image.ChunkSizeCells);

            image.Dispose();
        }

        /// <summary>
        /// <b>Nothing is drawn where nothing is discovered.</b> The earlier image painted unknown
        /// ground a shade above the panel background so a sector could be aimed at; missions are aimed
        /// at sites now, and the black has to be the absence of a map rather than a dark shape on one.
        /// </summary>
        [Test]
        public void UndiscoveredGround_IsNotDrawnAtAll()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            discovery.RevealDisc(new Vector2(5000f, 5000f), 10f);
            image.Refresh();

            Assert.IsTrue(image.IsDrawnAt(new GridCoord(5000, 5000)), "the middle of the revealed disc is drawn");

            // Inside the same chunk, outside the disc: the tile exists, and this cell is still absent.
            Assert.IsFalse(image.IsDrawnAt(new GridCoord(4970, 4970)),
                "a cell in a discovered chunk that was never revealed must stay transparent");

            // And a cell in no tile at all.
            Assert.IsFalse(image.IsDrawnAt(new GridCoord(100, 100)));

            image.Dispose();
        }

        /// <summary>
        /// The reason the sector image had to go. A mission reveals a disc of radius 8 in a 16-cell
        /// sector, so at one texel per sector the whole square took one flat colour and the map read as
        /// a grid of blocks. Per cell, the revelation has an edge.
        /// </summary>
        [Test]
        public void ARevealedDisc_HasAnEdge_RatherThanFillingItsSquare()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            int sector = grid.IndexAt(312, 312);
            grid.RevealInscribedDisc(sector, discovery);
            image.Refresh();

            int drawn = 0;
            int absent = 0;
            foreach (GridCoord cell in grid.CellsOf(sector))
            {
                if (image.IsDrawnAt(cell)) drawn++;
                else absent++;
            }

            Assert.Greater(drawn, 0, "the disc was drawn");
            Assert.Greater(absent, 0, "and its square's corners were not - which is the whole difference");

            image.Dispose();
        }

        /// <summary>
        /// The property that keeps the map affordable once a player has explored: a rebuild costs the
        /// chunk that changed, not everything ever seen.
        /// </summary>
        [Test]
        public void ARebuildCostsWhatChanged_NotWhatExists()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            for (int i = 0; i < 400; i++) grid.RevealInscribedDisc(grid.IndexAt(200 + i % 20, 200 + i / 20), discovery);
            image.Refresh();

            int cellsForEverything = image.LastVisitedCellCount;
            int tilesForEverything = image.Tiles.Count;
            Assert.Greater(tilesForEverything, 1, "the fixture needs several chunks to be worth anything");

            // One more sector, far from the rest so it lands in a chunk of its own.
            grid.RevealInscribedDisc(grid.IndexAt(500, 500), discovery);
            Assert.IsTrue(image.Refresh());

            Assert.AreEqual(ChunkSize * ChunkSize, image.LastVisitedCellCount,
                $"revealing one sector repainted {image.LastVisitedCellCount} cells - the rebuild is pricing "
                + "everything the player has ever seen, not what moved");
            Assert.Less(image.LastVisitedCellCount, cellsForEverything);

            Assert.IsTrue(image.IsDrawnAt(new GridCoord(500 * SectorSize + 8, 500 * SectorSize + 8)), "the new ground was drawn");
            Assert.IsTrue(image.IsDrawnAt(new GridCoord(200 * SectorSize + 8, 200 * SectorSize + 8)), "and the old was not wiped");

            image.Dispose();
        }

        /// <summary>
        /// Every way of writing discovery has to reach the image, not just the one it was built
        /// against. This is the test that was missing when the incremental rebuild first went in.
        /// </summary>
        [Test]
        public void EveryWayOfRevealing_ReachesTheImage()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            discovery.RevealDisc(new Vector2(5000f, 5000f), 40f);
            Assert.IsTrue(image.Refresh());
            Assert.IsTrue(image.IsDrawnAt(new GridCoord(5000, 5000)), "the Core's starting disc never reached the image");

            var cells = new List<GridCoord>();
            for (int y = 1600; y < 1616; y++)
                for (int x = 1600; x < 1616; x++)
                    cells.Add(new GridCoord(x, y));

            discovery.RevealCells(cells);
            Assert.IsTrue(image.Refresh());
            Assert.IsTrue(image.IsDrawnAt(new GridCoord(1608, 1608)), "a region revelation never reached the image");

            discovery.Reveal(new GridCoord(3200, 3200));
            Assert.IsTrue(image.Refresh());
            Assert.IsTrue(image.IsDrawnAt(new GridCoord(3200, 3200)), "a single-cell revelation never reached the image");

            image.Dispose();
        }

        /// <summary>Restoring a save replaces every chunk, and the stamps come from a version that only ever increases - so a restored chunk cannot be mistaken for one already drawn.</summary>
        [Test]
        public void AfterARestore_TheImageRedraws()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();

            grid.RevealInscribedDisc(grid.IndexAt(312, 312), discovery);
            string saved = discovery.CaptureState();

            var fresh = new DiscoveryRuntime(MapSize, ChunkSize);
            var image = new SectorMapImage(grid, fresh);
            image.Refresh();
            Assert.AreEqual(0, image.Tiles.Count, "nothing discovered, nothing drawn");

            fresh.RestoreState(saved);
            Assert.IsTrue(image.Refresh());
            Assert.IsTrue(image.IsDrawnAt(new GridCoord(312 * SectorSize + 8, 312 * SectorSize + 8)));

            image.Dispose();
        }

        [Test]
        public void ARefreshThatDiscoveredNothing_DoesNotUpload()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            grid.RevealInscribedDisc(grid.IndexAt(312, 312), discovery);
            Assert.IsTrue(image.Refresh());

            int uploads = image.UploadCount;

            for (int i = 0; i < 10; i++) Assert.IsFalse(image.Refresh(), "nothing moved, so nothing to rebuild");

            Assert.AreEqual(uploads, image.UploadCount);

            image.Dispose();
        }

        [Test]
        public void RevealingSomethingNew_RebuildsOnce()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            grid.RevealInscribedDisc(grid.IndexAt(312, 312), discovery);
            image.Refresh();
            int uploads = image.UploadCount;

            grid.RevealInscribedDisc(grid.IndexAt(320, 312), discovery);

            Assert.IsTrue(image.Refresh());
            Assert.AreEqual(uploads + 1, image.UploadCount);
            Assert.IsTrue(image.IsDrawnAt(new GridCoord(320 * SectorSize + 8, 312 * SectorSize + 8)));
            Assert.IsTrue(image.IsDrawnAt(new GridCoord(312 * SectorSize + 8, 312 * SectorSize + 8)), "and the first one is still there");

            image.Dispose();
        }

        /// <summary>A tile is square even where the world is not: cells past the map's edge are simply never discovered, so they stay absent.</summary>
        [Test]
        public void TheMapsEdgeIsHandled()
        {
            SectorGrid grid = NewGrid(300);
            var discovery = new DiscoveryRuntime(300, ChunkSize);
            var image = new SectorMapImage(grid, discovery);

            int last = grid.Columns - 1;
            grid.RevealInscribedDisc(grid.IndexAt(last, last), discovery);

            Assert.DoesNotThrow(() => image.Refresh());
            Assert.AreEqual(1, image.Tiles.Count);
            Assert.IsFalse(image.IsDrawnAt(new GridCoord(400, 400)), "past the world's edge there is nothing to draw");

            image.Dispose();
        }

        [Test]
        public void ReadingOutsideTheWorldIsNotDrawn()
        {
            var image = new SectorMapImage(NewGrid(), NewDiscovery());

            Assert.IsFalse(image.IsDrawnAt(new GridCoord(-1, 0)));
            Assert.IsFalse(image.IsDrawnAt(new GridCoord(0, 99999)));

            image.Dispose();
        }
    }
}
