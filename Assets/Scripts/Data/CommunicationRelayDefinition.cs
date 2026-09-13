using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Communication Relay: projects its own action radius, letting
    /// anything be built again inside it exactly like the Core's own radius does. Placeable outside
    /// every existing radius itself, on already-discovered ground, once
    /// ResearchEffectKind.UnlockOutOfRadiusConstruction is unlocked (CONSTRUCTION.md).
    ///
    /// Draws CU continuously while powered rather than in one shot per cycle - the deliberate
    /// exception to CALCUL.md's "CU is spent in one shot when a cycle starts" rule, since existing
    /// and projecting a radius is not a cycle with a start.
    /// </summary>
    [CreateAssetMenu(fileName = "CommunicationRelayDefinition", menuName = "Game/Buildings/Communication Relay Definition")]
    public sealed class CommunicationRelayDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int actionRadiusCells = 12;
        [SerializeField, Min(0f)] float powerDemandKw;
        [SerializeField, Min(0f)] float cuUpkeepPerSecond = 1f;

        /// <summary>How far the buildable zone this relay projects reaches, in cells - CONSTRUCTION.md.</summary>
        public int ActionRadiusCells => actionRadiusCells;

        public override float PowerDemandKw => powerDemandKw;
        public override float CuUpkeepPerSecond => cuUpkeepPerSecond;
    }
}
