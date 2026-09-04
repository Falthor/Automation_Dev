using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class StorageRuntimeTests
    {
        static StorageDefinition NewStorageDefinition(float intakeIntervalSeconds)
        {
            var definition = ScriptableObject.CreateInstance<StorageDefinition>();
            var so = new SerializedObject(definition);
            so.FindProperty("intakeIntervalSeconds").floatValue = intakeIntervalSeconds;
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        [Test]
        public void AddInput_ThenImmediateSecondDelivery_IsRefusedByTheIntakeCooldown()
        {
            // Reproduces the reported bug: a Storage placed directly against a production
            // building's raw pooled output (no conveyor, no belt-speed gating in between) could
            // drain it in a single tick. IntakeIntervalSeconds caps Storage's own absorption at
            // the fastest conveyor's throughput regardless of what feeds it.
            var storage = new StorageRuntime(NewStorageDefinition(1f), new GridCoord(0, 0), Direction.North);

            Assert.IsTrue(storage.CanAcceptInput("iron_ore", 1, Direction.South));
            storage.AddInput("iron_ore", 1, Direction.South);

            Assert.IsFalse(storage.CanAcceptInput("iron_ore", 1, Direction.South), "A second delivery within the same tick must be refused.");
        }

        [Test]
        public void Tick_PastTheIntakeInterval_AllowsAnotherDelivery()
        {
            var storage = new StorageRuntime(NewStorageDefinition(1f), new GridCoord(0, 0), Direction.North);
            storage.AddInput("iron_ore", 1, Direction.South);

            storage.Tick(0.5f);
            Assert.IsFalse(storage.CanAcceptInput("iron_ore", 1, Direction.South), "Half the interval must not be enough yet.");

            storage.Tick(0.51f);
            Assert.IsTrue(storage.CanAcceptInput("iron_ore", 1, Direction.South), "Once the full interval has elapsed, another delivery must be accepted.");
        }

        [Test]
        public void IntakeCooldown_DoesNotBlockFirstDelivery()
        {
            var storage = new StorageRuntime(NewStorageDefinition(1f), new GridCoord(0, 0), Direction.North);
            Assert.IsTrue(storage.CanAcceptInput("iron_ore", 1, Direction.South));
        }
    }
}
