using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Crossroad: two independent straight belt lanes crossing in a single
    /// cell. At zero rotation, one lane runs West-to-East and the other North-to-South; rotating
    /// turns both lanes together (CrossroadRuntime derives each lane's entry/exit from
    /// FacingRotation directly, no separate art-native-direction offset needed since the art's own
    /// zero-rotation pose already matches that reference).
    /// </summary>
    [CreateAssetMenu(fileName = "CrossroadDefinition", menuName = "Game/Buildings/Crossroad Definition")]
    public sealed class CrossroadDefinition : BuildingDefinition
    {
        /// <summary>Transport, not machinery - see BuildingDefinition.CountsAgainstBuildingCap.</summary>
        public override bool CountsAgainstBuildingCap => false;
    }
}
