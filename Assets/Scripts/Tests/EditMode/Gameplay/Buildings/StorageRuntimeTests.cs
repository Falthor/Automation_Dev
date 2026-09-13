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
        static StorageDefinition NewStorageDefinition() => ScriptableObject.CreateInstance<StorageDefinition>();

        /// <summary>
        /// A Storage has no absorption rate: it takes whatever arrives, the instant it arrives.
        /// A container's only limit is being full.
        /// </summary>
        [Test]
        public void DeliveriesAreAcceptedBackToBack()
        {
            var storage = new StorageRuntime(NewStorageDefinition(), new GridCoord(0, 0), Direction.North);

            Assert.IsTrue(storage.CanAcceptInput("iron_ore", 1, Direction.South));
            storage.AddInput("iron_ore", 1, Direction.South);

            Assert.IsTrue(storage.CanAcceptInput("iron_ore", 1, Direction.South), "A second delivery in the same tick must be accepted.");
            storage.AddInput("iron_ore", 1, Direction.South);

            Assert.IsTrue(storage.CanAcceptInput("iron_ore", 1, Direction.South), "And a third, without any tick in between.");
            Assert.AreEqual(2, storage.GetInputAmount("iron_ore"));
        }

        [Test]
        public void AFullStorageIsTheOnlyThingThatRefuses()
        {
            StorageDefinition definition = NewStorageDefinition();
            var so = new SerializedObject(definition);
            so.FindProperty("slotCountOverride").intValue = 1;
            so.FindProperty("capacityPerSlotOverride").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();

            var storage = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);
            storage.AddInput("iron_ore", 2, Direction.South);

            Assert.IsFalse(storage.CanAcceptInput("iron_ore", 1, Direction.South));
        }

        [Test]
        public void AConveyorRejectingStorageStillRefusesTheBelt()
        {
            StorageDefinition definition = NewStorageDefinition();
            var so = new SerializedObject(definition);
            so.FindProperty("rejectsConveyorInput").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var storage = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);

            Assert.IsFalse(storage.CanAcceptInput("iron_ore", 1, Direction.South), "Removing the cooldown must not also remove the conveyor lock.");
            Assert.IsTrue(storage.CanAcceptFromRobot("iron_ore", 1), "A robot was never subject to it.");
        }
    }
}
