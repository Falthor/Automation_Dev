using Game.Core;
using Game.Grid;
using NUnit.Framework;

namespace Game.Tests.EditMode.Grid
{
    public class TerrainRuntimeTests
    {
        [Test]
        public void SameSeedAndParameters_ProduceIdenticalTerrain()
        {
            var a = new TerrainRuntime(size: 20, seed: 42, terrainScale: 10f, proportion: 0.3f);
            var b = new TerrainRuntime(size: 20, seed: 42, terrainScale: 10f, proportion: 0.3f);

            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(a.GetTerrainType(cell), b.GetTerrainType(cell));
                }
            }
        }

        [Test]
        public void DifferentSeed_CanProduceDifferentTerrain()
        {
            var a = new TerrainRuntime(size: 20, seed: 1, terrainScale: 10f, proportion: 0.3f);
            var b = new TerrainRuntime(size: 20, seed: 2, terrainScale: 10f, proportion: 0.3f);

            bool anyDifference = false;
            for (int y = 0; y < 20 && !anyDifference; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    var cell = new GridCoord(x, y);
                    if (a.GetTerrainType(cell) != b.GetTerrainType(cell))
                    {
                        anyDifference = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(anyDifference);
        }

        [Test]
        public void OutOfBoundsCell_ReturnsBase()
        {
            var terrain = new TerrainRuntime(size: 10, seed: 0, terrainScale: 10f, proportion: 0.3f);

            Assert.AreEqual(TerrainType.Base, terrain.GetTerrainType(new GridCoord(-1, 0)));
            Assert.AreEqual(TerrainType.Base, terrain.GetTerrainType(new GridCoord(10, 10)));
        }

        [Test]
        public void SampleContinuous_IsDeterministicPerCoordinate()
        {
            var terrain = new TerrainRuntime(size: 10, seed: 7, terrainScale: 10f, proportion: 0.3f);

            float a = terrain.SampleContinuous(3.5f, 2.25f);
            float b = terrain.SampleContinuous(3.5f, 2.25f);

            Assert.AreEqual(a, b);
        }
    }
}
