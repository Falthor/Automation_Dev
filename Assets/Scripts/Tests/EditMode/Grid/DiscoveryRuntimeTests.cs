using System.Collections.Generic;
using Game.Core;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Grid
{
    /// <summary>
    /// The discovery state: what the player knows, one cell at a time, written by whoever reveals
    /// and never derived from where the Core happens to be.
    /// </summary>
    public class DiscoveryRuntimeTests
    {
        const int Size = 32;

        /// <summary>The shipped chunk size. Storage is per chunk, so a test map smaller than one chunk would never exercise a second one.</summary>
        const int ChunkSize = 64;

        [Test]
        public void ANewMap_IsEntirelyUnknown()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            Assert.AreEqual(0, discovery.DiscoveredCount());
            Assert.AreEqual(DiscoveryState.Unknown, discovery.GetState(new GridCoord(5, 5)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(0, 0)));
        }

        [Test]
        public void RevealingACell_MarksThatCellAndNoOther()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            Assert.IsTrue(discovery.Reveal(new GridCoord(4, 7)));

            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(4, 7)));
            Assert.AreEqual(1, discovery.DiscoveredCount());
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(7, 4)), "Not the transposed cell - the index is row-major, and getting that backwards would still pass a symmetric test.");
        }

        [Test]
        public void RevealingTheSameCellTwice_ReportsNoChangeTheSecondTime()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            var cell = new GridCoord(3, 3);

            Assert.IsTrue(discovery.Reveal(cell));
            Assert.IsFalse(discovery.Reveal(cell));
            Assert.AreEqual(1, discovery.DiscoveredCount());
        }

        [Test]
        public void OutOfBounds_ReadsAsUnknownAndCannotBeRevealed()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            foreach (GridCoord cell in new[] { new GridCoord(-1, 0), new GridCoord(0, -1), new GridCoord(Size, 0), new GridCoord(0, Size) })
            {
                Assert.IsFalse(discovery.Contains(cell), $"{cell}");
                Assert.AreEqual(DiscoveryState.Unknown, discovery.GetState(cell), $"{cell}");
                Assert.IsFalse(discovery.Reveal(cell), $"{cell}");
            }

            Assert.AreEqual(0, discovery.DiscoveredCount());
        }

        // ---- The version counter: what tells the renderer whether to re-upload ----

        [Test]
        public void TheVersion_MovesOnlyWhenSomethingActuallyChanged()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            int before = discovery.Version;

            discovery.Reveal(new GridCoord(2, 2));
            int afterFirst = discovery.Version;
            Assert.AreNotEqual(before, afterFirst, "A real revelation has to be visible to the renderer.");

            discovery.Reveal(new GridCoord(2, 2));
            Assert.AreEqual(afterFirst, discovery.Version, "Re-revealing the same cell must not cost an upload.");

            discovery.RevealDisc(new Vector2(2.5f, 2.5f), 0.4f);
            Assert.AreEqual(afterFirst, discovery.Version, "Nor must a disc that covers only what is already known.");
        }

        [Test]
        public void RevealingADisc_BumpsTheVersionOnce_NotOncePerCell()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            int before = discovery.Version;

            int revealed = discovery.RevealDisc(new Vector2(16f, 16f), 5f);

            Assert.Greater(revealed, 1, "Precondition: the disc covered several cells.");
            Assert.AreEqual(before + 1, discovery.Version);
        }

        // ---- The disc ----

        [Test]
        public void ADisc_RevealsWhatItCovers_AndNothingBeyondIt()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            var center = new Vector2(16f, 16f);
            const float radius = 6f;

            discovery.RevealDisc(center, radius);

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x + 0.5f - center.x;
                    float dy = y + 0.5f - center.y;
                    bool inside = dx * dx + dy * dy <= radius * radius;

                    Assert.AreEqual(inside, discovery.IsDiscovered(new GridCoord(x, y)), $"cell ({x},{y})");
                }
            }
        }

        [Test]
        public void ADiscOverTheEdge_RevealsTheCellsInsideTheMap_WithoutThrowing()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            Assert.DoesNotThrow(() => discovery.RevealDisc(new Vector2(0f, 0f), 8f));

            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(0, 0)));
            Assert.Greater(discovery.DiscoveredCount(), 0);
        }

        [Test]
        public void AZeroRadius_RevealsNothing()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            Assert.AreEqual(0, discovery.RevealDisc(new Vector2(16f, 16f), 0f));
            Assert.AreEqual(0, discovery.DiscoveredCount());
        }

        /// <summary>
        /// Discovery is permanent. Nothing here un-discovers, and a narrower disc later must not take
        /// back what a wider one gave - that is the whole difference between this state and the disc
        /// the fog used to compute from the Core's current reach.
        /// </summary>
        [Test]
        public void AWiderDiscThenANarrowerOne_KeepsEverythingTheWiderOneGave()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            var center = new Vector2(16f, 16f);

            discovery.RevealDisc(center, 8f);
            int wide = discovery.DiscoveredCount();

            discovery.RevealDisc(center, 2f);

            Assert.AreEqual(wide, discovery.DiscoveredCount());
        }

        /// <summary>Extending the radius reveals the ring between the two, and only it.</summary>
        [Test]
        public void ExtendingTheRadius_RevealsTheRingItGained()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            var center = new Vector2(16f, 16f);

            discovery.RevealDisc(center, 5f);
            int inner = discovery.DiscoveredCount();

            int gained = discovery.RevealDisc(center, 9f);

            Assert.Greater(gained, 0);
            Assert.AreEqual(inner + gained, discovery.DiscoveredCount(), "The second pass added exactly what it reported.");
        }

        [Test]
        public void RevealCells_MarksEveryCellGiven_AndSkipsThoseOutside()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            int revealed = discovery.RevealCells(new List<GridCoord>
            {
                new GridCoord(1, 1), new GridCoord(2, 2), new GridCoord(-5, 0), new GridCoord(1, 1)
            });

            Assert.AreEqual(2, revealed, "Two distinct in-bounds cells; the duplicate and the out-of-bounds one count for nothing.");
            Assert.AreEqual(2, discovery.DiscoveredCount());
        }

        [Test]
        public void RevealCells_ToleratesNull()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            Assert.DoesNotThrow(() => discovery.RevealCells(null));
            Assert.AreEqual(0, discovery.DiscoveredCount());
        }

        // ---- Save / Restore ----

        [Test]
        public void CaptureRestore_RoundTripsTheWholeMap()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            discovery.RevealDisc(new Vector2(10f, 20f), 6f);
            discovery.Reveal(new GridCoord(0, 0));
            discovery.Reveal(new GridCoord(Size - 1, Size - 1));

            string captured = discovery.CaptureState();

            var reloaded = new DiscoveryRuntime(Size, ChunkSize);
            reloaded.RestoreState(captured);

            Assert.AreEqual(discovery.DiscoveredCount(), reloaded.DiscoveredCount());
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(discovery.GetState(cell), reloaded.GetState(cell), $"cell ({x},{y})");
                }
            }
        }

        /// <summary>
        /// The reason the captured form is run-length encoded rather than one entry per cell: the
        /// save is written indented, and a real map is a handful of large runs. If this ever starts
        /// producing a run per cell, the save file grows by three orders of magnitude.
        /// </summary>
        [Test]
        public void TheCapturedForm_IsAHandfulOfRuns_NotOneEntryPerCell()
        {
            var discovery = new DiscoveryRuntime(300, ChunkSize);
            discovery.RevealDisc(new Vector2(150f, 150f), 22f);

            int runs = discovery.CaptureState().Split(',').Length;

            Assert.Less(runs, 200, "A single disc on an untouched map is two runs per covered row plus the empty remainder.");
            Assert.Greater(runs, 0);
        }

        [Test]
        public void Restore_OnAnAbsentOrEmptyValue_LeavesTheMapUndiscovered()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            discovery.RevealDisc(new Vector2(16f, 16f), 5f);

            Assert.DoesNotThrow(() => discovery.RestoreState(null));
            Assert.AreEqual(0, discovery.DiscoveredCount(), "A save predating this field loads as a map nobody has explored.");

            discovery.RevealDisc(new Vector2(16f, 16f), 5f);
            discovery.RestoreState(string.Empty);
            Assert.AreEqual(0, discovery.DiscoveredCount());
        }

        [Test]
        public void Restore_OnAMalformedValue_DoesNotThrow()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);

            foreach (string malformed in new[] { "garbage", "1:", ":5", "1:notanumber", ",,,", "1:-4", "9:3" })
            {
                Assert.DoesNotThrow(() => discovery.RestoreState(malformed), malformed);
            }
        }

        /// <summary>A run claiming more cells than the map holds is clipped, not allowed to run off the end of the array.</summary>
        [Test]
        public void Restore_OnARunLongerThanTheMap_IsClipped()
        {
            var discovery = new DiscoveryRuntime(4, ChunkSize);

            Assert.DoesNotThrow(() => discovery.RestoreState("1:1000"));
            Assert.AreEqual(16, discovery.DiscoveredCount(), "Every cell of the 4x4 map, and no more.");
        }

        [Test]
        public void Restore_ReplacesWhateverWasThere_RatherThanMergingIntoIt()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            discovery.RevealDisc(new Vector2(4f, 4f), 3f);

            discovery.RestoreState("0:2,1:1");

            Assert.AreEqual(1, discovery.DiscoveredCount(), "The restored map is the saved one, not the union of both.");
            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(2, 0)));
        }

        [Test]
        public void Restore_MovesTheVersion_SoTheRendererUploadsTheLoadedMap()
        {
            var discovery = new DiscoveryRuntime(Size, ChunkSize);
            int before = discovery.Version;

            discovery.RestoreState("1:5");

            Assert.AreNotEqual(before, discovery.Version);
        }

        // ---- Sparse storage ----
        //
        // The storage is per chunk, created on first write. None of it is visible through the API,
        // so what is asserted here is that it stays invisible - plus the one thing that would be
        // catastrophic if got backwards: an absent chunk means unknown, not discovered.

        /// <summary>Several chunks across, unlike Size above which fits inside one.</summary>
        const int WideSize = 200;

        static DiscoveryRuntime NewWide(int chunkSize = ChunkSize) => new DiscoveryRuntime(WideSize, chunkSize);

        [Test]
        public void AFreshMapHasNoStorageAtAll_AndReadsAsWhollyUnknown()
        {
            var discovery = NewWide();

            Assert.AreEqual(0, discovery.MaterialisedChunkCount);
            Assert.AreEqual(0, discovery.DiscoveredCount());

            foreach (GridCoord cell in new[] { new GridCoord(0, 0), new GridCoord(100, 100), new GridCoord(199, 199) })
            {
                Assert.AreEqual(DiscoveryState.Unknown, discovery.GetState(cell), $"{cell}");
            }
        }

        /// <summary>
        /// The one that would be catastrophic backwards. Writing a cell brings its chunk into
        /// existence, and every other cell of that same chunk has to stay unknown - a chunk allocated
        /// as discovered would reveal 4 096 cells at once, and the whole map as soon as it was
        /// touched.
        /// </summary>
        [Test]
        public void MaterialisingAChunkLeavesItsOtherCellsUnknown()
        {
            var discovery = NewWide();
            discovery.Reveal(new GridCoord(70, 70));

            Assert.AreEqual(1, discovery.MaterialisedChunkCount);
            Assert.AreEqual(1, discovery.DiscoveredCount(), "Exactly the one cell written, not its whole chunk.");

            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(70, 70)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(71, 70)), "Its neighbour in the same chunk.");
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(64, 64)), "The chunk's own first cell.");
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(127, 127)), "The chunk's last cell.");
        }

        [Test]
        public void OnlyTheChunksActuallyTouchedComeIntoExistence()
        {
            var discovery = NewWide();

            // A disc well inside one chunk - chunk (0,0), deliberately not one of the four below.
            discovery.RevealDisc(new Vector2(32f, 32f), 5f);
            Assert.AreEqual(1, discovery.MaterialisedChunkCount);

            // One straddling the corner where chunks (1,1), (2,1), (1,2) and (2,2) meet: x = 128 is
            // the boundary between chunk 1 (64-127) and chunk 2 (128-191).
            discovery.RevealDisc(new Vector2(128f, 128f), 3f);
            Assert.AreEqual(5, discovery.MaterialisedChunkCount, "One from the first disc plus the four around the corner.");
        }

        /// <summary>A cell exactly on a chunk boundary is the classic off-by-one in the index arithmetic.</summary>
        [Test]
        public void CellsEitherSideOfAChunkBoundaryAreIndependent()
        {
            var discovery = NewWide();

            discovery.Reveal(new GridCoord(63, 10));   // last column of chunk 0
            discovery.Reveal(new GridCoord(64, 10));   // first column of chunk 1

            Assert.AreEqual(2, discovery.MaterialisedChunkCount);
            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(63, 10)));
            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(64, 10)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(65, 10)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(62, 10)));
        }

        [Test]
        public void TheFarCornerOfTheMapIsReachable()
        {
            var discovery = NewWide();
            var corner = new GridCoord(WideSize - 1, WideSize - 1);

            Assert.IsTrue(discovery.Reveal(corner));
            Assert.IsTrue(discovery.IsDiscovered(corner));
            Assert.AreEqual(1, discovery.DiscoveredCount(), "The clipped edge chunk must not count cells past the map.");
        }

        /// <summary>
        /// The captured string is a contract (CONTRACTS.md §14) and the storage is not. Two maps with
        /// the same revelations but different chunk sizes have to capture identically, or the save
        /// format would depend on an implementation detail.
        /// </summary>
        [Test]
        public void TheCapturedFormDoesNotDependOnTheChunkSize()
        {
            var coarse = NewWide(64);
            var fine = NewWide(8);

            foreach (DiscoveryRuntime discovery in new[] { coarse, fine })
            {
                discovery.RevealDisc(new Vector2(100f, 100f), 20f);
                discovery.Reveal(new GridCoord(0, 0));
                discovery.Reveal(new GridCoord(199, 199));
            }

            Assert.AreEqual(coarse.CaptureState(), fine.CaptureState());
        }

        [Test]
        public void ARoundTripRestoresEveryCell_AndMaterialisesNothingForTheUnknownPart()
        {
            var source = NewWide();
            source.RevealDisc(new Vector2(100f, 100f), 12f);
            int chunksTouched = source.MaterialisedChunkCount;

            var restored = NewWide();
            restored.RestoreState(source.CaptureState());

            Assert.AreEqual(source.DiscoveredCount(), restored.DiscoveredCount());
            Assert.AreEqual(chunksTouched, restored.MaterialisedChunkCount,
                "Restoring must not bring the unknown chunks into existence.");

            for (int y = 0; y < WideSize; y += 7)
            {
                for (int x = 0; x < WideSize; x += 7)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(source.GetState(cell), restored.GetState(cell), $"{cell}");
                }
            }
        }

        [Test]
        public void RestoringOverAnExploredMapDropsWhatWasThere()
        {
            var discovery = NewWide();
            discovery.RevealDisc(new Vector2(100f, 100f), 20f);
            Assert.Greater(discovery.MaterialisedChunkCount, 0);

            discovery.RestoreState(string.Empty);

            Assert.AreEqual(0, discovery.MaterialisedChunkCount);
            Assert.AreEqual(0, discovery.DiscoveredCount());
        }

        /// <summary>The point of the whole change: cost follows what was explored, not the size of the map.</summary>
        [Test]
        public void ASmallExploredAreaCostsTheSameOnAHugeMap()
        {
            var small = new DiscoveryRuntime(300, ChunkSize);
            var huge = new DiscoveryRuntime(10000, ChunkSize);

            foreach (DiscoveryRuntime discovery in new[] { small, huge })
            {
                discovery.RevealDisc(new Vector2(150f, 150f), 22f);
            }

            Assert.AreEqual(small.MaterialisedChunkCount, huge.MaterialisedChunkCount);
            Assert.AreEqual(small.DiscoveredCount(), huge.DiscoveredCount());
        }

    }
}
