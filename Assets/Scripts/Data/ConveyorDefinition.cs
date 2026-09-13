using Game.Core;
using UnityEngine;

namespace Game.Data
{
    [CreateAssetMenu(fileName = "ConveyorDefinition", menuName = "Game/Buildings/Conveyor Definition")]
    public sealed class ConveyorDefinition : BuildingDefinition
    {
        /// <summary>A belt is transport, not machinery - see BuildingDefinition.CountsAgainstBuildingCap.</summary>
        public override bool CountsAgainstBuildingCap => false;

        /// <summary>The belt itself - see BuildingDefinition.IsTransportPiece.</summary>
        public override bool IsTransportPiece => true;

        [SerializeField] ConveyorShapeKind defaultShape = ConveyorShapeKind.Straight;

        /// <summary>
        /// The Belt Relay: a straight/corner conveyor is still just this same definition with the
        /// flag set, never a subclass - ConveyorDefinition is sealed and every existing shape
        /// (straight, corner, the drag-continuation variants) is already "one asset, one flag
        /// combination" rather than a type hierarchy. Read by ConstructionService's out-of-radius
        /// run-length check: placing one resets the 40-cell counter for whatever continues past it
        /// (CONSTRUCTION.md).
        /// </summary>
        [SerializeField] bool isRunLengthReset;

        [Header("Art override (optional - falls back to a procedural placeholder shape)")]
        [SerializeField] Sprite overrideSprite;
        [SerializeField] Direction artNativeDirection = Direction.North;

        /// <summary>Which shape this buildable item represents (straight/corner/crossroad are separate definitions).</summary>
        public ConveyorShapeKind DefaultShape => defaultShape;

        /// <summary>Whether placing this one resets the out-of-radius run-length counter (CONSTRUCTION.md) - the Belt Relay.</summary>
        public bool IsRunLengthReset => isRunLengthReset;

        /// <summary>
        /// Real art asset for this conveyor's default shape, or null to use the procedural
        /// placeholder. Only valid while the runtime orientation still matches DefaultShape -
        /// a reshaped conveyor (e.g. straight -> corner via a drag turn) falls back to
        /// procedural rendering for its new shape.
        /// </summary>
        public Sprite OverrideSprite => overrideSprite;

        /// <summary>Direction the override sprite art visually points to at zero rotation (e.g. East for an arrow pointing right).</summary>
        public Direction ArtNativeDirection => artNativeDirection;

        /// <summary>
        /// A corner's art connects to a neighbor on two perpendicular edges (entry and exit) -
        /// a slight uniform overscan closes both seams at once (straight instead stretches only
        /// its length axis via ConveyorView.LengthStretchFactor, so it doesn't need this).
        /// </summary>
        public override float RenderOverscan => defaultShape == ConveyorShapeKind.Corner ? 1.02f : 1f;
    }
}
