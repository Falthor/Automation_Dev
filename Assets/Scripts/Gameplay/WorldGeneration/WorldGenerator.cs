using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
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

        /// <summary>
        /// Distance (cells, Core center to cluster center) for every in-radius cluster - the
        /// guaranteed one per resource and the surplus ones alike. Replaces the old
        /// footprint-derived minimum (~6 cells, which put a cluster almost against the Core)
        /// with a flat value that leaves room to build around the Core (ALIGNEMENT_PROJET.md §8).
        /// </summary>
        const float InRadiusMinDistanceCells = 10f;

        /// <summary>Distance band (cells) for the single "invitation" cluster placed just outside the action radius - visible, deliberately not yet exploitable.</summary>
        const float InvitationMinDistanceCells = 28f;
        const float InvitationMaxDistanceCells = 34f;

        public CoreRuntime Core { get; private set; }
        public GridCoord CoreOrigin { get; private set; }
        public int ActionRadiusCells { get; private set; }
        public IReadOnlyList<DepositRuntime> OreDeposits => _oreDeposits;

        readonly List<DepositRuntime> _oreDeposits = new List<DepositRuntime>();

        public void Generate(GridRuntime grid, int mapSizeCells, WorldGenerationSettings settings, ComputeSystem computeSystem, PowerSystem powerSystem)
        {
            CoreDefinition coreDefinition = settings.CoreDefinition;
            ActionRadiusCells = coreDefinition.ActionRadiusCells;

            CoreOrigin = new GridCoord(
                mapSizeCells / 2 - coreDefinition.FootprintSize.x / 2,
                mapSizeCells / 2 - coreDefinition.FootprintSize.y / 2);

            Core = new CoreRuntime(coreDefinition, CoreOrigin, Direction.North, computeSystem, powerSystem);
            grid.SetOccupantFootprint(CoreOrigin, coreDefinition.FootprintSize, Core);

            var random = new System.Random(settings.ResourceSeed);
            Vector2 coreCenter = new Vector2(
                CoreOrigin.X + coreDefinition.FootprintSize.x / 2f,
                CoreOrigin.Y + coreDefinition.FootprintSize.y / 2f);

            // One guaranteed cluster per resource (one iron, one copper, one coal), placed first
            // and inside the radius - 4 deposit slots each, exactly covering the 4/4/2 extractors
            // the introduction needs (coal uses only 2 of its 4). The introduction is not
            // playable without at least one of each resource, so a failure to place any of them
            // throws rather than silently producing an amputated world (ALIGNEMENT_PROJET.md §8 -
            // today's 500-attempts-then-silent-skip is the exact bug this guards against).
            PlaceGuaranteedCluster(grid, random, coreCenter, settings.IronOreDefinition, "fer");
            PlaceGuaranteedCluster(grid, random, coreCenter, settings.CopperOreDefinition, "cuivre");
            PlaceGuaranteedCluster(grid, random, coreCenter, settings.CoalOreDefinition, "charbon");

            // One "invitation" cluster per resource, placed just outside the action radius:
            // visible (once fog of war reveals that far) but not yet exploitable - a standing
            // invitation to expand the radius later. Best-effort, not part of the guarantee above.
            TryPlaceCluster(grid, random, coreCenter, settings.IronOreDefinition, InvitationMinDistanceCells, InvitationMaxDistanceCells);
            TryPlaceCluster(grid, random, coreCenter, settings.CopperOreDefinition, InvitationMinDistanceCells, InvitationMaxDistanceCells);
            TryPlaceCluster(grid, random, coreCenter, settings.CoalOreDefinition, InvitationMinDistanceCells, InvitationMaxDistanceCells);
        }

        float InRadiusMaxDistance(OreDepositDefinition definition)
        {
            if (definition == null) return 0f;
            Vector2Int clusterFootprint = definition.FootprintSize * 2;
            return ActionRadiusCells - Mathf.Max(clusterFootprint.x, clusterFootprint.y);
        }

        /// <summary>Places the one required cluster for a resource type, or throws if it cannot be placed within the attempt budget - see the Generate() comment on why this is fatal instead of silent.</summary>
        void PlaceGuaranteedCluster(GridRuntime grid, System.Random random, Vector2 coreCenter, OreDepositDefinition definition, string resourceLabel)
        {
            if (definition == null)
            {
                throw new System.InvalidOperationException(
                    $"World generation cannot place the guaranteed {resourceLabel} cluster: no OreDepositDefinition assigned in WorldGenerationSettings.");
            }

            if (!TryPlaceCluster(grid, random, coreCenter, definition, InRadiusMinDistanceCells, InRadiusMaxDistance(definition)))
            {
                throw new System.InvalidOperationException(
                    $"World generation failed to place the guaranteed {resourceLabel} resource cluster within {DepositPlacementAttempts} attempts. " +
                    "An introduction missing a resource type is not playable (ALIGNEMENT_PROJET.md §8).");
            }
        }

        bool TryPlaceCluster(GridRuntime grid, System.Random random, Vector2 coreCenter, OreDepositDefinition definition, float minDistance, float maxDistance)
        {
            if (definition == null) return false;

            // A deposit never spawns as a single isolated footprint - it's a 2x2 cluster of
            // individually-exploitable deposit instances (each still its own definition-sized
            // footprint), so up to 4 extractors can work the same deposit side by side, and it
            // reads as a much bigger, more visible ore field on the map.
            Vector2Int clusterFootprint = definition.FootprintSize * 2;

            if (!TryFindFreeSpot(grid, random, coreCenter, minDistance, maxDistance, clusterFootprint, out GridCoord clusterOrigin))
            {
                return false;
            }

            PlaceDepositCluster(grid, clusterOrigin, definition);
            return true;
        }

        /// <summary>Places a 2x2 grid of individual deposit instances filling clusterOrigin's 2x-sized footprint.</summary>
        void PlaceDepositCluster(GridRuntime grid, GridCoord clusterOrigin, OreDepositDefinition definition)
        {
            Vector2Int size = definition.FootprintSize;

            for (int qx = 0; qx < 2; qx++)
            {
                for (int qy = 0; qy < 2; qy++)
                {
                    var subOrigin = new GridCoord(clusterOrigin.X + qx * size.x, clusterOrigin.Y + qy * size.y);
                    _oreDeposits.Add(grid.PlaceDeposit(subOrigin, definition));
                }
            }
        }

        /// <summary>
        /// Rebuilds this generator's state from a previously-saved snapshot instead of running
        /// procedural generation (CONTRACTS.md §14). Used only by the save/load restore path -
        /// the caller has already reconstructed Core and every DepositRuntime and placed them
        /// into Game.Grid at their saved cells.
        /// </summary>
        public void RestoreState(CoreRuntime core, GridCoord coreOrigin, int actionRadiusCells, IEnumerable<DepositRuntime> deposits)
        {
            Core = core;
            CoreOrigin = coreOrigin;
            ActionRadiusCells = actionRadiusCells;
            _oreDeposits.Clear();
            _oreDeposits.AddRange(deposits);
        }

        bool TryFindFreeSpot(GridRuntime grid, System.Random random, Vector2 coreCenter, float minDistance, float maxDistance, Vector2Int depositFootprint, out GridCoord origin)
        {
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
