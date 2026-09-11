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

        /// <summary>
        /// A Storage has no absorption rate. It used to carry the same intake cooldown a Foundry
        /// has, so a box parked against an output could not drain it faster than a belt would - but
        /// that also made the box slower than the belt feeding it, and items queued in front of a
        /// container that was visibly empty. A container's only limit is being full.
        ///
        /// The definition still carries an interval and it is deliberately non-zero here: the point
        /// is that nothing reads it any more, which a zero would not prove.
        /// </summary>
        [Test]
        public void DeliveriesAreAcceptedBackToBack_WhateverTheDefinitionsInterval()
        {
            var storage = new StorageRuntime(NewStorageDefinition(1f), new GridCoord(0, 0), Direction.North);

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
            StorageDefinition definition = NewStorageDefinition(0f);
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
            StorageDefinition definition = NewStorageDefinition(0f);
            var so = new SerializedObject(definition);
            so.FindProperty("rejectsConveyorInput").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            var storage = new StorageRuntime(definition, new GridCoord(0, 0), Direction.North);

            Assert.IsFalse(storage.CanAcceptInput("iron_ore", 1, Direction.South), "Removing the cooldown must not also remove the conveyor lock.");
            Assert.IsTrue(storage.CanAcceptFromRobot("iron_ore", 1), "A robot was never subject to it.");
        }
    }
}
