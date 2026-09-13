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

            Assert.IsNull(building.PeekPullableItem());
            Assert.DoesNotThrow(() => building.ConsumePulledItem(new object()));
        }
    }
}
