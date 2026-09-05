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
        /// Gameplay - StorageRuntime resolves the fallback). Only the Core chest fixture overrides
        /// these today (6 slots instead of the standard 2, 200 per slot - TASK_05_ROBOT_CONSTRUCTEUR.md
        /// §1b: the real constraint is the number of distinct item types it can hold at once, since
        /// a single demolished Datacenter returns six of them, not the 1200-unit total capacity).
        /// </summary>
        [SerializeField, Min(0)] int slotCountOverride;
        [SerializeField, Min(0)] int capacityPerSlotOverride;

        public int SlotCountOverride => slotCountOverride;
        public int CapacityPerSlotOverride => capacityPerSlotOverride;

        /// <summary>
        /// When true, no conveyor (or splitter/crossroad) may ever connect - StorageRuntime.
        /// CanAcceptInput refuses unconditionally, the same chokepoint CoreRuntime already uses to
        /// refuse every delivery. Only the Core chest fixture sets this (TASK_05_ROBOT_CONSTRUCTEUR.md
        /// §1b): it stays a construction reserve, never a production dumping ground. A builder
        /// robot's delivery/repatriation is a distinct path (StorageRuntime.AddFromRobot) and is
        /// never gated by this flag.
        /// </summary>
        [SerializeField] bool rejectsConveyorInput;

        public bool RejectsConveyorInput => rejectsConveyorInput;
    }
}
