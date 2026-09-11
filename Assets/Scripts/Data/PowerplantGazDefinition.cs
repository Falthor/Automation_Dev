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
        [SerializeField, Min(0f)] float cuCostPerCycle = 150f;
        [SerializeField, Min(0.01f)] float fuelCycleTimeSeconds = 10f;

        public ItemDefinition FuelItem => fuelItem;
        public int MaxFuelStack => maxFuelStack;
        public float PowerOutputKw => powerOutputKw;
        public override float CuCostPerCycle => cuCostPerCycle;
        public float FuelCycleTimeSeconds => fuelCycleTimeSeconds;

        // No PowerDemandKw override: a plant draws nothing from the network it feeds, so the base's
        // 0 is the truth and the Building menu's consumption preview stays silent about it. It used
        // to declare 2 kW of self-consumption, which was a load nobody could switch off and which
        // this system has no way to arbitrate - the plant never consulted the power gate.
    }
}
