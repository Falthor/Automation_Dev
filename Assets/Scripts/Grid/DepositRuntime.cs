using Game.Core;
using Game.Data;

namespace Game.Grid
{
    /// <summary>
    /// A placed ore deposit. Ore deposits are world entities, not buildings (MAP.md) - this does
    /// not extend BuildingRuntime, and lives in Game.Grid (which owns the ore/deposit registry)
    /// rather than Game.Gameplay.
    ///
    /// <b>A deposit never runs out</b>, so it holds no quantity and has
    /// nothing to save: it is immutable once placed. What pushes the player to expand is throughput,
    /// not scarcity - a cluster offers four extractor slots, and producing more means reaching other
    /// clusters, extending the Core's radius and exploring. The engine of expansion is the
    /// production ceiling, never depletion.
    /// </summary>
    public sealed class DepositRuntime
    {
        public OreDepositDefinition Definition { get; }
        public GridCoord Origin { get; }

        public string ItemId => Definition.Item.Id;

        public DepositRuntime(OreDepositDefinition definition, GridCoord origin)
        {
            Definition = definition;
            Origin = origin;
        }
    }
}
