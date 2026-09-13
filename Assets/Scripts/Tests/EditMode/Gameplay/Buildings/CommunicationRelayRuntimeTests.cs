using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class CommunicationRelayRuntimeTests
    {
        ComputeSystem _compute;
        PowerSystem _power;

        [SetUp]
        public void SetUp()
        {
            _compute = new ComputeSystem();
            _power = new PowerSystem();
        }

        CommunicationRelayRuntime NewRelay(int actionRadiusCells = 12, float powerDemandKw = 0f, float cuUpkeepPerSecond = 0f)
        {
            CommunicationRelayDefinition definition = TestDataFactory.NewCommunicationRelay(actionRadiusCells, powerDemandKw, cuUpkeepPerSecond);
            return new CommunicationRelayRuntime(definition, new GridCoord(0, 0), Direction.North, _compute, _power);
        }

        /// <summary>Same priming a power-consuming test always needs (FoundryRuntimeTests): a draw has to be asked for once before Settle has anything to allocate.</summary>
        void SupplyPower(CommunicationRelayRuntime relay, float kilowatts = 9999f)
        {
            _power.ReportSupply(kilowatts);
            _power.TryDraw(relay.Definition.Id, kilowatts);
            _power.Settle();
        }

        [Test]
        public void ZeroKwZeroCu_IsActiveAfterOneTick_NoPowerOrComputeNeeded()
        {
            CommunicationRelayRuntime relay = NewRelay();

            relay.Tick(1f);

            Assert.IsTrue(relay.IsActive);
        }

        [Test]
        public void Unpowered_IsNotActive_AndSpendsNoCu()
        {
            CommunicationRelayRuntime relay = NewRelay(powerDemandKw: 3f, cuUpkeepPerSecond: 1f);
            float before = _compute.Reserve;

            relay.Tick(1f); // no ReportSupply/Settle - nothing to draw from

            Assert.IsFalse(relay.IsActive);
            Assert.AreEqual(before, _compute.Reserve, "All-or-nothing: unpowered spends no CU either.");
        }

        [Test]
        public void PoweredButNoCu_IsNotActive()
        {
            CommunicationRelayRuntime relay = NewRelay(powerDemandKw: 3f, cuUpkeepPerSecond: 1f);
            SupplyPower(relay);
            _compute.Spend(_compute.Reserve); // reserve now exactly 0

            relay.Tick(1f);

            Assert.IsFalse(relay.IsActive);
        }

        [Test]
        public void PoweredAndFed_IsActive_AndSpendsExactlyItsUpkeep()
        {
            CommunicationRelayRuntime relay = NewRelay(powerDemandKw: 3f, cuUpkeepPerSecond: 2f);
            SupplyPower(relay);
            float before = _compute.Reserve;

            relay.Tick(1.5f);

            Assert.IsTrue(relay.IsActive);
            Assert.AreEqual(before - 3f, _compute.Reserve, 0.001f, "2 CU/s for 1.5s = 3 CU, taken in full.");
        }

        [Test]
        public void ActionRadiusCells_ReadsTheDefinitionsValue()
        {
            CommunicationRelayRuntime relay = NewRelay(actionRadiusCells: 12);

            Assert.AreEqual(12, relay.ActionRadiusCells);
        }

        [Test]
        public void CaptureAndRestore_RoundTripsIsActive()
        {
            CommunicationRelayRuntime relay = NewRelay();
            relay.Tick(1f);
            Assert.IsTrue(relay.IsActive, "Precondition.");

            var captured = relay.CaptureState();

            CommunicationRelayRuntime restored = NewRelay();
            Assert.IsFalse(restored.IsActive, "Precondition: a fresh one has not ticked yet.");

            restored.RestoreState(captured);

            Assert.IsTrue(restored.IsActive);
        }

        [Test]
        public void RestoreState_TreatsANullOrAbsentBlobAsInactive()
        {
            CommunicationRelayRuntime relay = NewRelay();

            relay.RestoreState(new Newtonsoft.Json.Linq.JObject());

            Assert.IsFalse(relay.IsActive, "Absent means inactive until the next real Tick decides for itself.");
        }
    }
}
