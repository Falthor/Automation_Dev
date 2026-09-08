using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Missions;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Where the explorer robots stand while they are not out on a mission.
    ///
    /// <b>At the Core's hatch.</b> Everything the Core sends out or takes in passes through the
    /// reserve beneath it, so a machine setting off from anywhere else would be setting off from
    /// nowhere in particular. It also puts them where the player already looks.
    ///
    /// A park, not a simulation: these are markers for robots that exist as charges in
    /// <see cref="MissionSystem"/>, which is where the fleet's real state lives. Nothing here is read
    /// back by gameplay. When missions can be launched, one leaving is a marker being hidden.
    ///
    /// Spawned once, when the fleet appears, and never per-frame: the check is one bool comparison.
    /// </summary>
    public sealed class ExplorerRobotParkView
    {
        /// <summary>Cells around the host a robot may stand on, tried in order. South first: the Data Center's own art reads top-down, and a robot below it sits in front rather than behind.</summary>
        static readonly Direction[] ParkingOrder = { Direction.South, Direction.East, Direction.West, Direction.North };

        readonly GridRuntime _grid;
        readonly MissionSettings _settings;
        readonly DepthSortLadder _depthSort;

        readonly List<GameObject> _markers = new List<GameObject>();

        /// <summary>The cells the markers stand on. Kept beside them because the grid does not hold them - see <see cref="StandsOn"/>.</summary>
        readonly List<GridCoord> _cells = new List<GridCoord>();

        bool _spawned;

        public ExplorerRobotParkView(GridRuntime grid, MissionSettings settings, DepthSortLadder depthSort)
        {
            _grid = grid;
            _settings = settings;
            _depthSort = depthSort;
        }

        /// <summary>How many markers currently stand in the park. Watched by a test: the fleet must appear once, not once per frame.</summary>
        public int MarkerCount => _markers.Count;

        /// <summary>
        /// Puts the fleet on the ground the first time the robots exist. Safe to call every frame;
        /// does nothing at all until then, and nothing again afterwards.
        /// </summary>
        public void Refresh(MissionSystem missions, BuildingRuntime host)
        {
            if (_spawned || missions == null || !missions.RobotsHaveAppeared) return;
            if (_settings == null || _settings.ExplorerRobotSprite == null || host == null) return;

            _spawned = true;

            for (int i = 0; i < missions.ExplorerRobotCount; i++)
            {
                if (!TryParkingCell(host, i, out GridCoord cell)) continue;

                _cells.Add(cell);
                _markers.Add(SpawnMarker(cell, i));
            }
        }

        /// <summary>
        /// Whether a parked robot stands on this cell - what makes the fleet clickable.
        ///
        /// A cell test, not a collider: the markers sit on grid cells, and every other click in this
        /// project is answered by asking the grid what is at a coordinate. They are not grid
        /// occupants though - they are views, and occupying a cell would stop a building being built
        /// there - so the park keeps its own short list rather than the grid holding them.
        /// </summary>
        public bool StandsOn(GridCoord cell) => _cells.Contains(cell);

        /// <summary>
        /// One free cell against the host's edge, spread along it so two robots never stack. Returns
        /// false when the host is walled in, which loses that marker rather than piling it on another.
        /// </summary>
        bool TryParkingCell(BuildingRuntime host, int index, out GridCoord cell)
        {
            Vector2Int footprint = host.Definition.FootprintSize;

            foreach (Direction side in ParkingOrder)
            {
                bool horizontalEdge = side == Direction.North || side == Direction.South;
                int alongLength = Mathf.Max(1, horizontalEdge ? footprint.x : footprint.y);

                for (int step = 0; step < alongLength; step++)
                {
                    // Start each robot at a different place along the edge, so the second does not
                    // queue behind the first before trying the next slot.
                    int slot = (index + step) % alongLength;

                    cell = horizontalEdge
                        ? new GridCoord(host.Cell.X + slot, host.Cell.Y + (side == Direction.North ? footprint.y : -1))
                        : new GridCoord(host.Cell.X + (side == Direction.East ? footprint.x : -1), host.Cell.Y + slot);

                    if (_grid.GetOccupant(cell) == null && !_cells.Contains(cell)) return true;
                }
            }

            cell = default;
            return false;
        }

        GameObject SpawnMarker(GridCoord cell, int index)
        {
            var go = new GameObject($"ExplorerRobot {index}");
            go.transform.position = _grid.CellCenterToWorld(cell);

            var renderer = go.AddComponent<SpriteRenderer>();
            BuildingSpawner.FitSpriteUniform(renderer, _settings.ExplorerRobotSprite, Vector2.one * _grid.CellSize);
            _depthSort?.Register(renderer, _grid.CellCenterToWorld(cell).y, SortingBands.SubSprite);

            return go;
        }
    }
}
