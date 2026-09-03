using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Laboratory: converts Data_Card into RP continuously
    /// (independent of an active research), and contributes to whichever research is active.
    /// </summary>
    [CreateAssetMenu(fileName = "LaboratoryDefinition", menuName = "Game/Buildings/Laboratory Definition")]
    public sealed class LaboratoryDefinition : BuildingDefinition
    {
        [SerializeField] ItemDefinition cardItem;
        [SerializeField, Min(1)] int maxCardStack = 100;
        [SerializeField, Min(0f)] float powerDemandKw = 3f;
        [SerializeField, Min(0f)] float cuCostPerCycle = 250f;
        [SerializeField, Min(0.01f)] float cardConvertIntervalSeconds = 2f;
        [SerializeField, Min(0f)] float rpPerCard = 2f;

        public ItemDefinition CardItem => cardItem;
        public int MaxCardStack => maxCardStack;
        public override float PowerDemandKw => powerDemandKw;
        public override float CuCostPerCycle => cuCostPerCycle;
        public float CardConvertIntervalSeconds => cardConvertIntervalSeconds;
        public float RpPerCard => rpPerCard;

        /// <summary>
        /// It takes Data Cards in but produces nothing physical, so it gets entry arrows without
        /// an output arrow: three sides accept deliveries and the fourth (its facing side, the
        /// one CanAcceptInput refuses) is the one left unmarked.
        /// </summary>
        public override bool HasInputArrows => true;
    }
}
