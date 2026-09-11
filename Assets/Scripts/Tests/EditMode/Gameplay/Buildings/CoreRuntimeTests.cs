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

        /// <summary>A test research carrying one radius target. Named for what it does, never after a shipped research: the id is not what the effect hangs on.</summary>
        static ResearchDefinition Radius(int cells)
            => TestDataFactory.WithEffects(TestDataFactory.NewResearch("radius_" + cells, 10f), new ResearchEffect(ResearchEffectKind.ActionRadius, value: cells));

        static ResearchSystem Knowing(params ResearchDefinition[] known) => new ResearchSystem(new ComputeSystem(), new ResearchCatalog(known));

        [Test]
        public void Constructor_StartsAtTheDefinitionsActionRadius()
        {
            var core = NewCoreRuntime(Knowing(), startingRadius: 22);

            Assert.AreEqual(22, core.ActionRadiusCells);
        }

        [Test]
        public void ARadiusEffect_GrowsTheRadiusToItsTarget()
        {
            ResearchDefinition radius42 = Radius(42);
            var research = Knowing(radius42);
            var core = NewCoreRuntime(research);

            research.Enqueue(radius42);
            research.Tick(60f);

            Assert.AreEqual(42, core.ActionRadiusCells);
        }

        /// <summary>Each research sets its own target and the highest completed wins: a lower one landing after a higher one never shrinks anything.</summary>
        [Test]
        public void RadiusTargets_TheHighestReachedWins_WhateverTheOrder()
        {
            ResearchDefinition radius42 = Radius(42), radius60 = Radius(60), radius80 = Radius(80);
            var research = Knowing(radius42, radius60, radius80);
            var core = NewCoreRuntime(research);

            research.Grant(radius60.Id);
            Assert.AreEqual(60, core.ActionRadiusCells);

            research.Grant(radius80.Id);
            Assert.AreEqual(80, core.ActionRadiusCells);

            research.Grant(radius42.Id);
            Assert.AreEqual(80, core.ActionRadiusCells, "A lower target after a higher one changes nothing.");
        }

        [Test]
        public void AResearchWithoutARadiusEffect_DoesNotChangeTheRadius()
        {
            ResearchDefinition unrelated = TestDataFactory.NewResearch("unrelated", 10f);
            var research = Knowing(unrelated);
            var core = NewCoreRuntime(research);

            research.Enqueue(unrelated);
            research.Tick(60f);

            Assert.AreEqual(22, core.ActionRadiusCells);
        }

        [Test]
        public void OnUnregistered_StopsReactingToFutureResearchCompletions()
        {
            ResearchDefinition radius42 = Radius(42);
            var research = Knowing(radius42);
            var core = NewCoreRuntime(research);
            core.OnUnregistered();

            research.Enqueue(radius42);
            research.Tick(60f);

            Assert.AreEqual(22, core.ActionRadiusCells);
        }

        [Test]
        public void CaptureState_IncludesActionRadiusCells()
        {
            ResearchDefinition radius42 = Radius(42);
            var research = Knowing(radius42);
            var core = NewCoreRuntime(research);
            research.Enqueue(radius42);
            research.Tick(60f);

            var state = core.CaptureState();

            Assert.AreEqual(42, state.Value<int?>("actionRadiusCells"));
        }

        [Test]
        public void CaptureAndRestore_RoundTripsActionRadiusCells()
        {
            ResearchDefinition radius42 = Radius(42);
            var research = Knowing(radius42);
            var original = NewCoreRuntime(research);
            research.Enqueue(radius42);
            research.Tick(60f);

            var state = original.CaptureState();
            var restored = NewCoreRuntime(Knowing());
            restored.RestoreState(state);

            Assert.AreEqual(42, restored.ActionRadiusCells);
        }

        [Test]
        public void RestoreState_ToleratesABlobMissingActionRadiusCells_FallsBackToTheDefinitionsStartingValue()
        {
            var core = NewCoreRuntime(Knowing(), startingRadius: 22);

            Assert.DoesNotThrow(() => core.RestoreState(new Newtonsoft.Json.Linq.JObject()));
            Assert.AreEqual(22, core.ActionRadiusCells);
        }
    }
}
