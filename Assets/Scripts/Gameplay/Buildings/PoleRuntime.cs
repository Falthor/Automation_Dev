using Game.Core;
using Game.Data;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// A placed electric pole. Passive on its own - it neither ticks nor holds anything; everything
    /// about which poles are connected, which network it belongs to and whether that network reaches
    /// a source lives in Power.PoleNetworkSystem, the one place that reasons about the graph
    /// (ENERGIE.md).
    /// </summary>
    public sealed class PoleRuntime : BuildingRuntime
    {
        /// <summary>
        /// Which connected group this pole belongs to - assigned by PoleNetworkSystem, never by this
        /// class. -1 means not yet registered with a network.
        ///
        /// Not saved: it is entirely derived from every pole's position and the order they were
        /// placed in, so replaying PoleNetworkSystem.RegisterPole for each restored pole in save
        /// order reconstructs the exact same groups the session had - the same "what comes from the
        /// player is saved, what is derived is recomputed" rule WreckField's own seed-derived layout
        /// already follows.
        /// </summary>
        public int NetworkId { get; internal set; } = -1;

        public PoleRuntime(PoleDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
        }
    }
}
