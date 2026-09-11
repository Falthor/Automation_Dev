using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Grid;
using UnityEngine;

namespace Game.Gameplay.WorldGeneration
{
    /// <summary>
    /// World-content placement run once at game start: the Core building at the map center, its
    /// starting-resources Storage Box fixture one cell south of it, and its resource deposits:
    /// one cluster of each resource within its action radius, and two rings of invitation clusters
    /// beyond it that the radius researches open. Not part of Game.Construction - this is world
    /// generation (like TerrainRuntime), not a player action.
    ///
    /// Seeded per run by default (WorldGenerationSettings.RandomizeResourceSeed), so two new games
    /// are two different worlds. Placement was always random in shape and fixed in fact, drawn from
    /// one seed stored in the asset - which is not randomness the player can ever observe. Pinning
    /// the seed stays available for reproducing a layout; ResourceSeed reports the one used.
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

        /// <summary>
        /// Distance band (cells) for the single "invitation" cluster placed just outside the
        /// action radius - visible, deliberately not yet exploitable. Must stay entirely beyond
        /// the starting action radius (22) and entirely within the lowest radius a research grants
        /// (42 today), so the first radius research is the one that opens it.
        /// </summary>
        const float InvitationMinDistanceCells = 26f;
        const float InvitationMaxDistanceCells = 29f;

        /// <summary>
        /// The second ring of invitation clusters: further out than the first, and richer - 8 iron,
        /// 8 copper, 4 coal. Only the centre is drawn in the band (cells, Core center to cluster
        /// center), so a cluster can overhang it by half its diagonal; the second radius research (60)
        /// is what opens most of it. Required, unlike the first ring: a world missing one is refused,
        /// the same way as a world missing a starting cluster.
        /// </summary>
        const float SecondRingMinDistanceCells = 40f;
        const float SecondRingMaxDistanceCells = 60f;
        const int SecondRingIronDeposits = 8;
        const int SecondRingCopperDeposits = 8;
        const int SecondRingCoalDeposits = 4;

        /// <summary>
        /// Clear cells kept between a cluster's outer edge and the action-radius ring, so a cluster
        /// never lands flush against the boundary. The far corner of a cluster is what has to clear
        /// it, not its centre - see InRadiusMaxDistance.
        /// </summary>
        const float RadiusEdgeMarginCells = 2f;

        /// <summary>
        /// Clear cells kept around every cluster, so two of them can never end up touching. Checked
        /// as a ring of empty ground around the candidate footprint rather than as a distance
        /// between centres: that also keeps a cluster off the Core, off its chest, and off anything
        /// else already placed, with one rule instead of one per thing to avoid.
        ///
        /// One cell is enough to read as separate on screen and to leave a belt a way between two
        /// fields, which is the practical reason the gap matters at all.
        /// </summary>
        const int ClusterClearanceCells = 1;

        public CoreRuntime Core { get; private set; }
        public GridCoord CoreOrigin { get; private set; }

        /// <summary>
        /// The middle of the Core's footprint, in cell space - what every distance from the Core is
        /// measured from (deposit placement, and the radius that writes into the discovery state).
        ///
        /// Derived rather than stored, so it is the same value whether the world was generated or
        /// restored from a save: both paths set CoreOrigin and Core, and this reads them.
        /// </summary>
        public Vector2 CoreCenterCells => Core != null
            ? new Vector2(
                CoreOrigin.X + Core.Definition.FootprintSize.x / 2f,
                CoreOrigin.Y + Core.Definition.FootprintSize.y / 2f)
            : Vector2.zero;

        /// <summary>
        /// The seed this world's deposits were actually drawn from - the drawn one when
        /// WorldGenerationSettings.RandomizeResourceSeed is on, the pinned one otherwise. Reported
        /// rather than left implicit so a layout worth looking at again can be pinned back.
        /// Meaningless after RestoreState: a loaded world's deposits come from the save file, not
        /// from a draw.
        /// </summary>
        public int ResourceSeed { get; private set; }

        /// <summary>
        /// A Storage Box fixture placed one cell south of the Core and seeded with
        /// WorldGenerationSettings.StartingStock at world generation - the Core itself never
        /// accepts any delivery (see CoreRuntime), so the player's starting resources live here
        /// instead, as a real, counted Storage box rather than a building-less pool. Only set by
        /// Generate() (a fresh game); null after RestoreState (a loaded game) - the fixture is
        /// then just one more entry in the save's regular building list, restored generically like
        /// any other placed Storage box, so there is nothing extra for this class to redo.
        /// </summary>
        public StorageRuntime CoreStorage { get; private set; }

        /// <summary>Pass-through to Core.ActionRadiusCells (TASK_04_PLAFOND_RAYON.md §4) - Core is the sole owner of the current radius, this is just a convenience for callers that only hold a WorldGenerator reference.</summary>
        public int ActionRadiusCells => Core?.ActionRadiusCells ?? 0;

        public IReadOnlyList<DepositRuntime> OreDeposits => _oreDeposits;

        readonly List<DepositRuntime> _oreDeposits = new List<DepositRuntime>();

        /// <summary>
        /// A deposit that has just come into existence after generation - what an explorer robot
        /// opening a sector produces. Presentation subscribes to spawn its view.
        ///
        /// It exists because the list and the grid have to stay one thing. A deposit written straight
        /// into <see cref="GridRuntime"/> is real to everything that asks the grid - the hover glow
        /// finds it, an Extractor could be placed on it - and invisible to everything that reads this
        /// list, which is the view and the save. That was exactly the defect: ore appeared, could be
        /// pointed at, was never drawn, and did not survive a reload.
        /// </summary>
        public event Action<DepositRuntime> DepositAppeared;

        /// <summary>
        /// Places a deposit and registers it, in that order and in one call.
        ///
        /// <b>The only way to add a deposit after generation.</b> Callers must not reach for
        /// <see cref="GridRuntime.PlaceDeposit"/> themselves: it returns the runtime it created, and
        /// dropping that return value is what makes a deposit exist without being owned by anything.
        /// </summary>
        public DepositRuntime AddDeposit(GridRuntime grid, GridCoord origin, OreDepositDefinition definition)
        {
            if (grid == null || definition == null) return null;

            DepositRuntime deposit = grid.PlaceDeposit(origin, definition);
            _oreDeposits.Add(deposit);
            DepositAppeared?.Invoke(deposit);

            return deposit;
        }

        public void Generate(GridRuntime grid, int mapSizeCells, WorldGenerationSettings settings, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem)
        {
            CoreDefinition coreDefinition = settings.CoreDefinition;

            CoreOrigin = new GridCoord(
                mapSizeCells / 2 - coreDefinition.FootprintSize.x / 2,
                mapSizeCells / 2 - coreDefinition.FootprintSize.y / 2);

            Core = new CoreRuntime(coreDefinition, CoreOrigin, Direction.North, computeSystem, powerSystem, researchSystem);
            grid.SetOccupantFootprint(CoreOrigin, coreDefinition.FootprintSize, Core);

            if (settings.CoreStorageDefinition != null)
            {
                // Centred on the Core's width and flush against its south edge, whatever either
                // footprint is. The old form assumed a 1x1 chest and put its single cell at half
                // the Core's width, which stops being the middle the moment the reserve is wider
                // than one cell.
                Vector2Int storageFootprint = settings.CoreStorageDefinition.FootprintSize;
                var storageCell = new GridCoord(
                    CoreOrigin.X + (coreDefinition.FootprintSize.x - storageFootprint.x) / 2,
                    CoreOrigin.Y - storageFootprint.y);
                CoreStorage = new StorageRuntime(settings.CoreStorageDefinition, storageCell, Direction.North);
                grid.SetOccupantFootprint(storageCell, settings.CoreStorageDefinition.FootprintSize, CoreStorage);

                foreach (RecipeIngredient ingredient in settings.StartingStock)
                {
                    if (ingredient.Item != null) CoreStorage.SeedInitialContents(ingredient.Item.Id, ingredient.Amount);
                }
            }

            // Guid rather than Unity's Random or a tick count: it depends on no global state
            // anything else in the project could have seeded, and two new games started in the same
            // millisecond still differ.
            ResourceSeed = settings.RandomizeResourceSeed ? System.Guid.NewGuid().GetHashCode() : settings.ResourceSeed;

            var random = new System.Random(ResourceSeed);
            Vector2 coreCenter = CoreCenterCells;

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

            // The second ring, placed last: every draw above happens exactly as it did before it
            // existed, so a pinned seed keeps its first two rings where they were.
            PlaceRequiredCluster(grid, random, coreCenter, settings.IronOreDefinition, "fer",
                SecondRingMinDistanceCells, SecondRingMaxDistanceCells, SecondRingIronDeposits);
            PlaceRequiredCluster(grid, random, coreCenter, settings.CopperOreDefinition, "cuivre",
                SecondRingMinDistanceCells, SecondRingMaxDistanceCells, SecondRingCopperDeposits);
            PlaceRequiredCluster(grid, random, coreCenter, settings.CoalOreDefinition, "charbon",
                SecondRingMinDistanceCells, SecondRingMaxDistanceCells, SecondRingCoalDeposits);
        }

        float InRadiusMaxDistance(OreDepositDefinition definition)
        {
            if (definition == null) return 0f;
            // The cluster's own far corner is what must stay inside the ring, so what comes off the
            // radius is half its diagonal - not its width, which was only ever an approximation that
            // happened to leave about a cell of slack.
            Vector2Int clusterFootprint = definition.FootprintSize * 2;
            float halfDiagonal = new Vector2(clusterFootprint.x, clusterFootprint.y).magnitude * 0.5f;
            return ActionRadiusCells - RadiusEdgeMarginCells - halfDiagonal;
        }

        /// <summary>Places the one required in-radius cluster for a resource type - see PlaceRequiredCluster.</summary>
        void PlaceGuaranteedCluster(GridRuntime grid, System.Random random, Vector2 coreCenter, OreDepositDefinition definition, string resourceLabel)
            => PlaceRequiredCluster(grid, random, coreCenter, definition, resourceLabel, InRadiusMinDistanceCells, InRadiusMaxDistance(definition), 4);

        /// <summary>Places a cluster the world cannot do without, or throws if it cannot be placed within the attempt budget - see the Generate() comment on why this is fatal instead of silent.</summary>
        void PlaceRequiredCluster(GridRuntime grid, System.Random random, Vector2 coreCenter, OreDepositDefinition definition, string resourceLabel,
            float minDistance, float maxDistance, int deposits)
        {
            if (definition == null)
            {
                throw new System.InvalidOperationException(
                    $"World generation cannot place the required {resourceLabel} cluster: no OreDepositDefinition assigned in WorldGenerationSettings.");
            }

            if (!TryPlaceCluster(grid, random, coreCenter, definition, minDistance, maxDistance, deposits))
            {
                throw new System.InvalidOperationException(
                    $"World generation failed to place the required {resourceLabel} cluster ({deposits} deposits, {minDistance:0}-{maxDistance:0} cells from the Core) " +
                    $"within {DepositPlacementAttempts} attempts. A world missing it is refused rather than handed over amputated.");
            }
        }

        bool TryPlaceCluster(GridRuntime grid, System.Random random, Vector2 coreCenter, OreDepositDefinition definition, float minDistance, float maxDistance, int deposits = 4)
        {
            if (definition == null) return false;

            // A deposit never spawns as a single isolated footprint - it's a cluster of
            // individually-exploitable deposit instances (each still its own definition-sized
            // footprint), so several extractors can work the same field side by side, and it reads
            // as a much bigger, more visible ore field on the map. Two rows, as many columns as that
            // takes: 4 deposits make the 2x2 square, 8 a 4x2 strip.
            int columns = Mathf.Max(1, (deposits + 1) / 2);
            int rows = deposits > 1 ? 2 : 1;

            // A strip is laid along x or y at random, so a long field does not always point the same
            // way. Drawn only for a strip: a square has one orientation, and not drawing for it keeps
            // every earlier cluster's placement exactly what the same seed gave before.
            if (columns != rows && random.NextDouble() < 0.5) (columns, rows) = (rows, columns);

            var layout = new Vector2Int(columns, rows);
            var clusterFootprint = new Vector2Int(definition.FootprintSize.x * columns, definition.FootprintSize.y * rows);

            if (!TryFindFreeSpot(grid, random, coreCenter, minDistance, maxDistance, clusterFootprint, out GridCoord clusterOrigin))
            {
                return false;
            }

            PlaceDepositCluster(grid, clusterOrigin, definition, layout, deposits);
            return true;
        }

        /// <summary>Places up to <paramref name="deposits"/> individual deposit instances on a <paramref name="layout"/> grid filling clusterOrigin's footprint.</summary>
        void PlaceDepositCluster(GridRuntime grid, GridCoord clusterOrigin, OreDepositDefinition definition, Vector2Int layout, int deposits)
        {
            Vector2Int size = definition.FootprintSize;
            int placed = 0;

            for (int qx = 0; qx < layout.x; qx++)
            {
                for (int qy = 0; qy < layout.y && placed < deposits; qy++)
                {
                    var subOrigin = new GridCoord(clusterOrigin.X + qx * size.x, clusterOrigin.Y + qy * size.y);
                    _oreDeposits.Add(grid.PlaceDeposit(subOrigin, definition));
                    placed++;
                }
            }
        }

        /// <summary>
        /// Rebuilds this generator's state from a previously-saved snapshot instead of running
        /// procedural generation (CONTRACTS.md §14). Used only by the save/load restore path -
        /// the caller has already reconstructed Core (with its own ActionRadiusCells already
        /// restored via CoreRuntime.RestoreState - TASK_04_PLAFOND_RAYON.md §4) and every
        /// DepositRuntime, and placed them into Game.Grid at their saved cells. No longer takes
        /// its own actionRadiusCells parameter - ActionRadiusCells here is a pass-through of
        /// core.ActionRadiusCells, so passing a second, separate value could only ever disagree
        /// with it.
        /// </summary>
        public void RestoreState(CoreRuntime core, GridCoord coreOrigin, IEnumerable<DepositRuntime> deposits)
        {
            Core = core;
            CoreOrigin = coreOrigin;
            _oreDeposits.Clear();
            _oreDeposits.AddRange(deposits);
        }

        /// <summary>
        /// A uniformly random spot in the ring between minDistance and maxDistance, with a clear
        /// ring of ClusterClearanceCells around it so nothing it lands next to is touching it.
        ///
        /// The angle is uniform and the radius is drawn from a square root rather than uniformly:
        /// a ring's area grows with its radius, so drawing the distance flat crowds every cluster
        /// toward the inner edge of the band. This makes any point of the band equally likely,
        /// which is what "aleatoire" has to mean for a layout the player reads as scattered.
        /// </summary>
        bool TryFindFreeSpot(GridRuntime grid, System.Random random, Vector2 coreCenter, float minDistance, float maxDistance, Vector2Int depositFootprint, out GridCoord origin)
        {
            for (int attempt = 0; attempt < DepositPlacementAttempts && maxDistance > minDistance; attempt++)
            {
                float angle = (float)(random.NextDouble() * Mathf.PI * 2.0);

                float inner = minDistance * minDistance;
                float outer = maxDistance * maxDistance;
                float distance = Mathf.Sqrt(inner + (float)random.NextDouble() * (outer - inner));

                int centerX = Mathf.RoundToInt(coreCenter.x + Mathf.Cos(angle) * distance);
                int centerY = Mathf.RoundToInt(coreCenter.y + Mathf.Sin(angle) * distance);
                var candidateOrigin = new GridCoord(centerX - depositFootprint.x / 2, centerY - depositFootprint.y / 2);

                if (IsAreaFreeWithClearance(grid, candidateOrigin, depositFootprint, ClusterClearanceCells))
                {
                    origin = candidateOrigin;
                    return true;
                }
            }

            origin = default;
            return false;
        }

        /// <summary>
        /// Whether the footprint AND a ring of `clearance` cells around it are all free. Asking the
        /// grid about the padded rectangle is what stops two clusters ending up edge to edge: the
        /// second one's own cells may be free while its neighbour starts in the very next column.
        /// </summary>
        static bool IsAreaFreeWithClearance(GridRuntime grid, GridCoord origin, Vector2Int footprint, int clearance)
        {
            var paddedOrigin = new GridCoord(origin.X - clearance, origin.Y - clearance);
            var paddedSize = new Vector2Int(footprint.x + clearance * 2, footprint.y + clearance * 2);
            return grid.IsAreaFree(paddedOrigin, paddedSize);
        }
    }
}
