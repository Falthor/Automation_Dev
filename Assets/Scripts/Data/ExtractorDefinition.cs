using UnityEngine;

namespace Game.Data
{
    /// <summary>Static definition of the Extractor: player-built, only valid on an exploitable ore deposit.</summary>
    [CreateAssetMenu(fileName = "ExtractorDefinition", menuName = "Game/Buildings/Extractor Definition")]
    public sealed class ExtractorDefinition : BuildingDefinition
    {
        [SerializeField] Sprite sprite;
        [SerializeField, Min(0.01f)] float extractionIntervalSeconds = 2f;
        [SerializeField, Min(1)] int itemsPerCycle = 1;

        public Sprite Sprite => sprite;
        public float ExtractionIntervalSeconds => extractionIntervalSeconds;
        public int ItemsPerCycle => itemsPerCycle;

        public override bool HasOutputArrow => true;
    }
}
