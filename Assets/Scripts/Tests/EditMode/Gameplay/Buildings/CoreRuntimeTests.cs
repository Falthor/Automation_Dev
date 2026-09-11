using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class CoreRuntimeTests
    {
        static CoreRuntime NewCoreRuntime(ResearchSystem research, int startingRadius = 22)
        {
            CoreDefinition definition = TestDataFactory.NewCore(startingRadius, new Vector2Int(4, 4));
            return new CoreRuntime(definition, new GridCoord(0, 0), Direction.North, new ComputeSystem(), new PowerSystem(), research);
        }

        [Test]
        public void Constructor_StartsAtTheDefinitionsActionRadius()
        {
            var core = NewCoreRuntime(new ResearchSystem(new ComputeSystem()), startingRadius: 22);

            Assert.AreEqual(22, core.ActionRadiusCells);
        }

        [Test]
        public void OnResearchCompleted_ExtendedBandwidth_GrowsRadiusTo42()
        {
            var research = new ResearchSystem(new ComputeSystem());
            var core = NewCoreRuntime(research);
            ResearchDefinition extendedBandwidth = TestDataFactory.NewResearch("extended_bandwidth", 10f);

            research.Enqueue(extendedBandwidth);
            research.Tick(60f);

            Assert.IsTrue(research.IsUnlocked("extended_bandwidth"));
            Assert.AreEqual(42, core.ActionRadiusCells);
        }

        /// <summary>Each level sets its own radius, the last one is the Core's furthest reach, and a lower level landing after a higher one never shrinks anything.</summary>
        [Test]
        public void TheBandwidthLevels_Reach60Then80_AndNeverShrinkTheRadius()
        {
            var research = new ResearchSystem(new ComputeSystem());
            var core = NewCoreRuntime(research);

            research.Grant("extended_bandwidth_2");
            Assert.AreEqual(60, core.ActionRadiusCells);

            research.Grant("extended_bandwidth_3");
            Assert.AreEqual(80, core.ActionRadiusCells);
            Assert.AreEqual(CoreRuntime.ExtendedActionRadiusCells, core.ActionRadiusCells, "The last level is the furthest reach world generation keeps clear.");

            research.Grant("extended_bandwidth");
            Assert.AreEqual(80, core.ActionRadiusCells, "A lower level after a higher one changes nothing.");
        }

        [Test]
        public void OnResearchCompleted_UnrelatedResearch_DoesNotChangeRadius()
        {
            var research = new ResearchSystem(new ComputeSystem());
            var core = NewCoreRuntime(research);
            ResearchDefinition unrelated = TestDataFactory.NewResearch("circuit_board", 10f);

            research.Enqueue(unrelated);
            research.Tick(60f);

            Assert.AreEqual(22, core.ActionRadiusCells);
        }

        [Test]
        public void OnUnregistered_StopsReactingToFutureResearchCompletions()
        {
            var research = new ResearchSystem(new ComputeSystem());
            var core = NewCoreRuntime(research);
            core.OnUnregistered();

            ResearchDefinition extendedBandwidth = TestDataFactory.NewResearch("extended_bandwidth", 10f);
            research.Enqueue(extendedBandwidth);
            research.Tick(60f);

            Assert.AreEqual(22, core.ActionRadiusCells);
        }

        [Test]
        public void CaptureState_IncludesActionRadiusCells()
        {
            var research = new ResearchSystem(new ComputeSystem());
            var core = NewCoreRuntime(research);
            ResearchDefinition extendedBandwidth = TestDataFactory.NewResearch("extended_bandwidth", 10f);
            research.Enqueue(extendedBandwidth);
            research.Tick(60f);

            var state = core.CaptureState();

            Assert.AreEqual(42, state.Value<int?>("actionRadiusCells"));
        }

        [Test]
        public void CaptureAndRestore_RoundTripsActionRadiusCells()
        {
            var research = new ResearchSystem(new ComputeSystem());
            var original = NewCoreRuntime(research);
            ResearchDefinition extendedBandwidth = TestDataFactory.NewResearch("extended_bandwidth", 10f);
            research.Enqueue(extendedBandwidth);
            research.Tick(60f);

            var state = original.CaptureState();
            var restored = NewCoreRuntime(new ResearchSystem(new ComputeSystem()));
            restored.RestoreState(state);

            Assert.AreEqual(42, restored.ActionRadiusCells);
        }

        [Test]
        public void RestoreState_ToleratesABlobMissingActionRadiusCells_FallsBackToTheDefinitionsStartingValue()
        {
            var core = NewCoreRuntime(new ResearchSystem(new ComputeSystem()), startingRadius: 22);

            Assert.DoesNotThrow(() => core.RestoreState(new Newtonsoft.Json.Linq.JObject()));
            Assert.AreEqual(22, core.ActionRadiusCells);
        }
    }
}
