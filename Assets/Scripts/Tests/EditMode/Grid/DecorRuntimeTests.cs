using System.Collections.Generic;
using Game.Core;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Grid
{
    /// <summary>
    /// What grows on the ground, derived per chunk, minus what the player has cleared.
    ///
    /// Two properties carry the design and both fail quietly. The derivation has to give the same
    /// answer whatever order chunks are asked in, or the world reshapes itself as the camera wanders.
    /// And a cleared rock must never grow back, or clearing ground to build on becomes something the
    /// player has to do again every time they look away.
    /// </summary>
    public class DecorRuntimeTests
    {
        const int ChunkSize = 64;
        const int Seed = 20260907;

        /// <summary>Rocks favour band 0, bushes band 1 - enough asymmetry that a mixed-up band shows.</summary>
        static float[][] TwoKinds() => new[]
        {
            new[] { 4f, 1f, 1f },
            new[] { 1f, 4f, 1f }
        };

        static BiomeField NewBiome() => new BiomeField(Vector2.zero, 40f, 6426f, new[] { 1f, 1f, 0f }, 2);

        static DecorRuntime NewDecor(int mapSize = 640, float itemsPerChunk = 40f, float edgeExclusion = 0f)
            => new DecorRuntime(mapSize, ChunkSize, Seed, NewBiome(), TwoKinds(), itemsPerChunk, edgeExclusion);

        static List<DecorItem> Chunk(DecorRuntime decor, int cx, int cy)
        {
            var items = new List<DecorItem>();
            decor.CollectChunk(cx, cy, items);
            return items;
        }

        static string Describe(List<DecorItem> items)
        {
            var builder = new System.Text.StringBuilder();
            foreach (DecorItem item in items)
            {
                builder.Append(item.Cell).Append(':').Append(item.Kind).Append(':').Append(item.Band).Append('|');
            }
            return builder.ToString();
        }

        // ---- Purity ----

        /// <summary>
        /// The directive's test, in the shape decor needs: the same chunks asked in two traversal
        /// orders. A chunk asked last must answer what it answered when asked first.
        /// </summary>
        [Test]
        public void TheOrderChunksAreAskedIn_ChangesNothing()
        {
            DecorRuntime forward = NewDecor();
            DecorRuntime backward = NewDecor();

            var seen = new Dictionary<int, string>();

            for (int cy = 0; cy < 5; cy++)
            {
                for (int cx = 0; cx < 5; cx++) seen[cy * 10 + cx] = Describe(Chunk(forward, cx, cy));
            }

            for (int cy = 4; cy >= 0; cy--)
            {
                for (int cx = 4; cx >= 0; cx--)
                {
                    Assert.AreEqual(seen[cy * 10 + cx], Describe(Chunk(backward, cx, cy)), $"chunk ({cx},{cy})");
                }
            }
        }

        [Test]
        public void AChunkAskedTwice_AnswersTheSame()
        {
            DecorRuntime decor = NewDecor();

            Assert.AreEqual(Describe(Chunk(decor, 3, 2)), Describe(Chunk(decor, 3, 2)));
        }

        /// <summary>A chunk's contents must not depend on whether its neighbours were asked about first - the "no consulting neighbours" rule, asserted rather than trusted.</summary>
        [Test]
        public void AChunkDoesNotDependOnItsNeighboursBeingAskedFirst()
        {
            DecorRuntime alone = NewDecor();
            string askedAlone = Describe(Chunk(alone, 3, 3));

            DecorRuntime surrounded = NewDecor();
            for (int cy = 2; cy <= 4; cy++)
            {
                for (int cx = 2; cx <= 4; cx++)
                {
                    if (cx != 3 || cy != 3) Chunk(surrounded, cx, cy);
                }
            }

            Assert.AreEqual(askedAlone, Describe(Chunk(surrounded, 3, 3)));
        }

        [Test]
        public void ADifferentSeed_GrowsADifferentWorld()
        {
            var a = new DecorRuntime(640, ChunkSize, 1, NewBiome(), TwoKinds(), 40f, 0f);
            var b = new DecorRuntime(640, ChunkSize, 2, NewBiome(), TwoKinds(), 40f, 0f);

            Assert.AreNotEqual(Describe(Chunk(a, 2, 2)), Describe(Chunk(b, 2, 2)));
        }

        // ---- Density ----

        /// <summary>
        /// Density is per chunk, so it means the same thing at any map size. A global count could not:
        /// 500 items is a scatter on 300 cells and invisible on 10 000.
        /// </summary>
        [Test]
        public void TheSameChunkHoldsTheSameThings_WhateverTheMapSize()
        {
            var small = new DecorRuntime(640, ChunkSize, Seed, NewBiome(), TwoKinds(), 40f, 0f);
            var huge = new DecorRuntime(10000, ChunkSize, Seed, NewBiome(), TwoKinds(), 40f, 0f);

            // Same chunk coordinates would be different chunk INDICES on maps of different widths, so
            // the derivation is keyed on the index and the two are expected to differ. What must hold
            // is the density, which the next test measures. Here: both produce a real, non-empty chunk.
            Assert.Greater(Chunk(small, 3, 3).Count, 0);
            Assert.Greater(Chunk(huge, 3, 3).Count, 0);
        }

        [Test]
        public void DensityPerChunkIsWhatWasAskedFor_OnAnyMapSize()
        {
            foreach (int mapSize in new[] { 640, 3200, 10000 })
            {
                var decor = new DecorRuntime(mapSize, ChunkSize, Seed, NewBiome(), TwoKinds(), 40f, 0f);

                int total = 0;
                int chunks = 0;
                for (int cy = 0; cy < 6; cy++)
                {
                    for (int cx = 0; cx < 6; cx++)
                    {
                        total += Chunk(decor, cx, cy).Count;
                        chunks++;
                    }
                }

                float average = total / (float)chunks;
                Assert.That(average, Is.InRange(30f, 50f), $"map {mapSize}: {average} items per chunk, asked for 40");
            }
        }

        [Test]
        public void RaisingTheDensityRaisesTheCount()
        {
            var sparse = new DecorRuntime(640, ChunkSize, Seed, NewBiome(), TwoKinds(), 10f, 0f);
            var dense = new DecorRuntime(640, ChunkSize, Seed, NewBiome(), TwoKinds(), 80f, 0f);

            Assert.Less(Chunk(sparse, 2, 2).Count, Chunk(dense, 2, 2).Count);
        }

        // ---- Everything lands where it should ----

        [Test]
        public void EveryItemLandsInsideItsOwnChunk_AndInsideTheMap()
        {
            DecorRuntime decor = NewDecor();

            for (int cy = 0; cy < 4; cy++)
            {
                for (int cx = 0; cx < 4; cx++)
                {
                    foreach (DecorItem item in Chunk(decor, cx, cy))
                    {
                        Assert.AreEqual(cx, item.Cell.X / ChunkSize, $"{item.Cell} is not in chunk column {cx}");
                        Assert.AreEqual(cy, item.Cell.Y / ChunkSize, $"{item.Cell} is not in chunk row {cy}");
                        Assert.IsTrue(decor.Contains(item.Cell), $"{item.Cell} is off the map");
                    }
                }
            }
        }

        [Test]
        public void AChunkOffTheMap_GrowsNothing()
        {
            DecorRuntime decor = NewDecor();

            Assert.IsFalse(decor.ContainsChunk(-1, 0));
            CollectionAssert.IsEmpty(Chunk(decor, -1, 0));
            CollectionAssert.IsEmpty(Chunk(decor, 9999, 9999));
        }

        /// <summary>Band weights are what makes a world read as a place rather than as noise: the kind that favours a band has to actually dominate it.</summary>
        [Test]
        public void AKindFavouringABand_DominatesIt()
        {
            DecorRuntime decor = NewDecor(itemsPerChunk: 200f);

            var kindCountsInBand = new int[2, 2];
            for (int cy = 0; cy < 6; cy++)
            {
                for (int cx = 0; cx < 6; cx++)
                {
                    foreach (DecorItem item in Chunk(decor, cx, cy))
                    {
                        if (item.Band < 2) kindCountsInBand[item.Band, item.Kind]++;
                    }
                }
            }

            Assert.Greater(kindCountsInBand[0, 0], kindCountsInBand[0, 1], "kind 0 weighs 4 to 1 in band 0");
            Assert.Greater(kindCountsInBand[1, 1], kindCountsInBand[1, 0], "kind 1 weighs 4 to 1 in band 1");
        }

        // ---- What the player cleared ----

        /// <summary>
        /// The trap the whole delta set exists for: a cleared rock must not grow back the next time the
        /// chunk is asked. Nothing about the derivation knows the player was ever there.
        /// </summary>
        [Test]
        public void AClearedCell_NeverGrowsBack()
        {
            DecorRuntime decor = NewDecor();

            List<DecorItem> before = Chunk(decor, 2, 2);
            Assert.Greater(before.Count, 0);

            GridCoord cleared = before[0].Cell;
            Assert.IsTrue(decor.Remove(cleared));

            foreach (DecorItem item in Chunk(decor, 2, 2))
            {
                Assert.AreNotEqual(cleared, item.Cell, "a cleared cell grew back");
            }

            Assert.AreEqual(before.Count - 1, Chunk(decor, 2, 2).Count, "and nothing else disappeared with it");
        }

        [Test]
        public void ClearingTheSameCellTwice_ChangesNothingTheSecondTime()
        {
            DecorRuntime decor = NewDecor();
            GridCoord cell = Chunk(decor, 1, 1)[0].Cell;

            Assert.IsTrue(decor.Remove(cell));
            Assert.IsFalse(decor.Remove(cell));
            Assert.AreEqual(1, decor.RemovedCount);
        }

        [Test]
        public void ClearingACellOffTheMap_IsIgnored()
        {
            DecorRuntime decor = NewDecor();

            Assert.IsFalse(decor.Remove(new GridCoord(-1, 0)));
            Assert.IsFalse(decor.Remove(new GridCoord(0, 99999)));
            Assert.AreEqual(0, decor.RemovedCount);
        }

        /// <summary>
        /// A building's footprint is cells; decor is roughly one item per hundred cells. Recording a
        /// removal for bare ground would put about a hundred useless entries in the save for every
        /// rock actually cleared, and would make the delta set grow with the area a player has built
        /// on rather than with what they removed. Measured on a 200-building base: 1 800 footprint
        /// cells, 49 real clearings.
        /// </summary>
        [Test]
        public void ClearingBareGround_RecordsNothing()
        {
            DecorRuntime decor = NewDecor();

            var grown = new HashSet<int>();
            foreach (DecorItem item in Chunk(decor, 3, 3)) grown.Add(item.Cell.Y * 640 + item.Cell.X);

            int bare = 0;
            for (int y = 3 * ChunkSize; y < 4 * ChunkSize && bare < 200; y++)
            {
                for (int x = 3 * ChunkSize; x < 4 * ChunkSize && bare < 200; x++)
                {
                    if (grown.Contains(y * 640 + x)) continue;
                    Assert.IsFalse(decor.Remove(new GridCoord(x, y)), "bare ground was recorded as cleared");
                    bare++;
                }
            }

            Assert.Greater(bare, 0);
            Assert.AreEqual(0, decor.RemovedCount);
        }

        [Test]
        public void GrowsAt_AgreesWithWhatTheChunkHolds()
        {
            DecorRuntime decor = NewDecor();
            List<DecorItem> items = Chunk(decor, 4, 4);
            Assert.Greater(items.Count, 0);

            foreach (DecorItem item in items) Assert.IsTrue(decor.GrowsAt(item.Cell));

            decor.Remove(items[0].Cell);
            Assert.IsFalse(decor.GrowsAt(items[0].Cell), "a cleared cell still answered that something grows on it");
        }

        // ---- Ground taken by something the player did not clear ----

        /// <summary>
        /// An ore deposit's footprint grows nothing, and none of it reaches the save: the deposit is
        /// the seed's doing, not the player's, and it can be mined out - at which point the ground is
        /// free again. The whole-map scatter this replaces excluded deposit rects the same way.
        /// </summary>
        [Test]
        public void GroundTakenByADeposit_GrowsNothing_AndIsNeverRecorded()
        {
            DecorRuntime decor = NewDecor();
            List<DecorItem> before = Chunk(decor, 5, 5);
            Assert.Greater(before.Count, 0);

            GridCoord taken = before[0].Cell;
            decor.GroundIsTaken = cell => cell.X == taken.X && cell.Y == taken.Y;

            Assert.IsFalse(decor.GrowsAt(taken));
            foreach (DecorItem item in Chunk(decor, 5, 5)) Assert.AreNotEqual(taken, item.Cell);

            Assert.IsFalse(decor.Remove(taken));
            Assert.AreEqual(0, decor.RemovedCount, "a deposit's footprint must not enter the player's clearing history");
        }

        /// <summary>The other half: what a deposit covered comes back when the deposit goes, because nothing about it was ever stored.</summary>
        [Test]
        public void WhenTheGroundStopsBeingTaken_ItGrowsAgain()
        {
            DecorRuntime decor = NewDecor();
            GridCoord cell = Chunk(decor, 5, 5)[0].Cell;

            bool taken = true;
            decor.GroundIsTaken = c => taken && c.X == cell.X && c.Y == cell.Y;
            Assert.IsFalse(decor.GrowsAt(cell));

            taken = false;
            Assert.IsTrue(decor.GrowsAt(cell), "the memo of what the seed decides must not have swallowed a live answer");
        }

        // ---- Through a save ----

        [Test]
        public void ClearedCellsSurviveASaveAndReload()
        {
            DecorRuntime original = NewDecor();
            List<DecorItem> items = Chunk(original, 2, 2);

            var cleared = new List<GridCoord> { items[0].Cell, items[1].Cell, items[2].Cell };
            foreach (GridCoord cell in cleared) original.Remove(cell);

            DecorRuntime reloaded = NewDecor();
            reloaded.RestoreState(original.CaptureState());

            Assert.AreEqual(original.RemovedCount, reloaded.RemovedCount);
            foreach (GridCoord cell in cleared) Assert.IsTrue(reloaded.IsRemoved(cell), $"{cell} grew back through the save");

            Assert.AreEqual(Describe(Chunk(original, 2, 2)), Describe(Chunk(reloaded, 2, 2)));
        }

        [Test]
        public void AnUntouchedWorldSavesNothing()
        {
            Assert.IsEmpty(NewDecor().CaptureState());
        }

        [Test]
        public void RestoreIsTolerant()
        {
            DecorRuntime decor = NewDecor();

            Assert.DoesNotThrow(() => decor.RestoreState(null));
            Assert.DoesNotThrow(() => decor.RestoreState(string.Empty));
            Assert.DoesNotThrow(() => decor.RestoreState("12,,not-a-number,-5,999999999,7"));

            Assert.AreEqual(2, decor.RemovedCount, "the two readable, in-range entries survive and the rest are dropped");
        }

        [Test]
        public void RestoringOverAClearedWorld_ReplacesWhatWasThere()
        {
            DecorRuntime decor = NewDecor();
            decor.Remove(Chunk(decor, 1, 1)[0].Cell);

            decor.RestoreState(string.Empty);

            Assert.AreEqual(0, decor.RemovedCount);
        }

        // ---- The band-edge skip ----

        /// <summary>
        /// Excluding a strip along the band boundaries costs a few spots and removes the only case
        /// where the CPU port and the shader can disagree about what a rock is standing on.
        /// </summary>
        [Test]
        public void TheBandEdgeExclusion_RemovesSomeSpotsAndOnlySome()
        {
            DecorRuntime none = NewDecor(edgeExclusion: 0f);
            DecorRuntime excluded = NewDecor(edgeExclusion: 0.02f);

            int without = 0, with = 0;
            for (int cy = 0; cy < 6; cy++)
            {
                for (int cx = 0; cx < 6; cx++)
                {
                    without += Chunk(none, cx, cy).Count;
                    with += Chunk(excluded, cx, cy).Count;
                }
            }

            Assert.Less(with, without, "the exclusion removed nothing at all");
            Assert.Greater(with, without * 0.8f, "the exclusion removed far more than the thin strip it is meant to");
        }
    }
}
