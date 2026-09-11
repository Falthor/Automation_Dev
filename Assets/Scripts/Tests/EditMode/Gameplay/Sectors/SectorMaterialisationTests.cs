using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Sectors;
using Game.Gameplay.WorldGeneration;
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

        // The shipped cluster figures, restated rather than read from the asset: a test that followed
        // the setting could not fail when the setting is wrong.
        const int OneSectorIn = 12;

        /// <summary>The shipped bands and the two radii the ramp runs between - see OreClusterProfile.</summary>
        static OreClusterProfile Profile => new OreClusterProfile(OneSectorIn, 6, 10, 10, 15, 32f, 330f);

        /// <summary>The middle of the fixture map, where a generated world puts its Core.</summary>
        static readonly Vector2 CoreCentre = new Vector2(MapSize / 2f, MapSize / 2f);

        /// <summary>CoreRuntime.ExtendedActionRadiusCells - the Core's furthest reach, which is what derived ore must stay out of.</summary>
        const float ExclusionRadius = 32f;


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

            /// <summary>Where a materialised deposit has to end up. The grid is not enough - see WhatIsMaterialised_IsRegistered.</summary>
            public WorldGenerator World;
        }

        Fixture NewFixture()
        {
            var fixture = new Fixture
            {
                Sectors = new SectorGrid(MapSize, SectorSize),
                Cells = new GridRuntime(1f),
                Ores = new[] { NewOre("iron"), NewOre("copper"), NewOre("coal") },
                World = new WorldGenerator()
            };

            fixture.Catalog = new SectorCatalog(fixture.Sectors, Seed, CoreCentre, Profile);
            // No exclusion in the fixture: its sectors sit at index 1000+ on a 10 000 map and its
            // WorldGenerator has no Core, so a radius here would carve a hole at the origin and
            // measure nothing. The exclusion has its own tests below.
            fixture.Materialisation = new SectorMaterialisation(
                fixture.Sectors, fixture.Cells, fixture.Catalog, fixture.Ores, fixture.World, Vector2.zero, 0f);

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
            var other = new SectorCatalog(otherGrid, Seed + 1, CoreCentre, Profile);

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
            var barren = new SectorMaterialisation(fixture.Sectors, fixture.Cells, fixture.Catalog, null, fixture.World, Vector2.zero, 0f);

            Assert.AreEqual(0, barren.Materialise(SectorWithDeposits(fixture)));
        }

        /// <summary>
        /// <b>A materialised deposit is registered, not only written into the grid.</b>
        ///
        /// This is the defect that shipped: the deposit went straight into GridRuntime, so the grid
        /// knew about it - the hover glow lit up, an Extractor could have been placed on it - and the
        /// two things that read WorldGenerator.OreDeposits did not. Those two are the view and the
        /// save, so the ore was invisible and did not survive a reload. Nothing failed anywhere.
        ///
        /// Asserted on the count and on the event, because the view is driven by the event and the
        /// save by the list: one without the other would have fixed half of it.
        /// </summary>
        [Test]
        public void WhatIsMaterialised_IsRegistered()
        {
            Fixture fixture = NewFixture();
            int sector = SectorWithDeposits(fixture);

            var announced = new List<DepositRuntime>();
            fixture.World.DepositAppeared += announced.Add;

            int placed = fixture.Materialisation.Materialise(sector);

            Assert.Greater(placed, 0, "the fixture sector was supposed to derive deposits");
            Assert.AreEqual(placed, fixture.World.OreDeposits.Count, "every placed deposit has to be in the list the save reads");
            Assert.AreEqual(placed, announced.Count, "every placed deposit has to be announced, or nothing draws it");

            foreach (DepositRuntime deposit in announced)
            {
                Assert.AreSame(deposit, fixture.Cells.GetOccupant(deposit.Origin),
                    "the runtime that was announced has to be the one standing in the grid");
            }
        }

        /// <summary>
        /// Without a world there is nowhere to register a deposit, so none is written at all - rather
        /// than written into the grid where nothing owns it, which is the shape of the defect above.
        /// </summary>
        [Test]
        public void WithNoWorld_NothingIsWritten()
        {
            Fixture fixture = NewFixture();
            int sector = SectorWithDeposits(fixture);

            var worldless = new SectorMaterialisation(fixture.Sectors, fixture.Cells, fixture.Catalog, fixture.Ores, null, Vector2.zero, 0f);

            Assert.AreEqual(0, worldless.Materialise(sector));
            foreach (GridCoord cell in fixture.Catalog.ContentsOf(sector).DepositCells)
            {
                Assert.IsNull(fixture.Cells.GetOccupant(cell));
            }
        }

        // ---- The Core's own ground ----

        /// <summary>
        /// <b>Derived ore may not appear inside the Core's furthest reach.</b> That ground is placed
        /// by hand, at chosen distances, because the introduction depends on it - and the first
        /// sortie of a fresh run put random ore a few cells from the base, which is a different game
        /// rather than a near miss.
        ///
        /// A sector is skipped when <b>any part of it</b> falls inside the radius, not when its
        /// centre does: a 16-cell sector whose centre clears the radius still has a near edge well
        /// inside it, and a clipped cluster would be two cells against a wall.
        /// </summary>
        [Test]
        public void NoDerivedOreLandsInsideTheCoresReach()
        {
            Fixture fixture = NewFixture();

            var centre = new Vector2(MapSize / 2f, MapSize / 2f);

            var guarded = new SectorMaterialisation(
                fixture.Sectors, fixture.Cells, fixture.Catalog, fixture.Ores, fixture.World,
                centre, ExclusionRadius);

            int coreSector = fixture.Sectors.IndexAt(new GridCoord((int)centre.x, (int)centre.y));
            Assert.IsTrue(guarded.ReachesIntoTheCoresGround(coreSector), "the Core's own sector, obviously");

            // Every sector of the 5x5 block around the Core: the exclusion is 32 cells and a sector
            // is 16, so two rings out is where it can start clearing.
            int column = fixture.Sectors.ColumnOf(coreSector);
            int row = fixture.Sectors.RowOf(coreSector);

            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -3; dy <= 3; dy++)
                {
                    int index = fixture.Sectors.IndexAt(column + dx, row + dy);
                    if (index < 0) continue;

                    guarded.Materialise(index);
                }
            }

            foreach (DepositRuntime deposit in fixture.World.OreDeposits)
            {
                float distance = Vector2.Distance(
                    new Vector2(deposit.Origin.X + 0.5f, deposit.Origin.Y + 0.5f), centre);

                Assert.GreaterOrEqual(distance, ExclusionRadius,
                    $"a derived deposit landed {distance:F1} cells from the Core, inside its {ExclusionRadius}-cell reach");
            }
        }

        /// <summary>
        /// And it does clear: the exclusion is a ring, not a ban. A sector far from the Core is
        /// materialised normally, or the rule would have quietly turned the derivation off.
        /// </summary>
        [Test]
        public void FarFromTheCore_TheExclusionDoesNothing()
        {
            Fixture fixture = NewFixture();

            var guarded = new SectorMaterialisation(
                fixture.Sectors, fixture.Cells, fixture.Catalog, fixture.Ores, fixture.World,
                new Vector2(MapSize / 2f, MapSize / 2f), ExclusionRadius);

            int far = SectorWithDeposits(fixture);
            Assert.IsFalse(guarded.ReachesIntoTheCoresGround(far));
            Assert.Greater(guarded.Materialise(far), 0);
        }
    }
}