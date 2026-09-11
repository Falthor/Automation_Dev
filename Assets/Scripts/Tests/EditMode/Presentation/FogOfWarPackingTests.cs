using Game.Core;
using Game.Grid;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// How the discovery and observation state become the texels the shader samples.
    ///
    /// The texture is a <b>window</b> that follows the camera, not a copy of the map, so what is
    /// packed is relative to the window's origin. Two things break silently here and are asserted
    /// hardest: orientation, because a transposed or mirrored fog is a plausible-looking image that
    /// is simply wrong and looks right on a symmetric starting disc; and the window offset, because
    /// getting its sign backwards shifts the whole fog by a constant and still looks like fog.
    ///
    /// <b>Two channels of one texel</b> since the third state arrived - R is what has ever been
    /// discovered, G is what is observed right now - which adds a third silent failure: the two
    /// channels being swapped, or the byte stride being wrong, both of which produce a picture rather
    /// than an exception.
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
        {
            int side = windowCells * texelsPerCell;
            return new byte[side * side * FogOfWarView.BytesPerTexel];
        }

        static byte DiscoveredAt(byte[] texels, int side, int x, int y)
            => texels[(y * side + x) * FogOfWarView.BytesPerTexel + FogOfWarView.DiscoveryChannel];

        static byte ObservedAt(byte[] texels, int side, int x, int y)
            => texels[(y * side + x) * FogOfWarView.BytesPerTexel + FogOfWarView.ObservationChannel];

        /// <summary>One observer, rebuilt from scratch - the only way the field is ever filled.</summary>
        static ObservationRuntime Watching(Vector2 centreCells, float radiusCells)
        {
            var observation = new ObservationRuntime();
            observation.BeginRebuild();
            observation.Add(centreCells, radiusCells);
            observation.EndRebuild();
            return observation;
        }

        // ---- The discovery channel ----

        [Test]
        public void AnUndiscoveredMap_PacksToAllZero()
        {
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(NewDiscovery(), null, Origin, WindowCells, 1, texels);

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

            FogOfWarView.PackTexels(discovery, null, Origin, WindowCells, 1, texels);

            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 1, 6), "The revealed cell.");
            Assert.AreEqual(0, DiscoveredAt(texels, WindowCells, 6, 1), "Its transpose - a swapped index would light this one instead.");
            Assert.AreEqual(0, DiscoveredAt(texels, WindowCells, 1, 1), "Its vertical mirror.");
        }

        [Test]
        public void EveryDiscoveredCell_AndOnlyThose_AreLit()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.RevealDisc(new Vector2(2f, 3f), 1.5f);
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, null, Origin, WindowCells, 1, texels);

            for (int y = 0; y < WindowCells; y++)
            {
                for (int x = 0; x < WindowCells; x++)
                {
                    byte expected = discovery.IsDiscovered(new GridCoord(x, y)) ? (byte)255 : (byte)0;
                    Assert.AreEqual(expected, DiscoveredAt(texels, WindowCells, x, y), $"cell ({x},{y})");
                }
            }
        }

        // ---- The observation channel ----

        /// <summary>
        /// The two fields land in their own channels, and not in each other's. Swapped, the fog would
        /// still draw - it would simply veil the wrong half of the map.
        /// </summary>
        [Test]
        public void DiscoveryGoesToRed_ObservationToGreen()
        {
            DiscoveryRuntime discovery = NewDiscovery();

            // A wide swathe discovered, a narrow disc of it watched.
            discovery.RevealDisc(new Vector2(4f, 4f), 4f);
            ObservationRuntime observation = Watching(new Vector2(4f, 4f), 1.5f);

            byte[] texels = NewBuffer(WindowCells, 1);
            FogOfWarView.PackTexels(discovery, observation, Origin, WindowCells, 1, texels);

            for (int y = 0; y < WindowCells; y++)
            {
                for (int x = 0; x < WindowCells; x++)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(discovery.IsDiscovered(cell) ? 255 : 0,
                        DiscoveredAt(texels, WindowCells, x, y), $"R at ({x},{y})");
                    Assert.AreEqual(observation.IsObserved(cell) ? 255 : 0,
                        ObservedAt(texels, WindowCells, x, y), $"G at ({x},{y})");
                }
            }
        }

        [Test]
        public void WithNothingObserving_TheWholeDiscoveredMapPacksAsRemembered()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.RevealDisc(new Vector2(4f, 4f), 3f);

            byte[] texels = NewBuffer(WindowCells, 1);
            FogOfWarView.PackTexels(discovery, null, Origin, WindowCells, 1, texels);

            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 4, 4), "Discovered.");

            for (int y = 0; y < WindowCells; y++)
            {
                for (int x = 0; x < WindowCells; x++)
                {
                    Assert.AreEqual(0, ObservedAt(texels, WindowCells, x, y), $"G at ({x},{y}) - nothing is watching.");
                }
            }
        }

        /// <summary>
        /// <b>Observed implies discovered, in the data.</b> The invariant holds in the texture rather
        /// than only in the shader, so the shader is never handed the contradiction "observed but
        /// never discovered" - and "never discovered never becomes remembered" is true by
        /// construction instead of by everything happening to be called in the right order.
        /// </summary>
        [Test]
        public void AnObserverOverUndiscoveredGround_LightsNeitherChannel()
        {
            DiscoveryRuntime discovery = NewDiscovery();       // nothing revealed at all
            ObservationRuntime observation = Watching(new Vector2(4f, 4f), 3f);

            byte[] texels = NewBuffer(WindowCells, 1);
            FogOfWarView.PackTexels(discovery, observation, Origin, WindowCells, 1, texels);

            Assert.IsTrue(observation.IsObserved(new GridCoord(4, 4)), "The disc does cover it.");
            foreach (byte texel in texels) Assert.AreEqual(0, texel, "Undiscovered ground stays black however hard it is looked at.");
        }

        /// <summary>The state the whole feature exists for: the observer leaves, R stays, G goes.</summary>
        [Test]
        public void WhenTheObserverLeaves_ObservationDropsAndDiscoveryStays()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.RevealDisc(new Vector2(4f, 4f), 3f);

            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, Watching(new Vector2(4f, 4f), 3f), Origin, WindowCells, 1, texels);
            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 4, 4));
            Assert.AreEqual(255, ObservedAt(texels, WindowCells, 4, 4));

            FogOfWarView.PackTexels(discovery, Watching(new Vector2(40f, 40f), 3f), Origin, WindowCells, 1, texels);
            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 4, 4), "Still discovered - nothing was erased.");
            Assert.AreEqual(0, ObservedAt(texels, WindowCells, 4, 4), "And no longer observed.");
        }

        // ---- The observation-only path ----

        /// <summary>
        /// The frequent path. It must move G and leave R exactly as it was - a full repack here would
        /// cost 65 000 chunk lookups on every frame a robot is walking, which is the whole reason the
        /// split exists.
        /// </summary>
        [Test]
        public void PackingObservationAlone_LeavesTheDiscoveryChannelUntouched()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.RevealDisc(new Vector2(4f, 4f), 3f);

            byte[] texels = NewBuffer(WindowCells, 1);
            FogOfWarView.PackTexels(discovery, Watching(new Vector2(4f, 4f), 3f), Origin, WindowCells, 1, texels);

            var discoveryBefore = new byte[WindowCells * WindowCells];
            for (int y = 0; y < WindowCells; y++)
            {
                for (int x = 0; x < WindowCells; x++) discoveryBefore[y * WindowCells + x] = DiscoveredAt(texels, WindowCells, x, y);
            }

            FogOfWarView.PackObservationChannel(discovery, Watching(new Vector2(40f, 40f), 3f), Origin, WindowCells, 1, texels);

            for (int y = 0; y < WindowCells; y++)
            {
                for (int x = 0; x < WindowCells; x++)
                {
                    Assert.AreEqual(discoveryBefore[y * WindowCells + x], DiscoveredAt(texels, WindowCells, x, y),
                        $"R at ({x},{y}) must not move.");
                    Assert.AreEqual(0, ObservedAt(texels, WindowCells, x, y), $"G at ({x},{y}) - the observer went away.");
                }
            }
        }

        /// <summary>
        /// The cheap path reads "was this discovered" back out of the channel beside it rather than
        /// re-asking the chunk store. So it must still refuse to light G where R is dark, or an
        /// observer wandering over never-seen ground would clear it.
        /// </summary>
        [Test]
        public void PackingObservationAlone_StillRefusesToLightUndiscoveredGround()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            byte[] texels = NewBuffer(WindowCells, 1);

            // R packed from an empty map: all dark.
            FogOfWarView.PackTexels(discovery, null, Origin, WindowCells, 1, texels);

            FogOfWarView.PackObservationChannel(discovery, Watching(new Vector2(4f, 4f), 3f), Origin, WindowCells, 1, texels);

            foreach (byte texel in texels) Assert.AreEqual(0, texel, "Nothing discovered, so nothing observed.");
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

            FogOfWarView.PackTexels(discovery, null, new GridCoord(8, 8), WindowCells, 1, texels);
            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 2, 4), "cell (10,12) is (2,4) inside a window starting at (8,8).");

            FogOfWarView.PackTexels(discovery, null, new GridCoord(10, 12), WindowCells, 1, texels);
            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 0, 0), "and (0,0) inside a window starting on it.");
        }

        /// <summary>Both channels are read against the same origin - that is the point of them sharing a texture, and a shifted G would veil ground beside the one being watched.</summary>
        [Test]
        public void AMovedWindow_MovesBothChannelsTogether()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(10, 12));
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, Watching(new Vector2(10.5f, 12.5f), 0.6f), new GridCoord(8, 8), WindowCells, 1, texels);

            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 2, 4));
            Assert.AreEqual(255, ObservedAt(texels, WindowCells, 2, 4), "G lands on the same texel as R.");
        }

        [Test]
        public void AWindowShowsOnlyWhatFallsInsideIt()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(2, 2));
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, null, new GridCoord(32, 32), WindowCells, 1, texels);

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
            FogOfWarView.PackTexels(discovery, null, new GridCoord(MapSize - 4, MapSize - 4), WindowCells, 1, texels);

            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 3, 3), "Inside the map, and discovered.");
            Assert.AreEqual(0, DiscoveredAt(texels, WindowCells, 4, 3), "One column past the map's edge.");
            Assert.AreEqual(0, DiscoveredAt(texels, WindowCells, 3, 4), "One row past it.");
            Assert.AreEqual(0, DiscoveredAt(texels, WindowCells, 7, 7), "The far corner, wholly outside.");
        }

        /// <summary>An observer standing at the world's edge cannot light the outside either: there is nothing out there to have discovered, so G is gated off with R.</summary>
        [Test]
        public void AnObserverAtTheMapsEdge_DoesNotLightTheOutside()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++) discovery.Reveal(new GridCoord(x, y));
            }

            byte[] texels = NewBuffer(WindowCells, 1);
            var origin = new GridCoord(MapSize - 4, MapSize - 4);

            FogOfWarView.PackTexels(discovery, Watching(new Vector2(MapSize, MapSize), 6f), origin, WindowCells, 1, texels);

            Assert.AreEqual(255, ObservedAt(texels, WindowCells, 3, 3), "Inside the map, and watched.");
            Assert.AreEqual(0, ObservedAt(texels, WindowCells, 4, 3), "One column past the edge.");
            Assert.AreEqual(0, ObservedAt(texels, WindowCells, 7, 7), "The far corner, wholly outside.");
        }

        [Test]
        public void ANegativeOrigin_IsHandledLikeAnyOtherOutsideRegion()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(0, 0));
            byte[] texels = NewBuffer(WindowCells, 1);

            FogOfWarView.PackTexels(discovery, null, new GridCoord(-4, -4), WindowCells, 1, texels);

            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 4, 4), "Cell (0,0) sits at (4,4) in this window.");
            Assert.AreEqual(0, DiscoveredAt(texels, WindowCells, 0, 0), "Cell (-4,-4) does not exist, so it is unknown.");
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

            FogOfWarView.PackTexels(small, Watching(new Vector2(150f, 150f), 2f), new GridCoord(146, 146), WindowCells, 1, forSmall);
            FogOfWarView.PackTexels(huge, Watching(new Vector2(150f, 150f), 2f), new GridCoord(146, 146), WindowCells, 1, forHuge);

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

            FogOfWarView.PackTexels(discovery, null, Origin, WindowCells, texelsPerCell, texels);

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    bool insideTheRevealedCell = x / texelsPerCell == 2 && y / texelsPerCell == 5;
                    Assert.AreEqual(insideTheRevealedCell ? 255 : 0, DiscoveredAt(texels, side, x, y), $"texel ({x},{y})");
                }
            }
        }

        /// <summary>Both channels take the same block, so the veil's edge and the black's edge are cut at the same resolution.</summary>
        [Test]
        public void AtSeveralTexelsPerCell_ObservationFillsTheSameBlock()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(2, 5));

            const int texelsPerCell = 2;
            int side = WindowCells * texelsPerCell;
            byte[] texels = NewBuffer(WindowCells, texelsPerCell);

            FogOfWarView.PackTexels(discovery, Watching(new Vector2(2.5f, 5.5f), 0.6f), Origin, WindowCells, texelsPerCell, texels);

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    bool insideTheRevealedCell = x / texelsPerCell == 2 && y / texelsPerCell == 5;
                    Assert.AreEqual(insideTheRevealedCell ? 255 : 0, ObservedAt(texels, side, x, y), $"texel ({x},{y})");
                }
            }
        }

        [Test]
        public void ATooSmallBufferOrABadArgument_IsIgnoredRatherThanOverrunning()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(0, 0));
            ObservationRuntime observation = Watching(new Vector2(0.5f, 0.5f), 2f);

            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, observation, Origin, WindowCells, 1, new byte[4]));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, observation, Origin, WindowCells, 1, null));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(null, observation, Origin, WindowCells, 1, NewBuffer(WindowCells, 1)));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, observation, Origin, WindowCells, 0, NewBuffer(WindowCells, 1)));
            Assert.DoesNotThrow(() => FogOfWarView.PackTexels(discovery, observation, Origin, 0, 1, NewBuffer(WindowCells, 1)));

            Assert.DoesNotThrow(() => FogOfWarView.PackObservationChannel(discovery, observation, Origin, WindowCells, 1, new byte[4]));
            Assert.DoesNotThrow(() => FogOfWarView.PackObservationChannel(discovery, observation, Origin, WindowCells, 1, null));
        }

        /// <summary>
        /// A buffer sized for one channel is refused rather than half-filled. Before the second
        /// channel existed this was exactly the right length, so a caller that was never updated
        /// would otherwise write a plausible-looking half-image.
        /// </summary>
        [Test]
        public void ASingleChannelBuffer_IsRefused()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.Reveal(new GridCoord(1, 1));

            var oneChannel = new byte[WindowCells * WindowCells];
            FogOfWarView.PackTexels(discovery, null, Origin, WindowCells, 1, oneChannel);

            foreach (byte texel in oneChannel) Assert.AreEqual(0, texel, "Too small for two channels, so nothing was written.");
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

            FogOfWarView.PackTexels(discovery, null, Origin, WindowCells, 1, texels);

            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 7, 0));
            Assert.AreEqual(255, DiscoveredAt(texels, WindowCells, 7, 1));
            Assert.AreEqual(0, DiscoveredAt(texels, WindowCells, 0, 0), "And nothing else was touched.");
        }
    }
}
