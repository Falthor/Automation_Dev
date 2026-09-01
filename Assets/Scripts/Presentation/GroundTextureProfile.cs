using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// A swappable ground rendering preset for TerrainView (texture set + tiling). Lets the
    /// active look be changed by reassigning one asset in the Inspector instead of touching code
    /// or TerrainView's own serialized fields.
    /// </summary>
    [CreateAssetMenu(fileName = "GroundTextureProfile", menuName = "Terrain/Ground Texture Profile")]
    public sealed class GroundTextureProfile : ScriptableObject
    {
        public Texture2D groundTexture;
        public Texture2D groundTexture2;
        [Min(0.01f)] public float textureWorldSize = 22f;
        public float variationScale = 0.15f;
        [Range(0.01f, 1f)] public float variationSoftness = 0.35f;
    }
}
