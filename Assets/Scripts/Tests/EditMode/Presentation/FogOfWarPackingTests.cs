using Game.Core;
using Game.Grid;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// How the discovery state becomes the texel the shader samples.
    ///
    /// Orientation is the whole risk here and it is invisible until someone looks at the screen: a
    /// transposed or vertically mirrored fog is a plausible-looking image that is simply wrong, and
    /// on a symmetric starting disc it looks right. Asserted on an asymmetric pattern for that
    /// reason.
    /// </summary>
    public class FogOfWarPackingTests
    {
        const int Size = 8;

        static byte[] NewBuffer(int size, int texelsPerCell) => new byte[size * texelsPerCell * size * texelsPerCell];

        static byte TexelAt(byte[] texels, int side, int x, int y) => texels[y * side + x];

        [Test]
        public void AnUndiscoveredMap_PacksToAllZero()
        {
            var discovery = new DiscoveryRuntime(Size);
            byte[] texels = NewBuffer(Size, 1);

            FogOfWarView.PackTexels(discovery, 1, texels);

            foreach (byte texel in texels) Assert.AreEqual(0, texel);
        }

        /// <summary>
        /// Texel row y is cell row y, and texel column x is cell column x. Written the other way
        /// round the fog is mirrored or transposed, which no symmetric test would catch.
        /// </summary>
        [Test]
        public void ACellIsWrittenAtItsOwnRowAndColumn()
        {
            var discovery = new DiscoveryRuntime(Size);
            discovery.Reveal(new GridCoord(1, 6));
            byte[] texels = NewBuffer(Size, 1);

            FogOfWarView.PackTexels(discovery, 1, texels);

            Assert.AreEqual(255, TexelAt(texels, Size, 1, 6), "The revealed cell.");
            Assert.AreEqual(0, TexelAt(texels, Size, 6, 1), "Its transpose - a swapped index would light this one instead.");
            Assert.AreEqual(0, TexelAt(texels, Size, 1, 1), "Its vertical mirror.");
        }

        [Test]
        public void EveryDiscoveredCell_AndOnlyThose_AreLit()
        {
            var discovery = new DiscoveryRuntime(Size);
            discovery.RevealDisc(new Vector2(2f, 3f), 1.5f);
            byte[] texels = NewBuffer(Size, 1);

            FogOfWarView.PackTexels(discovery, 1, texels);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    byte expected = discovery.IsDiscovered(new GridCoord(x, y)) ? (byte)255 : (byte)0;
                    Assert.AreEqual(expected, TexelAt(texels, Size, x, y), $"cell ({x},{y})");
                }
            }
        }

        /// <summary>At more than one texel per cell, a cell becomes a solid block - the extra resolution buys a tighter interpolation ramp, not a different shape.</summary>
        [TestCase(2)]
        [TestCase(3)]
        public void AtSeveralTexelsPerCell_EachCellFillsItsOwnBlock(int texelsPerCell)
        {
            var discovery = new DiscoveryRuntime(Size);
            discovery.Reveal(new GridCoord(2, 5));

            int side = Size * texelsPerCell;
            byte[] texels = NewBuffer(Size, texelsPerCell);

            FogOfWarView.PackTexels(discovery, texelsPerCell, texels);

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
            var discovery = new DiscoveryRuntime(Size);
            discovery.Reveal(new GridCoord(0, 0));

            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, 1, new byte[4]));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, 1, null));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(null, 1, NewBuffer(Size, 1)));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, 0, NewBuffer(Size, 1)));
        }

        /// <summary>
        /// The packing reads the state and nothing else - it has no notion of a Core or a radius.
        /// A region revealed nowhere near the Core packs exactly like one revealed by it, which is
        /// what makes a mission's revelation drawable without touching this file.
        /// </summary>
        [Test]
        public void ARegionRevealedFarFromAnyRadius_PacksLikeAnyOther()
        {
            var discovery = new DiscoveryRuntime(Size);
            discovery.RevealCells(new[] { new GridCoord(7, 0), new GridCoord(7, 1) });
            byte[] texels = NewBuffer(Size, 1);

            FogOfWarView.PackTexels(discovery, 1, texels);

            Assert.AreEqual(255, TexelAt(texels, Size, 7, 0));
            Assert.AreEqual(255, TexelAt(texels, Size, 7, 1));
            Assert.AreEqual(0, TexelAt(texels, Size, 0, 0), "And nothing else was touched.");
        }
    }
}
