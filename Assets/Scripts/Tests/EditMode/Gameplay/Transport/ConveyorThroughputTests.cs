using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Transport;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Transport
{
    /// <summary>
    /// What a belt line carries, and how it carries it.
    ///
    /// Both halves matter and only one of them shows on a throughput graph. Metering each belt at the
    /// line's own rate gave the right items-per-minute and made every item stop dead at every cell
    /// boundary - an item reaches the seam two thirds of a second after entering, and a one-second
    /// gate makes it wait out the remaining third, forever, at every belt. TransitIsContinuous is the
    /// test that would have caught it.
    /// </summary>
    public class ConveyorThroughputTests
    {
        const float TickSeconds = 1f / 60f;

        static ConveyorRuntime AddBelt(GridRuntime grid, TransportSystem transport, GridCoord cell, Direction exit)
        {
            var definition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            var belt = new ConveyorRuntime(definition, cell, exit);
            belt.ConfigureAsStraight(exit);
            grid.SetOccupant(cell, belt);
            transport.Register(belt);
            return belt;
        }

        /// <summary>
        /// One item, one line, nothing else moving: how long it takes to travel end to end. With
        /// nothing metering the seams that is exactly the time it takes to cross the cells at the
        /// belt speed, and any waiting at a boundary shows up here as extra seconds.
        /// </summary>
        static float TransitSecondsAcross(int beltCount)
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            var belts = new ConveyorRuntime[beltCount];
            for (int i = 0; i < beltCount; i++)
            {
                belts[i] = AddBelt(grid, transport, new GridCoord(i, 0), Direction.East);
            }

            belts[0].ReceiveItem(new object());

            int ticks = 0;
            while (belts[beltCount - 1].PeekPullableItem() == null && ticks < 60 * 60)
            {
                transport.Tick(TickSeconds);
                ticks++;
            }

            return ticks * TickSeconds;
        }

        [Test]
        public void TransitIsContinuous_AnItemNeverWaitsAtACellBoundary()
        {
            float oneCell = 1f / TransportSystem.ConveyorSpeedCellsPerSecond;

            // One tick of slack per seam: an item becomes handoverable when it reaches the end of a
            // cell, and the pass that moves it on runs on the next tick. That is granularity, not
            // waiting - the per-belt meter this replaced cost a third of a second at every seam,
            // which is twenty times larger and is what the tolerances below would catch.
            float tick = TickSeconds;

            Assert.AreEqual(oneCell, TransitSecondsAcross(1), 2f * tick);
            Assert.AreEqual(3f * oneCell, TransitSecondsAcross(3), 4f * tick,
                "Three cells cost three crossings and nothing else - no pause at either seam.");
            Assert.AreEqual(6f * oneCell, TransitSecondsAcross(6), 7f * tick,
                "And the cost stays linear however long the line is.");
        }

        /// <summary>
        /// The rate a real line runs at, measured through the path a real line uses: a source that is
        /// not itself a belt, handing over at RawOutputPullIntervalSeconds. Nothing on the belt meters
        /// anything - what sets the cadence is upstream, which is the whole design.
        /// </summary>
        [Test]
        public void ALineFedByASource_CarriesWhatTheMenuQuotes()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            ItemDefinition ore = Game.Tests.EditMode.TestSupport.TestDataFactory.NewItem("iron_ore");
            FoundryDefinition sourceDefinition = Game.Tests.EditMode.TestSupport.TestDataFactory.NewFoundry(
                powerDemandKw: 0f, intakeIntervalSeconds: 0f);

            var source = new FoundryRuntime(sourceDefinition, new GridCoord(0, 0), Direction.East,
                null, null, new Game.Gameplay.Compute.ComputeSystem(), new Game.Gameplay.Power.PowerSystem(), null);
            grid.SetOccupantFootprint(source.Cell, sourceDefinition.FootprintSize, source);
            transport.Register(source);
            source.AddOutput(ore.Id, 10000);

            ConveyorRuntime last = null;
            for (int i = 1; i <= 3; i++) last = AddBelt(grid, transport, new GridCoord(i, 0), Direction.East);

            int delivered = 0;
            int ticks = Mathf.RoundToInt(60f / TickSeconds);
            for (int tick = 0; tick < ticks * 2; tick++)
            {
                transport.Tick(TickSeconds);

                object atEnd = last.PeekPullableItem();
                if (atEnd != null)
                {
                    last.ConsumePulledItem(atEnd);
                    if (tick >= ticks) delivered++;
                }
            }

            Assert.AreEqual(TransportSystem.ConveyorItemsPerMinute, delivered, 1f,
                $"A real line carries {delivered}/min and the Building menu says {TransportSystem.ConveyorItemsPerMinute:0}/min.");
        }

        [Test]
        public void TheQuotedFigure_IsTheRateOfWhateverFeedsTheLine()
        {
            Assert.AreEqual(60f / TransportSystem.RawOutputPullIntervalSeconds,
                TransportSystem.ConveyorItemsPerMinute, 0.0001f);
        }

        /// <summary>
        /// The jam buffer: with the line ahead blocked, items pack up against each other instead of
        /// the belt holding one at a time. That is what MaxItemsPerCell is for - a buffer, never a
        /// rate - and it is also what lets a line clear a bottleneck at full speed rather than
        /// trickling out of it.
        /// </summary>
        [Test]
        public void ABlockedBelt_PacksItemsUpBehindTheBlockage()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            ConveyorRuntime belt = AddBelt(grid, transport, new GridCoord(0, 0), Direction.East);

            for (int tick = 0; tick < 60 * 5; tick++)
            {
                transport.Tick(TickSeconds);
                if (belt.HasRoomForNewItem) belt.ReceiveItem(new object());
            }

            Assert.AreEqual(ConveyorRuntime.MaxItemsPerCell, belt.Items.Count);
        }

        /// <summary>
        /// Coming out of a bottleneck, the packed items must leave at the belt's own speed rather
        /// than being metered back down to the line's rate - "60/min même en cas de goulot" means the
        /// line recovers, not that it trickles.
        /// </summary>
        [Test]
        public void ClearingAJam_LetsThePackedItemsLeaveAtBeltSpeed()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            ConveyorRuntime belt = AddBelt(grid, transport, new GridCoord(0, 0), Direction.East);

            for (int tick = 0; tick < 60 * 5; tick++)
            {
                transport.Tick(TickSeconds);
                if (belt.HasRoomForNewItem) belt.ReceiveItem(new object());
            }

            // The blockage clears: drain the belt and time how fast the queue comes off it.
            int drained = 0;
            int ticks = 0;
            while (belt.Items.Count > 0 && ticks < 60 * 10)
            {
                transport.Tick(TickSeconds);
                ticks++;

                object atFront = belt.PeekPullableItem();
                if (atFront != null)
                {
                    belt.ConsumePulledItem(atFront);
                    drained++;
                }
            }

            Assert.AreEqual(ConveyorRuntime.MaxItemsPerCell, drained, "The whole queue comes off.");

            float perItem = ticks * TickSeconds / drained;
            float spacingTravelTime = 1f / ConveyorRuntime.MaxItemsPerCell / TransportSystem.ConveyorSpeedCellsPerSecond;
            Assert.LessOrEqual(perItem, spacingTravelTime * 1.5f,
                "A queue must drain at the speed the belt runs, not at the rate a source feeds it.");
        }
    }
}
