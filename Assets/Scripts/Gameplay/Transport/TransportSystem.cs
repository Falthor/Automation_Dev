using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;

namespace Game.Gameplay.Transport
{
    /// <summary>
    /// Central tick driving item production and belt movement. Pull-based: a receiver (a
    /// conveyor with a free slot, or a storage) looks at the specific neighbor whose configured
    /// output (GetOutputCell(), footprint-aware) points at the receiver's own cell, and pulls
    /// from it via the existing Flow contract (PeekPullableItem/ConsumePulledItem) - no new
    /// transport contract, no belt-lane simulation, no per-frame world search (direct grid
    /// lookups only).
    /// </summary>
    public sealed class TransportSystem
    {
        static readonly Direction[] AllDirections = { Direction.North, Direction.East, Direction.South, Direction.West };

        const float ConveyorSpeedCellsPerSecond = 1.5f;

        readonly GridRuntime _grid;
        readonly List<ExtractorRuntime> _extractors = new List<ExtractorRuntime>();
        readonly List<ConveyorRuntime> _conveyors = new List<ConveyorRuntime>();
        readonly List<StorageRuntime> _storages = new List<StorageRuntime>();

        public TransportSystem(GridRuntime grid)
        {
            _grid = grid;
        }

        public void Register(BuildingRuntime building)
        {
            switch (building)
            {
                case ExtractorRuntime extractor: _extractors.Add(extractor); break;
                case ConveyorRuntime conveyor: _conveyors.Add(conveyor); break;
                case StorageRuntime storage: _storages.Add(storage); break;
            }
        }

        public void Unregister(BuildingRuntime building)
        {
            switch (building)
            {
                case ExtractorRuntime extractor: _extractors.Remove(extractor); break;
                case ConveyorRuntime conveyor: _conveyors.Remove(conveyor); break;
                case StorageRuntime storage: _storages.Remove(storage); break;
            }
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _extractors.Count; i++)
            {
                _extractors[i].Tick(deltaTime);
            }

            for (int i = 0; i < _conveyors.Count; i++)
            {
                ConveyorRuntime conveyor = _conveyors[i];
                if (!conveyor.HasItem)
                {
                    GridCoord behind = conveyor.Cell + conveyor.Orientation.Rotation.Opposite();
                    if (TryPullFromNeighbor(behind, conveyor.Cell, out object item, out BuildingRuntime source))
                    {
                        conveyor.ReceiveItem(item);
                        source.ConsumePulledItem(item);
                    }
                }

                conveyor.AdvanceItem(deltaTime, ConveyorSpeedCellsPerSecond);
            }

            for (int i = 0; i < _storages.Count; i++)
            {
                StorageRuntime storage = _storages[i];
                for (int d = 0; d < AllDirections.Length; d++)
                {
                    Direction dir = AllDirections[d];
                    GridCoord neighborCell = storage.Cell + dir;
                    if (!TryPullFromNeighbor(neighborCell, storage.Cell, out object item, out BuildingRuntime source)) continue;
                    if (!(item is OreType oreType) || !storage.CanAcceptInput(oreType, 1, dir.Opposite())) continue;

                    storage.AddInput(oreType, 1, dir.Opposite());
                    source.ConsumePulledItem(item);
                }
            }
        }

        /// <summary>
        /// A candidate at <paramref name="neighborCell"/> may only be pulled from if its own
        /// configured output actually targets <paramref name="destinationCell"/> - otherwise a
        /// conveyor/extractor pointed elsewhere would be incorrectly drained by an unrelated
        /// neighbor.
        /// </summary>
        bool TryPullFromNeighbor(GridCoord neighborCell, GridCoord destinationCell, out object item, out BuildingRuntime source)
        {
            item = null;
            source = null;

            if (!(_grid.GetOccupant(neighborCell) is BuildingRuntime candidate)) return false;
            if (candidate.GetOutputCell() != destinationCell) return false;

            object pulled = candidate.PeekPullableItem();
            if (pulled == null) return false;

            item = pulled;
            source = candidate;
            return true;
        }
    }
}
