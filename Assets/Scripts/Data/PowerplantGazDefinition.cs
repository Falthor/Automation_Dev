using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Gas Powerplant: burns Coal_ore to supply Power. Immediately
    /// operational once built (no research gate) but only actually supplies Power while it holds
    /// fuel; burning one unit of it costs CuCostPerCycle from the compute reserve.
    /// </summary>
    [CreateAssetMenu(fileName = "PowerplantGazDefinition", menuName = "Game/Buildings/Powerplant Gaz Definition")]
    public sealed class PowerplantGazDefinition : BuildingDefinition
    {
        [SerializeField] ItemDefinition fuelItem;
        [SerializeField, Min(1)] int maxFuelStack = 20;
        [SerializeField, Min(0f)] float powerOutputKw = 10f;
        [SerializeField, Min(0f)] float selfPowerDemandKw = 2f;
        [SerializeField, Min(0f)] float cuCostPerCycle = 150f;
        [SerializeField, Min(0.01f)] float fuelCycleTimeSeconds = 10f;

        public ItemDefinition FuelItem => fuelItem;
        public int MaxFuelStack => maxFuelStack;
        public float PowerOutputKw => powerOutputKw;
        public float SelfPowerDemandKw => selfPowerDemandKw;
        public override float CuCostPerCycle => cuCostPerCycle;

        /// <summary>Same value as SelfPowerDemandKw, exposed under the base contract's name for the Building menu's generic consumption preview.</summary>
        public override float PowerDemandKw => selfPowerDemandKw;
        public float FuelCycleTimeSeconds => fuelCycleTimeSeconds;
    }
}
