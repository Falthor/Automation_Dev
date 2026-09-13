using UnityEngine;

namespace Game.Data
{
    /// <summary>Static definition of the Storage Box: player-built pooled-inventory building.</summary>
    [CreateAssetMenu(fileName = "StorageDefinition", menuName = "Game/Buildings/Storage Definition")]
    public sealed class StorageDefinition : BuildingDefinition
    {
        /// <summary>Somewhere to put things, not a machine that does anything - see BuildingDefinition.CountsAgainstBuildingCap.</summary>
        public override bool CountsAgainstBuildingCap => false;

        /// <summary>
        /// 0 means "use Game.Gameplay.Items.Inventory's own default" (Data must not reference
        /// Gameplay - StorageRuntime resolves the fallback). Only the Core chest fixture overrides
        /// these today (6 slots instead of the standard 2, 200 per slot - the real constraint is
        /// the number of distinct item types it can hold at once, since
        /// a single demolished Datacenter returns six of them, not the 1200-unit total capacity).
        /// </summary>
        [SerializeField, Min(0)] int slotCountOverride;
        [SerializeField, Min(0)] int capacityPerSlotOverride;

        public int SlotCountOverride => slotCountOverride;
        public int CapacityPerSlotOverride => capacityPerSlotOverride;

        /// <summary>
        /// When true, no conveyor (or splitter/crossroad) may ever connect - StorageRuntime.
        /// CanAcceptInput refuses unconditionally, the same chokepoint CoreRuntime already uses to
        /// refuse every delivery. Only the Core chest fixture sets this: it stays a construction
        /// reserve, never a production dumping ground. A builder
        /// robot's delivery/repatriation is a distinct path (StorageRuntime.AddFromRobot) and is
        /// never gated by this flag.
        /// </summary>
        [SerializeField] bool rejectsConveyorInput;

        public bool RejectsConveyorInput => rejectsConveyorInput;
    }
}
