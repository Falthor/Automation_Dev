using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Splitter: a "+"-shaped logistics building routing a single
    /// conveyor-fed item across up to 3 outputs. Its footprint is a plus shape (center + one
    /// arm per cardinal side) inside a 3x3 bounding box - the 4 corners are left free for other
    /// buildings, matching the art asset's own cross silhouette.
    /// </summary>
    [CreateAssetMenu(fileName = "SplitterDefinition", menuName = "Game/Buildings/Splitter Definition")]
    public sealed class SplitterDefinition : BuildingDefinition
    {
        [SerializeField] Direction artNativeEntrySide = Direction.West;

        public override Vector2Int[] FootprintCells => CrossShapeCells;

        /// <summary>Which side the sprite's own chevron/entry marking visually shows at zero rotation.</summary>
        public Direction ArtNativeEntrySide => artNativeEntrySide;

        /// <summary>
        /// Slight overscan so each arm's tip visually reaches into the neighboring conveyor cell
        /// instead of leaving a hairline gap at the seam (same fix as Crossroad/ConveyorView).
        /// </summary>
        public override float RenderOverscan => 1.08f;
    }
}
