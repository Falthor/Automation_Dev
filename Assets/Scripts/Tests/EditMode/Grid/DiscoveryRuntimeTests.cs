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

        [Test]
        public void ANewMap_IsEntirelyUnknown()
        {
            var discovery = new DiscoveryRuntime(Size);

            Assert.AreEqual(0, discovery.DiscoveredCount());
            Assert.AreEqual(DiscoveryState.Unknown, discovery.GetState(new GridCoord(5, 5)));
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(0, 0)));
        }

        [Test]
        public void RevealingACell_MarksThatCellAndNoOther()
        {
            var discovery = new DiscoveryRuntime(Size);

            Assert.IsTrue(discovery.Reveal(new GridCoord(4, 7)));

            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(4, 7)));
            Assert.AreEqual(1, discovery.DiscoveredCount());
            Assert.IsFalse(discovery.IsDiscovered(new GridCoord(7, 4)), "Not the transposed cell - the index is row-major, and getting that backwards would still pass a symmetric test.");
        }

        [Test]
        public void RevealingTheSameCellTwice_ReportsNoChangeTheSecondTime()
        {
            var discovery = new DiscoveryRuntime(Size);
            var cell = new GridCoord(3, 3);

            Assert.IsTrue(discovery.Reveal(cell));
            Assert.IsFalse(discovery.Reveal(cell));
            Assert.AreEqual(1, discovery.DiscoveredCount());
        }

        [Test]
        public void OutOfBounds_ReadsAsUnknownAndCannotBeRevealed()
        {
            var discovery = new DiscoveryRuntime(Size);

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
            var discovery = new DiscoveryRuntime(Size);
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
            var discovery = new DiscoveryRuntime(Size);
            int before = discovery.Version;

            int revealed = discovery.RevealDisc(new Vector2(16f, 16f), 5f);

            Assert.Greater(revealed, 1, "Precondition: the disc covered several cells.");
            Assert.AreEqual(before + 1, discovery.Version);
        }

        // ---- The disc ----

        [Test]
        public void ADisc_RevealsWhatItCovers_AndNothingBeyondIt()
        {
            var discovery = new DiscoveryRuntime(Size);
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
            var discovery = new DiscoveryRuntime(Size);

            Assert.DoesNotThrow(() => discovery.RevealDisc(new Vector2(0f, 0f), 8f));

            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(0, 0)));
            Assert.Greater(discovery.DiscoveredCount(), 0);
        }

        [Test]
        public void AZeroRadius_RevealsNothing()
        {
            var discovery = new DiscoveryRuntime(Size);

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
            var discovery = new DiscoveryRuntime(Size);
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
            var discovery = new DiscoveryRuntime(Size);
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
            var discovery = new DiscoveryRuntime(Size);

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
            var discovery = new DiscoveryRuntime(Size);

            Assert.DoesNotThrow(() => discovery.RevealCells(null));
            Assert.AreEqual(0, discovery.DiscoveredCount());
        }

        // ---- Save / Restore ----

        [Test]
        public void CaptureRestore_RoundTripsTheWholeMap()
        {
            var discovery = new DiscoveryRuntime(Size);
            discovery.RevealDisc(new Vector2(10f, 20f), 6f);
            discovery.Reveal(new GridCoord(0, 0));
            discovery.Reveal(new GridCoord(Size - 1, Size - 1));

            string captured = discovery.CaptureState();

            var reloaded = new DiscoveryRuntime(Size);
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
            var discovery = new DiscoveryRuntime(300);
            discovery.RevealDisc(new Vector2(150f, 150f), 22f);

            int runs = discovery.CaptureState().Split(',').Length;

            Assert.Less(runs, 200, "A single disc on an untouched map is two runs per covered row plus the empty remainder.");
            Assert.Greater(runs, 0);
        }

        [Test]
        public void Restore_OnAnAbsentOrEmptyValue_LeavesTheMapUndiscovered()
        {
            var discovery = new DiscoveryRuntime(Size);
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
            var discovery = new DiscoveryRuntime(Size);

            foreach (string malformed in new[] { "garbage", "1:", ":5", "1:notanumber", ",,,", "1:-4", "9:3" })
            {
                Assert.DoesNotThrow(() => discovery.RestoreState(malformed), malformed);
            }
        }

        /// <summary>A run claiming more cells than the map holds is clipped, not allowed to run off the end of the array.</summary>
        [Test]
        public void Restore_OnARunLongerThanTheMap_IsClipped()
        {
            var discovery = new DiscoveryRuntime(4);

            Assert.DoesNotThrow(() => discovery.RestoreState("1:1000"));
            Assert.AreEqual(16, discovery.DiscoveredCount(), "Every cell of the 4x4 map, and no more.");
        }

        [Test]
        public void Restore_ReplacesWhateverWasThere_RatherThanMergingIntoIt()
        {
            var discovery = new DiscoveryRuntime(Size);
            discovery.RevealDisc(new Vector2(4f, 4f), 3f);

            discovery.RestoreState("0:2,1:1");

            Assert.AreEqual(1, discovery.DiscoveredCount(), "The restored map is the saved one, not the union of both.");
            Assert.IsTrue(discovery.IsDiscovered(new GridCoord(2, 0)));
        }

        [Test]
        public void Restore_MovesTheVersion_SoTheRendererUploadsTheLoadedMap()
        {
            var discovery = new DiscoveryRuntime(Size);
            int before = discovery.Version;

            discovery.RestoreState("1:5");

            Assert.AreNotEqual(before, discovery.Version);
        }
    }
}
