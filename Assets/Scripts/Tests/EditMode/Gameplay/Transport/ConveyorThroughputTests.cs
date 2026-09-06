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
    /// What a belt actually carries, measured on a belt actually running - never against the same
    /// arithmetic that produces the constant. A figure checked only against its own formula agrees
    /// with itself while both drift away from the simulation, which is how a displayed number starts
    /// lying. This one caught its formula being 6% optimistic on the first run.
    /// </summary>
    public class ConveyorThroughputTests
    {
        const float TickSeconds = 1f / 60f;

        /// <summary>
        /// A belt fed as fast as it will accept and emptied as fast as it delivers - its own ceiling,
        /// with nothing upstream or downstream limiting it. Warms up first, then counts a clean
        /// minute: the cold start costs the time the first item needs to cross the cell, which would
        /// otherwise be charged against the rate.
        /// </summary>
        static int SteadyStateItemsPerMinute(ConveyorRuntime belt, TransportSystem transport)
        {
            int ticksPerMinute = Mathf.RoundToInt(60f / TickSeconds);
            int delivered = 0;

            for (int tick = 0; tick < ticksPerMinute * 2; tick++)
            {
                transport.Tick(TickSeconds);

                object atFront = belt.PeekPullableItem();
                if (atFront != null)
                {
                    belt.ConsumePulledItem(atFront);
                    if (tick >= ticksPerMinute) delivered++;
                }

                if (belt.HasRoomForNewItem) belt.ReceiveItem(new object());
            }

            return delivered;
        }

        static ConveyorRuntime Register(GridRuntime grid, TransportSystem transport, ConveyorRuntime belt)
        {
            grid.SetOccupant(belt.Cell, belt);
            transport.Register(belt);
            return belt;
        }

        static ConveyorRuntime NewStraight(GridRuntime grid, TransportSystem transport)
        {
            var definition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            var belt = new ConveyorRuntime(definition, new GridCoord(0, 0), Direction.East);
            belt.ConfigureAsStraight(Direction.East);
            return Register(grid, transport, belt);
        }

        static ConveyorRuntime NewCorner(GridRuntime grid, TransportSystem transport)
        {
            var definition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            var belt = new ConveyorRuntime(definition, new GridCoord(0, 0), Direction.East);
            belt.ConfigureAsCorner(Direction.South, Direction.East);
            return Register(grid, transport, belt);
        }

        [Test]
        public void ASaturatedStraight_CarriesTheCadenceTheConstantsImply()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            int measured = SteadyStateItemsPerMinute(NewStraight(grid, transport), transport);

            Assert.AreEqual(TransportSystem.ConveyorItemsPerMinute, measured, 1f,
                "A belt takes one item per intake interval, whatever is trying to feed it.");
        }

        /// <summary>
        /// Straight and corner are the same belt - one cell at one speed - and that turning a line
        /// costs nothing in throughput is itself the useful answer. The day a corner is given a speed
        /// of its own, this is what says the menu needs two figures rather than one.
        /// </summary>
        [Test]
        public void ACorner_CarriesAsMuchAsAStraight()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);

            int measured = SteadyStateItemsPerMinute(NewCorner(grid, transport), transport);

            Assert.AreEqual(TransportSystem.ConveyorItemsPerMinute, measured, 1f);
        }

        /// <summary>
        /// The quoted figure is the belt's intake rate, not a value of its own, and it matches the
        /// cap on a raw production output - the two are balanced against each other, so one source
        /// fills exactly one belt.
        /// </summary>
        [Test]
        public void TheQuotedFigure_IsTheIntakeRate_AndMatchesARawOutput()
        {
            Assert.AreEqual(60f / ConveyorRuntime.IntakeIntervalSeconds, TransportSystem.ConveyorItemsPerMinute, 0.0001f);
            Assert.AreEqual(TransportSystem.RawOutputPullIntervalSeconds, ConveyorRuntime.IntakeIntervalSeconds, 0.0001f,
                "A production building's output and the belt carrying it away are rated the same.");
        }

        /// <summary>
        /// Metering the intake must not have slowed anything down: an item still crosses its cell at
        /// the full belt speed. A belt that took one item per second by moving at a third of its
        /// speed would be a different, much worse change with the same throughput.
        /// </summary>
        [Test]
        public void MeteringTheIntake_DoesNotSlowWhatIsAlreadyOnTheBelt()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            ConveyorRuntime belt = NewStraight(grid, transport);

            belt.ReceiveItem(new object());

            int ticks = 0;
            while (belt.PeekPullableItem() == null && ticks < 600)
            {
                transport.Tick(TickSeconds);
                ticks++;
            }

            float crossingSeconds = ticks * TickSeconds;
            Assert.AreEqual(1f / TransportSystem.ConveyorSpeedCellsPerSecond, crossingSeconds, 0.02f,
                "One cell at 1.5 cells/s is still two thirds of a second.");
        }

        /// <summary>
        /// The jam buffer survives the metering: a blocked belt still packs items up against each
        /// other instead of holding one at a time. That is what MaxItemsPerCell is for, and it is a
        /// buffer rather than a rate.
        /// </summary>
        [Test]
        public void ABlockedBelt_StillPacksItemsUpBehindTheBlockage()
        {
            var grid = new GridRuntime(1f);
            var transport = new TransportSystem(grid);
            ConveyorRuntime belt = NewStraight(grid, transport);

            // Nothing consumes the front, so the line ahead is blocked.
            for (int tick = 0; tick < 60 * 10; tick++)
            {
                transport.Tick(TickSeconds);
                if (belt.HasRoomForNewItem) belt.ReceiveItem(new object());
            }

            Assert.AreEqual(ConveyorRuntime.MaxItemsPerCell, belt.Items.Count,
                "A jam fills the belt to its buffer, which metering the intake must not have taken away.");
        }

    }
}
