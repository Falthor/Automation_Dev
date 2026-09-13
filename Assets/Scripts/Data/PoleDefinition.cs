using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the electric pole (ENERGIE.md): carries current over a range and
    /// connects to its neighbours on its own, with no cable the player drags. No fields of its own -
    /// its footprint, sprite and cost are the base's, its range/sag live on PoleNetworkSettings (one
    /// settings asset, not one per pole), and its runtime behaviour lives entirely in
    /// PoleNetworkSystem - this type exists only so ConstructionService can tell a pole apart from
    /// every other building it places.
    /// </summary>
    [CreateAssetMenu(fileName = "PoleDefinition", menuName = "Game/Buildings/Pole Definition")]
    public sealed class PoleDefinition : BuildingDefinition
    {
        /// <summary>A connector, not machinery - same reasoning as a conveyor or a Storage box (BuildingDefinition.CountsAgainstBuildingCap).</summary>
        public override bool CountsAgainstBuildingCap => false;
    }
}
