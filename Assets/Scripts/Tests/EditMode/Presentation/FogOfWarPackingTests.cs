using Game.Core;
using Game.Grid;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// How the discovery state becomes the texels the shader samples.
    ///
    /// The texture is a <b>window</b> that follows the camera, not a copy of the map, so what is
    /// packed is relative to the window's origin. Two things break silently here and are asserted
    /// hardest: orientation, because a transposed or mirrored fog is a plausible-looking image that
    /// is simply wrong and looks right on a symmetric starting disc; and the window offset, because
    /// getting its sign backwards shifts the whole fog by a constant and still looks like fog.
    /// </summary>
    public class FogOfWarPackingTests
    {
        const int MapSize = 64;

        /// <summary>The shipped chunk size - discovery storage is per chunk.</summary>
        const int ChunkSize = 64;

        /// <summary>Small enough to assert texel by texel, unlike the shipped 256.</summary>
        const int WindowCells = 8;

        static readonly GridCoord Origin = new GridCoord(0, 0);

        static DiscoveryRuntime NewDiscovery() => new DiscoveryRuntime(MapSize, ChunkSize);

        static byte[] NewBuffer(int windowCells, int texelsPerCell)
            => new byte[windowCells * texelsPerCell * windowCells * texelsPerCell];

        static byte TexelAt(byte[] texels, int side, int x, int y) => texels[y * side + x];

        [Test]
        public void AnUndiscoveredMap_PacksToAllZero()
        {
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(NewDiscovery(), Origin, WindowCells, 1, texels);

            foreach (byte texel in texels) Assert.AreEqual(0, texel);
        }

        /// <summary>
        /// Texel row y is the window's row y, and texel column x its column x. Written the other way
        /// round the fog is mirrored or transposed, which no symmetric test would catch.
        /// </summary>
        [Test]
        public void ACellIsWrittenAtItsOwnRowAndColumn()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(1, 6));
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, Origin, WindowCells, 1, texels);

            Assert.AreEqual(255, TexelAt(texels, WindowCells, 1, 6), "The revealed cell.");
            Assert.AreEqual(0, TexelAt(texels, WindowCells, 6, 1), "Its transpose - a swapped index would light this one instead.");
            Assert.AreEqual(0, TexelAt(texels, WindowCells, 1, 1), "Its vertical mirror.");
        }

        [Test]
        public void EveryDiscoveredCell_AndOnlyThose_AreLit()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.RevealDisc(new Vector2(2f, 3f), 1.5f);
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, Origin, WindowCells, 1, texels);

            for (int y = 0; y < WindowCells; y++)
            {
                for (int x = 0; x < WindowCells; x++)
                {
                    byte expected = discovery.IsDiscovered(new GridCoord(x, y)) ? (byte)255 : (byte)0;
                    Assert.AreEqual(expected, TexelAt(texels, WindowCells, x, y), $"cell ({x},{y})");
                }
            }
        }

        // ---- The window ----

        /// <summary>
        /// The packing is relative to the window's origin: the same cell lands at a different texel
        /// once the window has moved. Getting the sign of this backwards shifts the whole fog by a
        /// constant offset, which still looks like fog.
        /// </summary>
        [Test]
        public void AMovedWindow_PacksTheSameCellAtADifferentTexel()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(10, 12));
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, new GridCoord(8, 8), WindowCells, 1, texels);
            Assert.AreEqual(255, TexelAt(texels, WindowCells, 2, 4), "cell (10,12) is (2,4) inside a window starting at (8,8).");

            FogOfWarView.PackTexels(discovery, new GridCoord(10, 12), WindowCells, 1, texels);
            Assert.AreEqual(255, TexelAt(texels, WindowCells, 0, 0), "and (0,0) inside a window starting on it.");
        }

        [Test]
        public void AWindowShowsOnlyWhatFallsInsideIt()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(2, 2));
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, new GridCoord(32, 32), WindowCells, 1, texels);

            foreach (byte texel in texels) Assert.AreEqual(0, texel, "Nothing discovered lies in this window.");
        }

        /// <summary>
        /// A window hanging off the edge of the world packs the outside as undiscovered - which is
        /// what fogs the world's border with no special case anywhere. There is nothing out there to
        /// have discovered.
        /// </summary>
        [Test]
        public void AWindowOverhangingTheMap_PacksTheOutsideAsUndiscovered()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++) discovery.Reveal(new GridCoord(x, y));
            }

            byte[] texels = NewBuffer(WindowCells, 1);

            // Straddling the map's top-right corner: four cells in, four out, on each axis.
            FogOfWarView.PackTexels(discovery, new GridCoord(MapSize - 4, MapSize - 4), WindowCells, 1, texels);

            Assert.AreEqual(255, TexelAt(texels, WindowCells, 3, 3), "Inside the map, and discovered.");
            Assert.AreEqual(0, TexelAt(texels, WindowCells, 4, 3), "One column past the map's edge.");
            Assert.AreEqual(0, TexelAt(texels, WindowCells, 3, 4), "One row past it.");
            Assert.AreEqual(0, TexelAt(texels, WindowCells, 7, 7), "The far corner, wholly outside.");
        }

        [Test]
        public void ANegativeOrigin_IsHandledLikeAnyOtherOutsideRegion()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(0, 0));
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, new GridCoord(-4, -4), WindowCells, 1, texels);

            Assert.AreEqual(255, TexelAt(texels, WindowCells, 4, 4), "Cell (0,0) sits at (4,4) in this window.");
            Assert.AreEqual(0, TexelAt(texels, WindowCells, 0, 0), "Cell (-4,-4) does not exist, so it is unknown.");
        }

        /// <summary>The whole point of the window: what it costs no longer depends on how big the map is.</summary>
        [Test]
        public void ThePackedResultDependsOnTheWindow_NotOnTheMap()
        {
            var small = new DiscoveryRuntime(300, ChunkSize);
            var huge = new DiscoveryRuntime(10000, ChunkSize);

            small.RevealDisc(new Vector2(150f, 150f), 3f);
            huge.RevealDisc(new Vector2(150f, 150f), 3f);

            byte[] forSmall = NewBuffer(WindowCells, 1);
            byte[] forHuge = NewBuffer(WindowCells, 1);

            Assert.AreEqual(forSmall.Length, forHuge.Length, "Same buffer for a 300-cell map and a 10 000-cell one.");

            FogOfWarView.PackTexels(small, new GridCoord(146, 146), WindowCells, 1, forSmall);
            FogOfWarView.PackTexels(huge, new GridCoord(146, 146), WindowCells, 1, forHuge);

            CollectionAssert.AreEqual(forSmall, forHuge, "The same neighbourhood packs the same, whatever the map around it.");
        }

        // ---- Resolution ----

        /// <summary>At more than one texel per cell, a cell becomes a solid block - the extra resolution buys a tighter interpolation ramp, not a different shape.</summary>
        [TestCase(2)]
        [TestCase(3)]
        public void AtSeveralTexelsPerCell_EachCellFillsItsOwnBlock(int texelsPerCell)
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(2, 5));

            int side = WindowCells * texelsPerCell;
            byte[] texels = NewBuffer(WindowCells, texelsPerCell);

            FogOfWarView.PackTexels(discovery, Origin, WindowCells, texelsPerCell, texels);

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    bool insideTheRevealedCell = x / texelsPerCell == 2 && y / texelsPerCell == 5;
                    Assert.AreEqual(insideTheRevealedCell ? 255 : 0, TexelAt(texels, side, x, y), $"texel ({x},{y})");
                }
            }
        }

        [Test]
        public void ATooSmallBufferOrABadArgument_IsIgnoredRatherThanOverrunning()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(0, 0));

            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, Origin, WindowCells, 1, new byte[4]));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, Origin, WindowCells, 1, null));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(null, Origin, WindowCells, 1, NewBuffer(WindowCells, 1)));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, Origin, WindowCells, 0, NewBuffer(WindowCells, 1)));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, Origin, 0, 1, NewBuffer(WindowCells, 1)));
        }

        /// <summary>
        /// The packing reads the state and nothing else - it has no notion of a Core or a radius.
        /// A region revealed nowhere near the Core packs exactly like one revealed by it, which is
        /// what makes a mission's revelation drawable without touching this file.
        /// </summary>
        [Test]
        public void ARegionRevealedFarFromAnyRadius_PacksLikeAnyOther()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.RevealCells(new[] { new GridCoord(7, 0), new GridCoord(7, 1) });
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, Origin, WindowCells, 1, texels);

            Assert.AreEqual(255, TexelAt(texels, WindowCells, 7, 0));
            Assert.AreEqual(255, TexelAt(texels, WindowCells, 7, 1));
            Assert.AreEqual(0, TexelAt(texels, WindowCells, 0, 0), "And nothing else was touched.");
        }
    }
}
