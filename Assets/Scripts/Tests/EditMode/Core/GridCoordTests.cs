using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    public class GridCoordTests
    {
        [Test]
        public void Equality_ComparesByValue()
        {
            Assert.AreEqual(new GridCoord(2, 3), new GridCoord(2, 3));
            Assert.IsTrue(new GridCoord(2, 3) == new GridCoord(2, 3));
            Assert.IsTrue(new GridCoord(2, 3) != new GridCoord(2, 4));
        }

        [Test]
        public void Addition_WithDirection_OffsetsByOneCell()
        {
            var origin = new GridCoord(5, 5);
            Assert.AreEqual(new GridCoord(5, 6), origin + Direction.North);
            Assert.AreEqual(new GridCoord(6, 5), origin + Direction.East);
            Assert.AreEqual(new GridCoord(5, 4), origin + Direction.South);
            Assert.AreEqual(new GridCoord(4, 5), origin + Direction.West);
        }
    }
}
