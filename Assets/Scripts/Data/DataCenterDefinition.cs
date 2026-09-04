using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Data Center: aggregates installed CPU/Memory components into
    /// Compute supply and Power demand. Slot counts/timers live on the runtime, not here - this
    /// only configures the pooled input side, the placement gate, and the two tunables
    /// TASK_03_DATACENTER.md calls out as parameters rather than buried constants.
    /// </summary>
    [CreateAssetMenu(fileName = "DataCenterDefinition", menuName = "Game/Buildings/Data Center Definition")]
    public sealed class DataCenterDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int maxStackPerItem = 10;
        [SerializeField] string[] acceptedItemIds = { "cpu_mkI", "Memory_MK1" };

        /// <summary>
        /// Floor of the axis-yield formula (rendement = floor + (1-floor) * concentration,
        /// TASK_03_DATACENTER.md §7) - 0.20 with the current two axes (research/buildings). Will
        /// become 0.35 once the armament axis opens, so a three-way split stays tenable; not
        /// anticipated here, but kept as this one field specifically so that future change is a
        /// single edit rather than a hunt through the formula's code.
        /// </summary>
        [SerializeField, Range(0f, 1f)] float axisYieldFloor = 0.20f;

        /// <summary>Seed for the per-component nominal-lifetime draw (TASK_03_DATACENTER.md §4.4) - same seed and installation sequence must reproduce the same drawn lifetimes.</summary>
        [SerializeField] int componentLifetimeSeed = 12345;

        public int MaxStackPerItem => maxStackPerItem;
        public string[] AcceptedItemIds => acceptedItemIds;
        public float AxisYieldFloor => axisYieldFloor;
        public int ComponentLifetimeSeed => componentLifetimeSeed;
    }
}
