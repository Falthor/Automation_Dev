using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static, deterministic terrain generation parameters. Gameplay-relevant (which terrain
    /// type each cell authoritatively is) - not visual/rendering settings, which belong to
    /// Presentation instead.
    /// </summary>
    [CreateAssetMenu(fileName = "TerrainGenerationSettings", menuName = "Game/Terrain/Generation Settings")]
    public sealed class TerrainGenerationSettings : ScriptableObject
    {
        [SerializeField, Min(2)] int size = 60;
        [SerializeField] int seed;
        [SerializeField, Min(0.01f)] float terrainScale = 45f;
        [SerializeField, Range(0f, 1f)] float proportion = 0.30f;

        public int Size => size;
        public int Seed => seed;
        public float TerrainScale => terrainScale;
        public float Proportion => proportion;
    }
}
