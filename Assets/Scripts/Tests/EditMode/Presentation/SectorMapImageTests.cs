using System.Collections.Generic;
using Game.Core;
using Game.Grid;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// The zoomed-out map's image: one texel per sector, for the whole map.
    ///
    /// Two properties carry it, and both fail quietly. It must walk only the chunks that exist, or
    /// a 10 000-cell map costs 390 625 sector tests per rebuild instead of a handful. And it must not
    /// re-upload when nothing has been discovered, or a still frame pays for a texture upload forever.
    /// </summary>
    public class SectorMapImageTests
    {
        const int MapSize = 10000;
        const int SectorSize = 16;
        const int ChunkSize = 64;

        static SectorGrid NewGrid(int mapSize = MapSize) => new SectorGrid(mapSize, SectorSize);

        static DiscoveryRuntime NewDiscovery(int mapSize = MapSize) => new DiscoveryRuntime(mapSize, ChunkSize);

        [Test]
        public void TheImageIsOneTexelPerSector()
        {
            SectorGrid grid = NewGrid();
            var image = new SectorMapImage(grid, NewDiscovery());

            Assert.AreEqual(grid.Columns, image.SizeSectors, "625 sectors along an axis of a 10 000-cell map");
            Assert.AreEqual(grid.Columns, image.Texture.width);
            Assert.AreEqual(grid.Columns, image.Texture.height);

            image.Dispose();
        }

        [Test]
        public void AnUntouchedMapIsWhollyUnknown()
        {
            var image = new SectorMapImage(NewGrid(), NewDiscovery());
            image.Refresh();

            Assert.AreEqual(SectorMapImage.UnknownTexel, image.TexelAt(0, 0));
            Assert.AreEqual(SectorMapImage.UnknownTexel, image.TexelAt(312, 312));
            Assert.AreEqual(0, image.LastVisitedSectorCount, "nothing has been written, so there is nothing to walk");

            image.Dispose();
        }

        /// <summary>
        /// A sector opened by a mission rests at Partial forever - the inscribed disc cannot cover the
        /// corners - so Partial is the normal state on this map, not a transient.
        /// </summary>
        [Test]
        public void ARevealedSectorReadsAsPartial()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            int index = grid.IndexAt(312, 312);
            grid.RevealInscribedDisc(index, discovery);
            image.Refresh();

            Assert.AreEqual(SectorMapImage.PartialTexel, image.TexelAt(312, 312));

            image.Dispose();
        }

        [Test]
        public void AFullyRevealedSectorReadsAsDiscovered()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            const int Column = 100;
            const int Row = 100;
            foreach (GridCoord cell in grid.CellsOf(grid.IndexAt(Column, Row))) discovery.Reveal(cell);

            image.Refresh();
            Assert.AreEqual(SectorMapImage.DiscoveredTexel, image.TexelAt(Column, Row));

            image.Dispose();
        }

        /// <summary>
        /// The property that makes drawing a 390 625-sector map affordable. A chunk holds 4x4 sectors,
        /// so revealing one sector should cost sixteen sector tests - not the whole map.
        /// </summary>
        [Test]
        public void OnlyTheChunksThatExistAreWalked()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            grid.RevealInscribedDisc(grid.IndexAt(312, 312), discovery);
            image.Refresh();

            Assert.AreEqual(1, discovery.MaterialisedChunkCount);
            Assert.AreEqual(16, image.LastVisitedSectorCount,
                "one chunk holds 4x4 sectors, and nothing outside a materialised chunk can be anything but unknown");

            Assert.Less(image.LastVisitedSectorCount, grid.Count / 1000,
                $"{image.LastVisitedSectorCount} sectors walked out of {grid.Count} - the sparse walk is not holding");

            image.Dispose();
        }

        /// <summary>
        /// The property that keeps the map affordable once a player has explored: a rebuild must cost
        /// the chunk that changed, not everything ever seen. Walking all materialised chunks measured
        /// 154 ms on a well-explored map - a dropped frame every time a mission reports back.
        /// </summary>
        [Test]
        public void ARebuildCostsWhatChanged_NotWhatExists()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            // A wide explored area first, all of it drawn once.
            for (int i = 0; i < 400; i++) grid.RevealInscribedDisc(grid.IndexAt(200 + i % 20, 200 + i / 20), discovery);
            image.Refresh();

            int walkedForEverything = image.LastVisitedSectorCount;
            Assert.Greater(walkedForEverything, 300, "the fixture needs a decent area explored to be worth anything");

            // Then one more sector, far from the rest so it lands in a chunk of its own.
            grid.RevealInscribedDisc(grid.IndexAt(500, 500), discovery);
            Assert.IsTrue(image.Refresh());

            Assert.AreEqual(16, image.LastVisitedSectorCount,
                $"revealing one sector walked {image.LastVisitedSectorCount} sectors - the rebuild is still "
                + "pricing everything the player has ever seen, not what moved");

            Assert.AreEqual(SectorMapImage.PartialTexel, image.TexelAt(500, 500), "the new sector was drawn");
            Assert.AreEqual(SectorMapImage.PartialTexel, image.TexelAt(200, 200), "and the old ones were not wiped");

            image.Dispose();
        }

        /// <summary>
        /// Every way of writing discovery has to reach the image, not just the one it was built
        /// against.
        ///
        /// This is the test that was missing when the incremental rebuild went in: per-chunk stamps
        /// were written by <c>Reveal</c> only, while the Core's starting disc goes through
        /// <c>RevealDisc</c> and a mission through <c>RevealInscribedDisc</c>. The version moved, no
        /// chunk looked changed, and the map drew nothing - indistinguishable from nothing having
        /// happened.
        /// </summary>
        [Test]
        public void EveryWayOfRevealing_ReachesTheImage()
        {
            SectorGrid grid = NewGrid();
            DiscoveryRuntime discovery = NewDiscovery();
            var image = new SectorMapImage(grid, discovery);

            // RevealDisc - the Core's own radius.
            discovery.RevealDisc(new Vector2(5000f, 5000f), 40f);
            Assert.IsTrue(image.Refresh());
            Assert.AreNotEqual(SectorMapImage.UnknownTexel, image.TexelAt(312, 312), "the Core's starting disc never reached the image");

            // RevealCells - an arbitrary region.
            var cells = new List<GridCoord>();
            for (int y = 1600; y < 1616; y++)
                for (int x = 1600; x < 1616; x++)
                    cells.Add(new GridCoord(x, y));

            discovery.RevealCells(cells);
            Assert.IsTrue(image.Refresh());
            Assert.AreEqual(SectorMapImage.DiscoveredTexel, image.TexelAt(100, 100), "a region revelation never reached the image");

            // Reveal - one cell.
            discovery.Reveal(new GridCoord(3200, 3200));
            Assert.IsTrue(image.Refresh());
            Assert.AreEqual(SectorMapImage.PartialTexel, image.TexelAt(200, 200), "a single-cell revelation never reached the image");

            image.Dispose();
        }

        /// <summary>
        /// Restoring a save replaces every chunk. The stamps are taken from a version that only ever
        /// increases, so a restored chunk cannot be mistaken for the one already drawn - which a
        /// per-chunk counter starting again at zero would have allowed.
        /// </summary>
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
            Assert.AreEqual(SectorMapImage.UnknownTexel, image.TexelAt(312, 312));

            fresh.RestoreState(saved);
            Assert.IsTrue(image.Refresh());
            Assert.AreEqual(SectorMapImage.PartialTexel, image.TexelAt(312, 312));

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
            Assert.AreEqual(SectorMapImage.PartialTexel, image.TexelAt(320, 312));
            Assert.AreEqual(SectorMapImage.PartialTexel, image.TexelAt(312, 312), "and the first one is still there");

            image.Dispose();
        }

        /// <summary>Sectors clipped by the map's edge must not be written outside the image, nor drop a real sector.</summary>
        [Test]
        public void TheMapsEdgeIsHandled()
        {
            SectorGrid grid = NewGrid(300);   // 300 is not a whole number of 16s
            DiscoveryRuntime discovery = new DiscoveryRuntime(300, ChunkSize);
            var image = new SectorMapImage(grid, discovery);

            int last = grid.Columns - 1;
            grid.RevealInscribedDisc(grid.IndexAt(last, last), discovery);

            Assert.DoesNotThrow(() => image.Refresh());
            Assert.AreNotEqual(SectorMapImage.UnknownTexel, image.TexelAt(last, last));

            image.Dispose();
        }

        [Test]
        public void ReadingOutsideTheImageIsUnknown()
        {
            var image = new SectorMapImage(NewGrid(), NewDiscovery());

            Assert.AreEqual(SectorMapImage.UnknownTexel, image.TexelAt(-1, 0));
            Assert.AreEqual(SectorMapImage.UnknownTexel, image.TexelAt(0, 99999));

            image.Dispose();
        }
    }
}
