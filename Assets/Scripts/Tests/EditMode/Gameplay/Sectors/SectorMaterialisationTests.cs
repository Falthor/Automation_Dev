using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Sectors;
using Game.Grid;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Sectors
{
    /// <summary>
    /// Turning a sector's derived contents into real deposits.
    ///
    /// <b>One rule carries this whole class, and it fails silently:</b> a sector already carrying
    /// placed content keeps it. Get it backwards and the starting area - hand-composed precisely
    /// because the introduction depends on the right ore at the right distance - is overwritten by
    /// random derivation, and nothing says so until the run is unplayable.
    /// </summary>
    public class SectorMaterialisationTests
    {
        const int MapSize = 10000;
        const int SectorSize = 16;
        const int Seed = 20260907;


        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        OreDepositDefinition NewOre(string id)
        {
            var definition = ScriptableObject.CreateInstance<OreDepositDefinition>();
            definition.name = id;
            _created.Add(definition);
            return definition;
        }

        sealed class Fixture
        {
            public SectorGrid Sectors;
            public GridRuntime Cells;
            public SectorCatalog Catalog;
            public SectorMaterialisation Materialisation;
            public OreDepositDefinition[] Ores;
        }

        Fixture NewFixture()
        {
            var fixture = new Fixture
            {
                Sectors = new SectorGrid(MapSize, SectorSize),
                Cells = new GridRuntime(1f),
                Ores = new[] { NewOre("iron"), NewOre("copper"), NewOre("coal") }
            };

            fixture.Catalog = new SectorCatalog(fixture.Sectors, Seed);
            fixture.Materialisation = new SectorMaterialisation(fixture.Sectors, fixture.Cells, fixture.Catalog, fixture.Ores);

            return fixture;
        }

        /// <summary>The first sector at or after <paramref name="from"/> whose derivation actually holds deposits.</summary>
        static int SectorWithDeposits(Fixture fixture, int from = 1000)
        {
            for (int index = from; index < from + 200; index++)
            {
                if (fixture.Catalog.ContentsOf(index).DepositCells.Length > 0) return index;
            }

            throw new System.InvalidOperationException("no sector with deposits in range");
        }

        // ---- The rule ----

        [Test]
        public void AnEmptySector_GetsItsDerivedDeposits()
        {
            Fixture fixture = NewFixture();
            int sector = SectorWithDeposits(fixture);

            int placed = fixture.Materialisation.Materialise(sector);

            Assert.Greater(placed, 0);
            Assert.AreEqual(1, fixture.Materialisation.MaterialisedSectorCount);

            foreach (GridCoord cell in fixture.Catalog.ContentsOf(sector).DepositCells)
            {
                Assert.IsInstanceOf<DepositRuntime>(fixture.Cells.GetOccupant(cell));
            }

        }

        /// <summary>
        /// The test this class exists for. A sector carrying anything placed keeps all of it - not
        /// just the occupied cells, the whole sector - so derived ore never grows in the gaps between
        /// hand-placed clusters.
        /// </summary>
        [Test]
        public void ASectorCarryingPlacedContent_IsLeftAlone()
        {
            Fixture fixture = NewFixture();
            int sector = SectorWithDeposits(fixture);

            // One deliberately placed deposit, anywhere in the sector.
            GridCoord origin = fixture.Sectors.OriginOf(sector);
            fixture.Cells.PlaceDeposit(origin, fixture.Ores[0]);

            int placed = fixture.Materialisation.Materialise(sector);

            Assert.AreEqual(0, placed, "the derivation wrote into a sector that already carried placed content");
            Assert.AreEqual(1, fixture.Materialisation.SkippedForPlacedContentCount);
            Assert.AreEqual(0, fixture.Materialisation.MaterialisedSectorCount);

        }

        /// <summary>
        /// The ordering constraint, stated as a test rather than as a hope: place first, derive
        /// second, and the placed content survives. Reverse the two and it does not - which is the
        /// failure the rule exists to prevent, and the reason this is asserted from both directions.
        /// </summary>
        [Test]
        public void PlacedFirstSurvives_DerivedFirstIsOverwritten()
        {
            Fixture placedFirst = NewFixture();
            int sector = SectorWithDeposits(placedFirst);
            GridCoord origin = placedFirst.Sectors.OriginOf(sector);

            placedFirst.Cells.PlaceDeposit(origin, placedFirst.Ores[0]);
            placedFirst.Materialisation.Materialise(sector);

            Assert.AreEqual(placedFirst.Ores[0], ((DepositRuntime)placedFirst.Cells.GetOccupant(origin)).Definition,
                "the hand-placed deposit was replaced");

            // The other order: the derivation runs first, so the sector is no longer empty and the
            // placed content has nowhere to go. Nothing in the code prevents this - only the order in
            // which GameRuntime calls them does, which is why that order is documented.
            Fixture derivedFirst = NewFixture();
            derivedFirst.Materialisation.Materialise(sector);

            Assert.IsTrue(derivedFirst.Materialisation.CarriesPlacedContent(sector),
                "once derived, the sector reads as carrying content - so a later placement would have to fight it");

        }

        [Test]
        public void MaterialisingTwice_WritesNothingTheSecondTime()
        {
            Fixture fixture = NewFixture();
            int sector = SectorWithDeposits(fixture);

            int first = fixture.Materialisation.Materialise(sector);
            int second = fixture.Materialisation.Materialise(sector);

            Assert.Greater(first, 0);
            Assert.AreEqual(0, second, "idempotence comes from the grid itself, with no bookkeeping to keep in step");

        }

        // ---- One ore per sector ----

        /// <summary>
        /// A mining zone holds one ore, never a mixture. That is what gives a destination an identity
        /// and makes choosing one a decision rather than a draw.
        /// </summary>
        [Test]
        public void ASectorHoldsOneOre_NeverAMixture()
        {
            Fixture fixture = NewFixture();

            int checkedSectors = 0;
            for (int index = 1000; index < 1200 && checkedSectors < 20; index++)
            {
                SectorContents contents = fixture.Catalog.ContentsOf(index);
                if (contents.DepositCells.Length < 2) continue;

                fixture.Materialisation.Materialise(index);

                OreDepositDefinition first = null;
                foreach (GridCoord cell in contents.DepositCells)
                {
                    if (!(fixture.Cells.GetOccupant(cell) is DepositRuntime deposit)) continue;

                    first = first ?? deposit.Definition;
                    Assert.AreEqual(first, deposit.Definition, $"sector {index} holds more than one ore");
                }

                checkedSectors++;
            }

            Assert.Greater(checkedSectors, 0);
        }

        [Test]
        public void TheOreIsStableForASeed_AndVariesBetweenSeeds()
        {
            Fixture a = NewFixture();

            var otherGrid = new SectorGrid(MapSize, SectorSize);
            var other = new SectorCatalog(otherGrid, Seed + 1);

            int same = 0;
            for (int index = 1000; index < 1100; index++)
            {
                Assert.AreEqual(a.Catalog.ContentsOf(index).ResourceIndex, a.Catalog.ContentsOf(index).ResourceIndex);
                if (a.Catalog.ContentsOf(index).ResourceIndex == other.ContentsOf(index).ResourceIndex) same++;
            }

            Assert.Less(same, 90, "a different seed should not lay out the same ores");
        }

        // ---- Edges ----

        [Test]
        public void ASectorOffTheMap_DoesNothing()
        {
            Fixture fixture = NewFixture();

            Assert.AreEqual(0, fixture.Materialisation.Materialise(-1));
            Assert.AreEqual(0, fixture.Materialisation.Materialise(int.MaxValue));

        }

        [Test]
        public void WithNoOreDefinitions_NothingIsWritten()
        {
            Fixture fixture = NewFixture();
            var barren = new SectorMaterialisation(fixture.Sectors, fixture.Cells, fixture.Catalog, null);

            Assert.AreEqual(0, barren.Materialise(SectorWithDeposits(fixture)));
        }
    }
}
