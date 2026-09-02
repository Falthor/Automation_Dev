using UnityEngine;

namespace Game.Data
{
    /// <summary>Static definition of the Extractor: player-built, only valid on an exploitable ore deposit.</summary>
    [CreateAssetMenu(fileName = "ExtractorDefinition", menuName = "Game/Buildings/Extractor Definition")]
    public sealed class ExtractorDefinition : BuildingDefinition
    {
        [SerializeField, Min(0.01f)] float extractionIntervalSeconds = 2f;
        [SerializeField, Min(1)] int itemsPerCycle = 1;
        [SerializeField, Min(0f)] float cuDemand = 10f;
        [SerializeField, Min(0f)] float powerDemandKw = 1f;

        public float ExtractionIntervalSeconds => extractionIntervalSeconds;
        public int ItemsPerCycle => itemsPerCycle;
        public override float CuDemand => cuDemand;
        public override float PowerDemandKw => powerDemandKw;

        public override bool HasOutputArrow => true;
    }
}
