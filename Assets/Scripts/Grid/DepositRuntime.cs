using Game.Core;
using Game.Data;

namespace Game.Grid
{
    /// <summary>
    /// Mutable runtime state for a placed ore deposit. Ore deposits are world entities, not
    /// buildings (PROJECT_ARCHITECTURE.md §12) - this does not extend BuildingRuntime, and
    /// lives in Game.Grid (which owns the ore/deposit registry, §7) rather than Game.Gameplay.
    /// </summary>
    public sealed class DepositRuntime
    {
        public OreDepositDefinition Definition { get; }
        public GridCoord Origin { get; }
        public int RemainingQuantity { get; private set; }

        public OreType OreType => Definition.OreType;

        public DepositRuntime(OreDepositDefinition definition, GridCoord origin)
        {
            Definition = definition;
            Origin = origin;
            RemainingQuantity = definition.InitialQuantity;
        }

        /// <summary>Attempts to extract up to <paramref name="amount"/> units. Returns false once exhausted.</summary>
        public bool TryExtract(int amount, out int extracted)
        {
            extracted = System.Math.Min(amount, RemainingQuantity);
            if (extracted <= 0)
            {
                extracted = 0;
                return false;
            }

            RemainingQuantity -= extracted;
            return true;
        }
    }
}
