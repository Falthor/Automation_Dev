using Game.Core;
using Game.Data;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// Whichever lane that side is the entry of, if that lane is free.
        ///
        /// <b>Without this a crossroad could not be handed anything.</b> The base answer is
        /// <c>false</c>, and a crossroad overrode neither this nor <c>PeekPullableItem</c> - so a
        /// splitter (or a production building) wired straight into one had its push refused, while
        /// the crossroad's own pull found nothing on the splitter's side either, since a splitter
        /// exposes no pullable item. Belts worked because a conveyor implements both halves. This
        /// is the receiving half, and it is deliberately the same two-line shape as
        /// <see cref="SplitterRuntime.CanAcceptInput"/>.
        /// </summary>
        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (fromDirection == EntryA) return !HasItemA;
            if (fromDirection == EntryB) return !HasItemB;
            return false;
        }

        /// <summary>Routes to the lane the side belongs to. Only ever called after CanAcceptInput agreed, so the lane is free.</summary>
        public override void AddInput(string itemId, int amount, Direction fromDirection)
        {
            if (fromDirection == EntryA) ReceiveA(itemId);
            else if (fromDirection == EntryB) ReceiveB(itemId);
        }

        public GridCoord ArmCell(Direction direction) => CrossFootprint.ArmCell(Cell, direction);
        public GridCoord NeighborCell(Direction direction) => CrossFootprint.NeighborCell(Cell, direction);

        /// <summary>Both lanes leave, so both count. The base answer - one edge derived from ExitDirection - would name FacingRotation, which here is lane B's <b>entry</b>.</summary>
        public override bool FeedsCell(GridCoord cell) => cell == NeighborCell(ExitA) || cell == NeighborCell(ExitB);

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

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["hasItemA"] = HasItemA,
                ["itemA"] = _itemA as string,
                ["progressA"] = _progressA,
                ["hasItemB"] = HasItemB,
                ["itemB"] = _itemB as string,
                ["progressB"] = _progressB
            };
        }

        public override void RestoreState(JObject state)
        {
            HasItemA = state.Value<bool?>("hasItemA") ?? false;
            _itemA = HasItemA ? state.Value<string>("itemA") : null;
            _progressA = state.Value<float?>("progressA") ?? 0f;

            HasItemB = state.Value<bool?>("hasItemB") ?? false;
            _itemB = HasItemB ? state.Value<string>("itemB") : null;
            _progressB = state.Value<float?>("progressB") ?? 0f;
        }
    }
}
