using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.WorldGeneration
{
    /// <summary>
    /// Deterministic world-content placement run once at game start: the Core building at the
    /// map center, and its resource deposits scattered within its action radius. Not part of
    /// Game.Construction - this is world generation (like TerrainRuntime), not a player action.
    /// </summary>
    public sealed class WorldGenerator
    {
        const int DepositPlacementAttempts = 500;

        public BuildingRuntime Core { get; private set; }
        public GridCoord CoreOrigin { get; private set; }
        public int ActionRadiusCells { get; private set; }
        public IReadOnlyList<DepositRuntime> OreDeposits => _oreDeposits;

        readonly List<DepositRuntime> _oreDeposits = new List<DepositRuntime>();

        public void Generate(GridRuntime grid, int mapSizeCells, WorldGenerationSettings settings)
        {
            CoreDefinition coreDefinition = settings.CoreDefinition;
            ActionRadiusCells = coreDefinition.ActionRadiusCells;

            CoreOrigin = new GridCoord(
                mapSizeCells / 2 - coreDefinition.FootprintSize.x / 2,
                mapSizeCells / 2 - coreDefinition.FootprintSize.y / 2);

            Core = new BuildingRuntime(coreDefinition, CoreOrigin, Direction.North);
            grid.SetOccupantFootprint(CoreOrigin, coreDefinition.FootprintSize, Core);

            var random = new System.Random(settings.ResourceSeed);
            Vector2 coreCenter = new Vector2(
                CoreOrigin.X + coreDefinition.FootprintSize.x / 2f,
                CoreOrigin.Y + coreDefinition.FootprintSize.y / 2f);

            OreDepositDefinition[] toPlace =
            {
                settings.IronOreDefinition, settings.IronOreDefinition,
                settings.CopperOreDefinition, settings.CopperOreDefinition,
                settings.CoalOreDefinition, settings.CoalOreDefinition
            };

            foreach (OreDepositDefinition definition in toPlace)
            {
                if (definition == null) continue;

                if (TryFindFreeSpot(grid, random, coreCenter, coreDefinition.FootprintSize, definition.FootprintSize, out GridCoord origin))
                {
                    _oreDeposits.Add(grid.PlaceDeposit(origin, definition));
                }
            }
        }

        bool TryFindFreeSpot(GridRuntime grid, System.Random random, Vector2 coreCenter, Vector2Int coreFootprint, Vector2Int depositFootprint, out GridCoord origin)
        {
            float minDistance = Mathf.Max(coreFootprint.x, coreFootprint.y) * 0.5f + Mathf.Max(depositFootprint.x, depositFootprint.y);
            float maxDistance = ActionRadiusCells - Mathf.Max(depositFootprint.x, depositFootprint.y);

            for (int attempt = 0; attempt < DepositPlacementAttempts && maxDistance > minDistance; attempt++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);
                float distance = minDistance + (float)random.NextDouble() * (maxDistance - minDistance);

                int centerX = Mathf.RoundToInt(coreCenter.x + Mathf.Cos(angle) * distance);
                int centerY = Mathf.RoundToInt(coreCenter.y + Mathf.Sin(angle) * distance);
                var candidateOrigin = new GridCoord(centerX - depositFootprint.x / 2, centerY - depositFootprint.y / 2);

                if (grid.IsAreaFree(candidateOrigin, depositFootprint))
                {
                    origin = candidateOrigin;
                    return true;
                }
            }

            origin = default;
            return false;
        }
    }
}
