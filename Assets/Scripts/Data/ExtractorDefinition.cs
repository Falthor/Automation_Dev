using UnityEngine;

namespace Game.Data
{
    /// <summary>Static definition of the Extractor: player-built, only valid on an exploitable ore deposit.</summary>
    [CreateAssetMenu(fileName = "ExtractorDefinition", menuName = "Game/Buildings/Extractor Definition")]
    public sealed class ExtractorDefinition : BuildingDefinition
    {
        [SerializeField, Min(0.01f)] float extractionIntervalSeconds = 2f;
        [SerializeField, Min(1)] int itemsPerCycle = 1;
        [SerializeField, Min(0f)] float cuCostPerCycle = 50f;
        [SerializeField, Min(0f)] float powerDemandKw = 1f;

        public float ExtractionIntervalSeconds => extractionIntervalSeconds;
        public int ItemsPerCycle => itemsPerCycle;

        /// <summary>
        /// Ore pulled out of the deposit per minute, at full power and with the compute to pay for
        /// each cycle - the rated figure the Building menu quotes on hover.
        ///
        /// Derived from the interval and the yield rather than configured beside them, so the two
        /// cannot disagree with the number shown. It is a rating, not a promise: an extractor short
        /// of power or of CU produces less, and one whose buffer has filled up because nothing is
        /// carrying the ore away produces nothing at all. ExtractorThroughputTests measures a running
        /// extractor against it.
        /// </summary>
        public float ItemsPerMinute => extractionIntervalSeconds > 0f ? itemsPerCycle / extractionIntervalSeconds * 60f : 0f;
        public override float CuCostPerCycle => cuCostPerCycle;
        public override float PowerDemandKw => powerDemandKw;

        public override bool HasOutputArrow => true;
    }
}
