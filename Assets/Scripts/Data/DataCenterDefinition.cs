using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Data Center: aggregates installed CPU/Memory components into
    /// Compute supply and Power demand. Slot counts/timers live on the runtime, not here - this
    /// only configures the pooled input side and the placement gate.
    /// </summary>
    [CreateAssetMenu(fileName = "DataCenterDefinition", menuName = "Game/Buildings/Data Center Definition")]
    public sealed class DataCenterDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int maxStackPerItem = 10;
        [SerializeField] string[] acceptedItemIds = { "cpu_mkI", "Memory_MK1" };

        public int MaxStackPerItem => maxStackPerItem;
        public string[] AcceptedItemIds => acceptedItemIds;
    }
}
