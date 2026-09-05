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

        /// <summary>
        /// Custom/BuildingGroundSlab, carried here from GameRuntime's serialized field rather than
        /// resolved by ProceduralSpriteFactory with Shader.Find. The factory is a plain class
        /// constructed in a dozen places, so it has no inspector of its own - but it already
        /// receives these settings, and an asset reference cannot be stripped from a build the way
        /// a shader reached only by name can. See docs/BUILD.md.
        /// </summary>
        public Shader SlabShader;

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

        /// <summary>Everything the slab needs is present. Any piece missing disables the slab entirely rather than falling back to a placeholder - the convention this project already used for the texture pair, now covering the shader too.</summary>
        public bool CanRenderSlab => SlabDiffuse != null && SlabNormal != null && SlabShader != null;
    }
}
