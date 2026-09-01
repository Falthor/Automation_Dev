using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of a world-generated ore deposit. Placed automatically by world
    /// generation, never by the player - a static resource marker only, no extraction logic
    /// yet (PlaceholderColor from the base class stands in for real art).
    /// </summary>
    [CreateAssetMenu(fileName = "OreDepositDefinition", menuName = "Game/World/Ore Deposit Definition")]
    public sealed class OreDepositDefinition : BuildingDefinition
    {
        [SerializeField] OreType oreType;
        [SerializeField, Min(1)] int initialQuantity = 1000;

        public OreType OreType => oreType;
        public int InitialQuantity => initialQuantity;
    }
}
