using Game.Core;
using Game.Data;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// A conveyor's visual/logical orientation. Mirrored is an internal representation
    /// detail (corner chirality) that callers never set directly - see CONTRACTS.md
    /// "must not depend on an internal conveyor enum/type".
    /// </summary>
    public readonly struct ConveyorOrientation
    {
        public ConveyorShapeKind Shape { get; }
        public Direction Rotation { get; }
        public bool Mirrored { get; }

        public ConveyorOrientation(ConveyorShapeKind shape, Direction rotation, bool mirrored)
        {
            Shape = shape;
            Rotation = rotation;
            Mirrored = mirrored;
        }
    }
}
