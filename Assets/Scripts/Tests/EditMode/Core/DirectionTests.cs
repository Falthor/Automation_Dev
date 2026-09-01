using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    public class DirectionTests
    {
        [TestCase(Direction.North, Direction.South)]
        [TestCase(Direction.East, Direction.West)]
        [TestCase(Direction.South, Direction.North)]
        [TestCase(Direction.West, Direction.East)]
        public void Opposite_ReturnsExpected(Direction direction, Direction expected)
        {
            Assert.AreEqual(expected, direction.Opposite());
        }

        [TestCase(Direction.North, 1, Direction.East)]
        [TestCase(Direction.North, 2, Direction.South)]
        [TestCase(Direction.North, 4, Direction.North)]
        [TestCase(Direction.North, -1, Direction.West)]
        [TestCase(Direction.East, 3, Direction.North)]
        public void RotateCW_ReturnsExpected(Direction direction, int steps, Direction expected)
        {
            Assert.AreEqual(expected, direction.RotateCW(steps));
        }

        [Test]
        public void ToOffset_MatchesCardinalConvention()
        {
            Assert.AreEqual(new GridCoord(0, 1), Direction.North.ToOffset());
            Assert.AreEqual(new GridCoord(1, 0), Direction.East.ToOffset());
            Assert.AreEqual(new GridCoord(0, -1), Direction.South.ToOffset());
            Assert.AreEqual(new GridCoord(-1, 0), Direction.West.ToOffset());
        }

        [TestCase(Direction.North, 0)]
        [TestCase(Direction.East, 90)]
        [TestCase(Direction.South, 180)]
        [TestCase(Direction.West, 270)]
        public void RotationDegrees_RoundTrips(Direction direction, int degrees)
        {
            Assert.AreEqual(degrees, direction.ToRotationDegrees());
            Assert.AreEqual(direction, DirectionExtensions.FromRotationDegrees(degrees));
        }
    }
}
