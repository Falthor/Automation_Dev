using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// A swappable ground rendering preset for TerrainView: two independent randomized noise
    /// fields (see ShadedGroundTiled.shader) - the same single-octave value-noise smoothstep
    /// blend technique validated earlier for a directional gradient, generalized to be isotropic
    /// (random in every direction, not just one axis) and layered. The base layer's textures
    /// split the whole map by weighted bands of one field at biomeCellSize, smoothly blended
    /// (not dithered - a plain lerp at this small scale reads as soft grain, not a halo); the
    /// accent layer's textures are sparse and overlaid on top wherever a second,
    /// independently-seeded field at accentCellSize crosses its own threshold, so they read as
    /// scattered small patches rather than nesting inside the base layer's shapes. Both feature
    /// sizes are kept small (a handful of world units) so several alternations are visible within
    /// a normal camera view, rather than a couple of map-spanning regions. Both layer counts can
    /// grow later (up to MaxBiomeTextures each) as more ground art is added, without further
    /// shader changes. Each texture may optionally carry a matching normal map (baseNormals /
    /// accentNormals) for TerrainView's fixed-direction relief lighting - purely cosmetic, no
    /// gameplay dependency. Lets the active look/layout be changed by reassigning one asset in the
    /// Inspector instead of touching code or TerrainView's own serialized fields.
    /// </summary>
    [CreateAssetMenu(fileName = "GroundTextureProfile", menuName = "Terrain/Ground Texture Profile")]
    public sealed class GroundTextureProfile : ScriptableObject
    {
        // Each layer needs 2 sampler slots per texture (diffuse + normal), and base+accent share
        // the same shader: 4 * MaxBiomeTextures total samplers must stay under hardware/shader
        // model limits (16 on the common ps_4_0 profile) - keep this at 3 (12 samplers) unless the
        // shader is also updated to target a higher shader model.
        public const int MaxBiomeTextures = 3;

        [Header("Base textures (dominant, tile the whole map)")]
        public Texture2D[] baseTextures = new Texture2D[0];
        [Tooltip("Relative pick weight per base texture, same length as baseTextures. Missing/zero entries fall back to an equal split.")]
        public float[] baseWeights = new float[0];
        [Tooltip("Optional normal map per base texture, same length/order as baseTextures, for the relief lighting on TerrainView. A missing entry falls back to a flat (unbumped) normal.")]
        public Texture2D[] baseNormals = new Texture2D[0];
        [Min(0.01f)] public float textureWorldSize = 22f;

        [Header("Base region layout (randomized noise field)")]
        [Tooltip("World-unit size of one base noise feature - kept small (a handful of units) so several alternations are visible within a normal camera view.")]
        [Min(1f)] public float biomeCellSize = 12f;
        [Tooltip("Width of the smooth blend into a neighboring texture, in field-value units (0.01 = a thin band, 0.4 = a wide one).")]
        [Range(0.01f, 0.4f)] public float biomeEdgeSoftness = 0.1f;

        [Header("Accent textures (sparse, scattered small patches on top of the base)")]
        public Texture2D[] accentTextures = new Texture2D[0];
        [Tooltip("Relative pick weight per accent texture, same length as accentTextures. Missing/zero entries fall back to an equal split.")]
        public float[] accentWeights = new float[0];
        [Tooltip("Optional normal map per accent texture, same length/order as accentTextures. A missing entry falls back to a flat (unbumped) normal.")]
        public Texture2D[] accentNormals = new Texture2D[0];
        [Tooltip("Total map area share the accent layer covers, randomized within [Min, Max] each game (the base layer fills the rest).")]
        [Range(0f, 1f)] public float accentShareMin = 0.15f;
        [Range(0f, 1f)] public float accentShareMax = 0.25f;

        [Header("Accent region layout (independent, smaller noise field)")]
        [Tooltip("World-unit size of one accent patch - kept smaller than biomeCellSize so accents stay small and scattered.")]
        [Min(1f)] public float accentCellSize = 7f;
        [Tooltip("Width of the smooth blend at an accent patch's edge, in field-value units.")]
        [Range(0.01f, 0.4f)] public float accentEdgeSoftness = 0.1f;

        [Header("Seed")]
        [Tooltip("When enabled, a new random layout (including the base/accent area split) is generated every time the game starts. Disable to reproduce a specific fixed layout (e.g. for testing).")]
        public bool randomizeSeedEachRun = true;
        public int seed;
    }
}
