using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Power
{
    /// <summary>
    /// The pole network graph (ENERGIE.md): connection ("nearest of each distinct group in range"),
    /// merging, splitting on removal, and the save/load equivalence that comes from the graph being
    /// entirely derived rather than stored (PoleRuntime.NetworkId).
    /// </summary>
    public class PoleNetworkSystemTests
    {
        GridRuntime _grid;
        PoleDefinition _poleDefinition;
        PoleNetworkSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridRuntime(1f);
            _poleDefinition = TestDataFactory.NewPoleDefinition();
            _settings = TestDataFactory.NewPoleNetworkSettings(powerRangeCells: 2, connectionRangeCells: 8);
        }

        PoleRuntime NewPole(GridCoord cell) => new PoleRuntime(_poleDefinition, cell, Direction.North);

        // ---- The connection rule ----

        [Test]
        public void ThreePolesInALine_ProducesTwoCablesNotThree()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime a = NewPole(new GridCoord(0, 0));
            PoleRuntime b = NewPole(new GridCoord(5, 0));
            PoleRuntime c = NewPole(new GridCoord(10, 0));

            network.RegisterPole(a);
            network.RegisterPole(b);
            network.RegisterPole(c);

            Assert.AreEqual(2, network.Edges.Count, "the third pole must connect only to the second (its nearest), not redundantly to the first as well");
            Assert.AreEqual(a.NetworkId, b.NetworkId);
            Assert.AreEqual(b.NetworkId, c.NetworkId);
        }

        [Test]
        public void AnIsolatedPole_StartsItsOwnNetworkWithNoCable()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime lone = NewPole(new GridCoord(0, 0));

            network.RegisterPole(lone);

            Assert.AreEqual(0, network.Edges.Count);
            Assert.AreNotEqual(-1, lone.NetworkId);
        }

        [Test]
        public void TwoPolesOfTheSameNetworkAlreadyConnected_DoNotConnectASecondTime()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime a = NewPole(new GridCoord(0, 0));
            PoleRuntime b = NewPole(new GridCoord(5, 0));
            network.RegisterPole(a);
            network.RegisterPole(b);
            Assert.AreEqual(1, network.Edges.Count, "Precondition.");

            // A third pole in range of both a and b - both already the same group, so only one cable.
            PoleRuntime c = NewPole(new GridCoord(2, 4));
            network.RegisterPole(c);

            Assert.AreEqual(2, network.Edges.Count, "c connects once to its single nearest neighbour in the one group in range, not once per pole in it");
        }

        [Test]
        public void APoleBeyondConnectionRange_ButWithinPowerRange_StaysUnconnected()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime a = NewPole(new GridCoord(0, 0));
            PoleRuntime b = NewPole(new GridCoord(9, 0)); // 9 > ConnectionRangeCells (8)

            network.RegisterPole(a);
            network.RegisterPole(b);

            Assert.AreEqual(0, network.Edges.Count);
            Assert.AreNotEqual(a.NetworkId, b.NetworkId);
        }

        // ---- Merging ----

        [Test]
        public void APoleBetweenTwoGroups_CreatesTwoCablesAndMergesThem()
        {
            var network = new PoleNetworkSystem(_grid, _settings);

            PoleRuntime g1A = NewPole(new GridCoord(0, 0));
            PoleRuntime g1B = NewPole(new GridCoord(5, 0));
            network.RegisterPole(g1A);
            network.RegisterPole(g1B);

            PoleRuntime g2A = NewPole(new GridCoord(20, 0));
            PoleRuntime g2B = NewPole(new GridCoord(25, 0));
            network.RegisterPole(g2A);
            network.RegisterPole(g2B);

            Assert.AreNotEqual(g1A.NetworkId, g2A.NetworkId, "Precondition: two separate networks so far.");
            Assert.AreEqual(2, network.Edges.Count, "Precondition: one cable per group so far.");

            // 7 from g1B (nearest of group 1), 8 from g2A (nearest of group 2) - within
            // ConnectionRangeCells (8) of both.
            PoleRuntime bridge = NewPole(new GridCoord(12, 0));
            network.RegisterPole(bridge);

            Assert.AreEqual(4, network.Edges.Count, "one new cable to each of the two groups found");
            Assert.AreEqual(g1A.NetworkId, g2A.NetworkId, "both original groups now share one network");
            Assert.AreEqual(g1A.NetworkId, bridge.NetworkId);
            Assert.AreEqual(g1A.NetworkId, g1B.NetworkId);
            Assert.AreEqual(g2A.NetworkId, g2B.NetworkId);
        }

        // ---- Splitting on removal ----

        [Test]
        public void RemovingAMiddlePole_SplitsTheNetwork()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime a = NewPole(new GridCoord(0, 0));
            PoleRuntime b = NewPole(new GridCoord(5, 0));
            PoleRuntime c = NewPole(new GridCoord(10, 0));
            network.RegisterPole(a);
            network.RegisterPole(b);
            network.RegisterPole(c);
            Assert.AreEqual(a.NetworkId, c.NetworkId, "Precondition: one network.");

            network.UnregisterPole(b);

            Assert.AreEqual(0, network.Edges.Count, "both of b's cables are gone with it");
            Assert.AreNotEqual(a.NetworkId, c.NetworkId, "the tree was cut in the middle - a and c no longer share a network");
            Assert.AreEqual(2, network.Poles.Count);
        }

        [Test]
        public void RemovingAnEndPole_LeavesTheRestOfTheNetworkIntact()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime a = NewPole(new GridCoord(0, 0));
            PoleRuntime b = NewPole(new GridCoord(5, 0));
            PoleRuntime c = NewPole(new GridCoord(10, 0));
            network.RegisterPole(a);
            network.RegisterPole(b);
            network.RegisterPole(c);

            network.UnregisterPole(a);

            Assert.AreEqual(1, network.Edges.Count);
            Assert.AreEqual(b.NetworkId, c.NetworkId);
        }

        // ---- Save/load equivalence (PoleRuntime.NetworkId is derived, never persisted) ----

        [Test]
        public void ReplayingRegistrationInSaveOrder_ReproducesTheSameGroups()
        {
            var original = new PoleNetworkSystem(_grid, _settings);
            var positions = new[] { new GridCoord(0, 0), new GridCoord(5, 0), new GridCoord(10, 0), new GridCoord(30, 0) };

            var originalPoles = new PoleRuntime[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                originalPoles[i] = NewPole(positions[i]);
                original.RegisterPole(originalPoles[i]);
            }

            // The fourth pole (x=30) is out of range of everything - its own isolated network.
            Assert.AreEqual(originalPoles[0].NetworkId, originalPoles[2].NetworkId, "Precondition: first three share a network.");
            Assert.AreNotEqual(originalPoles[0].NetworkId, originalPoles[3].NetworkId, "Precondition: the fourth is isolated.");

            // A fresh system, and fresh PoleRuntime instances at the same cells in the same order -
            // exactly what restoring from a save does, since only positions/order are ever persisted.
            var reloaded = new PoleNetworkSystem(_grid, _settings);
            var reloadedPoles = new PoleRuntime[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                reloadedPoles[i] = NewPole(positions[i]);
                reloaded.RegisterPole(reloadedPoles[i]);
            }

            Assert.AreEqual(original.Edges.Count, reloaded.Edges.Count);
            Assert.AreEqual(reloadedPoles[0].NetworkId, reloadedPoles[2].NetworkId, "the same three poles share a network again");
            Assert.AreNotEqual(reloadedPoles[0].NetworkId, reloadedPoles[3].NetworkId, "the fourth is isolated again");
        }

        // ---- Fed state and the power-range/connection-range distinction ----

        BuildingRuntime PlacePowerSource(GridCoord cell)
        {
            var definition = ScriptableObject.CreateInstance<PowerplantGazDefinition>();
            var source = new BuildingRuntime(definition, cell, Direction.North);
            _grid.SetOccupant(cell, source);
            return source;
        }

        [Test]
        public void ANetworkWithNoSourceNearby_IsNotFed()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime pole = NewPole(new GridCoord(0, 0));
            network.RegisterPole(pole);

            network.Tick(1f);

            Assert.IsFalse(network.IsNetworkFed(pole.NetworkId));
        }

        [Test]
        public void ASourceWithinPowerRangeOfAnyPole_FeedsTheWholeNetwork()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime a = NewPole(new GridCoord(0, 0));
            PoleRuntime b = NewPole(new GridCoord(5, 0)); // within ConnectionRangeCells of a
            network.RegisterPole(a);
            network.RegisterPole(b);

            PlacePowerSource(new GridCoord(6, 0)); // within PowerRangeCells (2) of b only

            network.Tick(1f);

            Assert.IsTrue(network.IsNetworkFed(a.NetworkId), "the whole network is fed, not just the pole next to the source");
            Assert.IsTrue(network.IsNetworkFed(b.NetworkId));
        }

        [Test]
        public void ASourceBeyondPowerRange_DoesNotFeedEvenAnAdjacentPole()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            PoleRuntime pole = NewPole(new GridCoord(0, 0));
            network.RegisterPole(pole);

            PlacePowerSource(new GridCoord(4, 0)); // 4 > PowerRangeCells (2)

            network.Tick(1f);

            Assert.IsFalse(network.IsNetworkFed(pole.NetworkId));
        }

        // ---- Gating a consumer's actual power draw (BuildingRuntime.PoleNetwork) ----

        [Test]
        public void AConsumerOutsideEveryFedPole_NeverDraws_WhateverSupplyExists()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            var compute = new ComputeSystem();
            var power = new PowerSystem();
            power.ReportSupply(100f);
            power.Settle();

            CommunicationRelayDefinition relayDefinition = TestDataFactory.NewCommunicationRelay(actionRadiusCells: 12, powerDemandKw: 3f);
            var relay = new CommunicationRelayRuntime(relayDefinition, new GridCoord(50, 50), Direction.North, compute, power)
            {
                PoleNetwork = network
            };

            relay.Tick(1f);

            Assert.IsFalse(relay.IsActive, "no pole reaches it, so it must not draw, whatever supply exists");
        }

        [Test]
        public void AConsumerWithinAFedPolesReach_Draws()
        {
            var network = new PoleNetworkSystem(_grid, _settings);
            var compute = new ComputeSystem();
            var power = new PowerSystem();

            CommunicationRelayDefinition relayDefinition = TestDataFactory.NewCommunicationRelay(actionRadiusCells: 12, powerDemandKw: 3f);
            var relay = new CommunicationRelayRuntime(relayDefinition, new GridCoord(50, 50), Direction.North, compute, power)
            {
                PoleNetwork = network
            };

            // A fed pole within PowerRangeCells (2) of the relay's 2x2 footprint (cells 50,50 - 51,51).
            PoleRuntime pole = NewPole(new GridCoord(52, 50));
            network.RegisterPole(pole);
            PlacePowerSource(new GridCoord(53, 50));
            network.Tick(1f);

            // Same priming CommunicationRelayRuntimeTests.SupplyPower uses: a draw has to be asked
            // for once before Settle has anything to allocate, and ReportSupply is consumed by every
            // Settle (a real Powerplant calls it every Tick, this test calls it once).
            power.ReportSupply(3f);
            power.TryDraw(relayDefinition.Id, 3f);
            power.Settle();

            relay.Tick(1f);

            Assert.IsTrue(relay.IsActive, "within range of a pole whose network is fed");
        }
    }
}
