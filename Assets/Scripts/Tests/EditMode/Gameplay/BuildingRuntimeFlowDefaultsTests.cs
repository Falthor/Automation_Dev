using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay
{
    public class BuildingRuntimeFlowDefaultsTests
    {
        class DummyDefinition : BuildingDefinition
        {
        }

        [Test]
        public void BuildingRuntime_FlowContract_DefaultsToNeutral()
        {
            var definition = ScriptableObject.CreateInstance<DummyDefinition>();
            var building = new BuildingRuntime(definition, new GridCoord(0, 0), Direction.North);

            Assert.IsFalse(building.IsFlowReceiver());
            Assert.IsNull(building.PeekPullableItem());
            Assert.DoesNotThrow(() => building.ConsumePulledItem(new object()));
        }

        [Test]
        public void ConveyorRuntime_IsFlowReceiver()
        {
            var definition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            var conveyor = new ConveyorRuntime(definition, new GridCoord(0, 0), Direction.North);

            Assert.IsTrue(conveyor.IsFlowReceiver());
        }
    }
}
