using UnityEngine;

namespace Game.Data
{
    /// <summary>Static definition of the Storage Box: player-built pooled-inventory building.</summary>
    [CreateAssetMenu(fileName = "StorageDefinition", menuName = "Game/Buildings/Storage Definition")]
    public sealed class StorageDefinition : BuildingDefinition
    {
        /// <summary>
        /// Minimum delay (seconds) between two accepted deliveries, on every side alike (pushed
        /// into, or pulled off a neighbor's raw pooled output). Without this, a Storage sitting
        /// directly against a production building's output (no conveyor in between) could drain
        /// its entire stack in one tick, since a production building's own pooled output has no
        /// belt-speed gating of its own - only a conveyor's progress meter throttles delivery
        /// naturally. 1 second matches our fastest conveyor today (60 items/min); retune this
        /// when a faster conveyor tier ships.
        /// </summary>
        [SerializeField, Min(0f)] float intakeIntervalSeconds = 1f;

        public float IntakeIntervalSeconds => intakeIntervalSeconds;

        /// <summary>
        /// 0 means "use Game.Gameplay.Items.Inventory's own default" (Data must not reference
        /// Gameplay - StorageRuntime resolves the fallback). Only the Core's starting-resources
        /// fixture overrides these today (3 slots instead of the standard 2, a higher per-slot
        /// cap so its largest starting stack - currently 150 - fits in a single slot).
        /// </summary>
        [SerializeField, Min(0)] int slotCountOverride;
        [SerializeField, Min(0)] int capacityPerSlotOverride;

        public int SlotCountOverride => slotCountOverride;
        public int CapacityPerSlotOverride => capacityPerSlotOverride;
    }
}
