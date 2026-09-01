using System;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay
{
    public class ConveyorOrientationTests
    {
        static ConveyorRuntime NewConveyor()
        {
            var definition = ScriptableObject.CreateInstance<ConveyorDefinition>();
            return new ConveyorRuntime(definition, new GridCoord(0, 0), Direction.North);
        }

        // The 8 valid perpendicular (entry, exit) pairs, derived from the canonical
        // reference (South-in, East-out = North/unmirrored) rotated through all 4 steps.
        [TestCase(Direction.South, Direction.East, Direction.North, false)]
        [TestCase(Direction.South, Direction.West, Direction.North, true)]
        [TestCase(Direction.West, Direction.South, Direction.East, false)]
        [TestCase(Direction.West, Direction.North, Direction.East, true)]
        [TestCase(Direction.North, Direction.West, Direction.South, false)]
        [TestCase(Direction.North, Direction.East, Direction.South, true)]
        [TestCase(Direction.East, Direction.North, Direction.West, false)]
        [TestCase(Direction.East, Direction.South, Direction.West, true)]
        public void ConfigureAsCorner_ValidPerpendicularPairs_DeriveExpectedOrientation(
            Direction entry, Direction exit, Direction expectedRotation, bool expectedMirrored)
        {
            var conveyor = NewConveyor();

            conveyor.ConfigureAsCorner(entry, exit);

            Assert.AreEqual(ConveyorShapeKind.Corner, conveyor.Orientation.Shape);
            Assert.AreEqual(expectedRotation, conveyor.Orientation.Rotation);
            Assert.AreEqual(expectedMirrored, conveyor.Orientation.Mirrored);
        }

        [TestCase(Direction.North, Direction.North)]
        [TestCase(Direction.East, Direction.East)]
        [TestCase(Direction.South, Direction.South)]
        [TestCase(Direction.West, Direction.West)]
        [TestCase(Direction.North, Direction.South)]
        [TestCase(Direction.East, Direction.West)]
        [TestCase(Direction.South, Direction.North)]
        [TestCase(Direction.West, Direction.East)]
        public void ConfigureAsCorner_EqualOrOppositePairs_Throws(Direction entry, Direction exit)
        {
            var conveyor = NewConveyor();

            Assert.Throws<ArgumentException>(() => conveyor.ConfigureAsCorner(entry, exit));
        }

        [Test]
        public void ConfigureAsCorner_IsDeterministic()
        {
            var a = NewConveyor();
            var b = NewConveyor();

            a.ConfigureAsCorner(Direction.West, Direction.North);
            b.ConfigureAsCorner(Direction.West, Direction.North);

            Assert.AreEqual(a.Orientation.Rotation, b.Orientation.Rotation);
            Assert.AreEqual(a.Orientation.Mirrored, b.Orientation.Mirrored);
        }

        [TestCase(Direction.North)]
        [TestCase(Direction.East)]
        [TestCase(Direction.South)]
        [TestCase(Direction.West)]
        public void ConfigureAsStraight_SetsRotationToExitDirection(Direction exit)
        {
            var conveyor = NewConveyor();

            conveyor.ConfigureAsStraight(exit);

            Assert.AreEqual(ConveyorShapeKind.Straight, conveyor.Orientation.Shape);
            Assert.AreEqual(exit, conveyor.Orientation.Rotation);
            Assert.IsFalse(conveyor.Orientation.Mirrored);
        }
    }
}
