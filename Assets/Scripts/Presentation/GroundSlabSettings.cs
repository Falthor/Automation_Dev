using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Plain data bundle for everything Custom/BuildingGroundSlab needs beyond geometry - built
    /// once by GameRuntime.Start() (the diffuse/normal/darken/band/edge/noise fields come from its
    /// own serialized options, the Biome* fields are read back from TerrainView.GroundMaterial so
    /// the slab's sand-encroachment recomputes the exact same Mars/Gravel04 base layer the real
    /// Ground sprite shows) and passed to both BuildingSpawner and WorldContentSpawner.
    /// </summary>
    public sealed class GroundSlabSettings
    {
        public Texture2D SlabDiffuse;
        public Texture2D SlabNormal;
        public float SlabDarken = 1f;

        public float SandBandWidth = 1f;
        public float EdgeSoftness = 0.6f;
        public float SandNoiseScale = 1.2f;
        public float SandNoiseAmplitude = 0.5f;

        public Texture[] BiomeTextures = new Texture[3];
        public float[] BiomeWeights = new float[3];
        public float BiomeTexCount = 1f;
        public float BiomeCellSize = 12f;
        public float BiomeEdgeSoftness = 0.1f;
        public float BiomeSeed;
        public Vector2 VariationOrigin;
        public Vector2 TextureWorldSize = new Vector2(4f, 4f);

        public bool HasSlabTextures => SlabDiffuse != null && SlabNormal != null;
    }
}
