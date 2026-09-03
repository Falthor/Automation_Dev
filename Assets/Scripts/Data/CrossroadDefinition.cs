using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Crossroad: two independent straight belt lanes crossing at a
    /// single "+"-shaped footprint (same shape as Splitter - center + one arm per cardinal side,
    /// 3x3 bounding box with free corners). At zero rotation, one lane runs West-to-East and the
    /// other North-to-South; rotating turns both lanes together (CrossroadRuntime derives each
    /// lane's entry/exit from FacingRotation directly, no separate art-native-direction offset
    /// needed since the art's own zero-rotation pose already matches that reference).
    /// </summary>
    [CreateAssetMenu(fileName = "CrossroadDefinition", menuName = "Game/Buildings/Crossroad Definition")]
    public sealed class CrossroadDefinition : BuildingDefinition
    {
        public override Vector2Int[] FootprintCells => CrossShapeCells;

        /// <summary>
        /// Slight overscan so each arm's tip visually reaches into the neighboring conveyor cell
        /// instead of leaving a hairline gap at the seam (same fix as ConveyorView's belt stretch).
        /// </summary>
        public override float RenderOverscan => 1.08f;
    }
}
