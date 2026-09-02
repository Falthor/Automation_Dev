using Game.Core;
using UnityEngine;

namespace Game.Data
{
    [CreateAssetMenu(fileName = "ConveyorDefinition", menuName = "Game/Buildings/Conveyor Definition")]
    public sealed class ConveyorDefinition : BuildingDefinition
    {
        [SerializeField] ConveyorShapeKind defaultShape = ConveyorShapeKind.Straight;

        [Header("Art override (optional - falls back to a procedural placeholder shape)")]
        [SerializeField] Sprite overrideSprite;
        [SerializeField] Direction artNativeDirection = Direction.North;

        /// <summary>Which shape this buildable item represents (straight/corner/crossroad are separate definitions).</summary>
        public ConveyorShapeKind DefaultShape => defaultShape;

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
