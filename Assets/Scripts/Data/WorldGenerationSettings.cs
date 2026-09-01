using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Deterministic world-content generation parameters: which Core and ore deposit
    /// definitions to place at game start, and the seed driving deposit placement within the
    /// Core's action radius. Separate from TerrainGenerationSettings, which only governs the
    /// ground terrain type per cell.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "Game/World/Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [SerializeField] CoreDefinition coreDefinition;
        [SerializeField] OreDepositDefinition ironOreDefinition;
        [SerializeField] OreDepositDefinition copperOreDefinition;
        [SerializeField] OreDepositDefinition coalOreDefinition;
        [SerializeField] int resourceSeed;

        public CoreDefinition CoreDefinition => coreDefinition;
        public OreDepositDefinition IronOreDefinition => ironOreDefinition;
        public OreDepositDefinition CopperOreDefinition => copperOreDefinition;
        public OreDepositDefinition CoalOreDefinition => coalOreDefinition;
        public int ResourceSeed => resourceSeed;
    }
}
