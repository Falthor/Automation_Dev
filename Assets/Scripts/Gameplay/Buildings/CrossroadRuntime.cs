using Game.Core;
using Game.Data;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Two independent single-item belt lanes crossing at a "+"-shaped footprint (see
    /// CrossFootprint), matching the art's own two fixed straight-through lanes. At
    /// FacingRotation=North, lane A runs West-to-East and lane B runs North-to-South; rotating
    /// turns both lanes together (e.g. one 90-degree step gives North-to-South and East-to-West).
    /// Entirely driven by TransportSystem's dedicated crossroad step, for the same reason as
    /// Splitter: its input/output cells are arm tips of a "+" footprint, not a rectangle.
    /// </summary>
    public sealed class CrossroadRuntime : BuildingRuntime
    {
        object _itemA;
        float _progressA;
        object _itemB;
        float _progressB;

        public bool HasItemA { get; private set; }
        public bool HasItemB { get; private set; }
        public float ProgressA => _progressA;
        public float ProgressB => _progressB;
        public object ItemA => _itemA;
        public object ItemB => _itemB;

        int Steps => (int)FacingRotation;

        public Direction EntryA => Direction.West.RotateCW(Steps);
        public Direction ExitA => Direction.East.RotateCW(Steps);
        public Direction EntryB => Direction.North.RotateCW(Steps);
        public Direction ExitB => Direction.South.RotateCW(Steps);

        public CrossroadRuntime(CrossroadDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
        }

        public GridCoord ArmCell(Direction direction) => CrossFootprint.ArmCell(Cell, direction);
        public GridCoord NeighborCell(Direction direction) => CrossFootprint.NeighborCell(Cell, direction);

        public void ReceiveA(object item)
        {
            _itemA = item;
            HasItemA = true;
            _progressA = 0f;
        }

        public void ReceiveB(object item)
        {
            _itemB = item;
            HasItemB = true;
            _progressB = 0f;
        }

        public void AdvanceItems(float deltaTime, float speedCellsPerSecond)
        {
            if (HasItemA) _progressA = System.Math.Min(1f, _progressA + deltaTime * speedCellsPerSecond);
            if (HasItemB) _progressB = System.Math.Min(1f, _progressB + deltaTime * speedCellsPerSecond);
        }

        public void ClearA()
        {
            _itemA = null;
            HasItemA = false;
            _progressA = 0f;
        }

        public void ClearB()
        {
            _itemB = null;
            HasItemB = false;
            _progressB = 0f;
        }
    }
}
