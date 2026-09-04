using System.Collections.Generic;
using Game.Core;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Keeps every live ground slab's Custom/BuildingGroundSlab._EdgeMask in sync with actual
    /// grid adjacency, so two flush slabs merge into one continuous pad instead of both fading
    /// into ground on their shared side. Shared by BuildingSpawner and WorldContentSpawner (the
    /// Core's slab needs to react to buildings placed next to it after world generation too).
    /// </summary>
    public sealed class GroundSlabNeighborLinker
    {
        readonly GridRuntime _grid;
        readonly Dictionary<GridCoord, (SpriteRenderer Renderer, Vector2Int FootprintSize)> _entries = new Dictionary<GridCoord, (SpriteRenderer, Vector2Int)>();

        public GroundSlabNeighborLinker(GridRuntime grid)
        {
            _grid = grid;
        }

        /// <summary>origin is the footprint's bottom-left grid cell (BuildingRuntime.Cell), matching GridRuntime's own footprint convention.</summary>
        public void Register(GridCoord origin, Vector2Int footprintSize, SpriteRenderer renderer)
        {
            _entries[origin] = (renderer, footprintSize);
            RefreshAll();
        }

        /// <summary>No-op if origin was never registered (e.g. a conveyor/Splitter/Crossroad view, which never gets a slab).</summary>
        public void Unregister(GridCoord origin)
        {
            if (_entries.Remove(origin)) RefreshAll();
        }

        /// <summary>
        /// Recomputes every registered slab's mask from current grid occupancy. Runs once per
        /// placement/demolition (a player action, not per-frame), so an O(building count) full
        /// scan is negligible - simpler and lower-risk than tracking only the touched neighbors.
        /// </summary>
        void RefreshAll()
        {
            foreach (var entry in _entries)
            {
                RefreshMask(entry.Key, entry.Value.FootprintSize, entry.Value.Renderer);
            }
        }

        void RefreshMask(GridCoord origin, Vector2Int size, SpriteRenderer renderer)
        {
            bool west = SideTouchesSlab(origin.X - 1, origin.Y, size.y, vertical: true);
            bool east = SideTouchesSlab(origin.X + size.x, origin.Y, size.y, vertical: true);
            bool south = SideTouchesSlab(origin.X, origin.Y - 1, size.x, vertical: false);
            bool north = SideTouchesSlab(origin.X, origin.Y + size.y, size.x, vertical: false);

            var propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock); // preserve _UVOffset/_FootprintWorldSize already set by the caller
            propertyBlock.SetVector("_EdgeMask", new Vector4(west ? 1f : 0f, east ? 1f : 0f, south ? 1f : 0f, north ? 1f : 0f));
            renderer.SetPropertyBlock(propertyBlock);
        }

        /// <summary>
        /// True if any cell along one side of a footprint is occupied by another registered
        /// slab. GridRuntime stores occupants as opaque objects (Game.Grid must not depend on
        /// Game.Gameplay); casting to BuildingRuntime here is safe since Presentation already
        /// does the same in BuildingSpawner.
        /// </summary>
        bool SideTouchesSlab(int fixedX, int fixedY, int length, bool vertical)
        {
            for (int i = 0; i < length; i++)
            {
                GridCoord cell = vertical ? new GridCoord(fixedX, fixedY + i) : new GridCoord(fixedX + i, fixedY);
                if (_grid.GetOccupant(cell) is BuildingRuntime buildingRuntime && _entries.ContainsKey(buildingRuntime.Cell))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
