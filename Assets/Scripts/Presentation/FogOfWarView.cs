using Game.Core;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws the fog of war from the discovery state, and from nothing else.
    ///
    /// <b>It reads, it does not decide.</b> This used to be a disc computed from the Core's
    /// position and current radius - which meant the map had no memory: nothing was "discovered",
    /// so nothing could ever be revealed by anything other than the radius itself. The radius now
    /// writes into <see cref="DiscoveryRuntime"/> and this reads that state, so a mission revealing
    /// a region needs no change here at all. There is deliberately no Core reference and no radius
    /// in this file: recomputing a distance here would quietly restore the old behaviour.
    ///
    /// One quad over the whole map with one texel per cell, in FilterMode.Bilinear. The
    /// interpolation between texels is what turns a per-cell binary field into a boundary the
    /// shader can cut anywhere, and the shader's noise then breaks that boundary up so it does not
    /// read as a circle. The texture is re-uploaded only when the state's version has moved.
    /// </summary>
    public sealed class FogOfWarView : MonoBehaviour
    {
        /// <summary>Custom/FogOfWar. An asset reference, not a Shader.Find by name - see ActionRadiusView for why, and docs/BUILD.md.</summary>
        [SerializeField] Shader fogShader;

        [SerializeField] Color fogColor = new Color(0.02f, 0.03f, 0.05f, 0.96f);

        // The three values below were dialled in on screen, against the real map, and these are the
        // ones that were kept. They are not derived from anything - do not "restore" them to rounder
        // numbers.
        //
        /// <summary>How far the fog fades over its own edge, in threshold units (not world units - the old disc's edgeSoftness was, hence the new name).</summary>
        [SerializeField, Range(0f, 1f)] float borderSoftness = 0.114f;

        /// <summary>Periods per world unit. Far finer than first guessed - see the note in FogOfWar.shader.</summary>
        [SerializeField, Min(0.01f)] float noiseScale = 3f;

        [SerializeField, Range(0f, 1f)] float noiseWeight = 0.396f;

        /// <summary>
        /// Texels per cell along each axis. One is enough for a border broken up by the shader's
        /// own noise; raise it if the edge still reads as too coarse, at the cost of the square of
        /// this in texels (a 300-cell map is 90 000 at 1, 360 000 at 2).
        /// </summary>
        [SerializeField, Range(1, 4)] int texelsPerCell = 1;

        /// <summary>
        /// How far past the map the quad extends, in cells. The camera is unbounded, so without
        /// this, panning off the edge of the world shows unfogged nothing. Sampling out there
        /// clamps to the border texel, which is unknown - so the fog just carries on.
        /// </summary>
        [SerializeField, Min(0f)] float outsideMarginCells = 400f;

        DiscoveryRuntime _discovery;

        /// <summary>Kept only so an Inspector edit to texelsPerCell can rebuild the texture without a restart.</summary>
        GridRuntime _grid;

        SpriteRenderer _renderer;
        Material _material;
        Texture2D _texture;

        /// <summary>Allocated once, at Initialize, and refilled in place - this must not allocate on a frame.</summary>
        byte[] _texels;

        /// <summary>The discovery version already on the GPU. -1 is "nothing uploaded yet", which no real version equals.</summary>
        int _uploadedVersion = -1;

        public void Initialize(DiscoveryRuntime discovery, GridRuntime grid)
        {
            if (discovery == null || grid == null || discovery.Size <= 0) return;

            _discovery = discovery;
            _grid = grid;

            int side = discovery.Size * texelsPerCell;
            float mapWorldSize = discovery.Size * grid.CellSize;
            Vector3 mapMin = grid.CellToWorld(new GridCoord(0, 0));

            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sprite = CreateCenteredUnitSprite();
                _renderer.sortingOrder = SortingBands.Fog;
                _material = new Material(fogShader) { name = "FogOfWar (Instance)" };
                _renderer.sharedMaterial = _material;
            }

            // linear: true, like GroundCoverage's own field texture and for the same reason - an R8
            // sampled through a gamma curve arrives at the shader as a different number than it was
            // written, and this one is compared against a threshold.
            _texture = new Texture2D(side, side, TextureFormat.R8, mipChain: false, linear: true)
            {
                name = "FogOfWar Discovery",
                filterMode = FilterMode.Bilinear,   // the per-cell field only reads as a boundary because of this
                wrapMode = TextureWrapMode.Clamp    // and the fog continues past the map because of this
            };
            _texels = new byte[side * side];

            _material.SetTexture("_FogTex", _texture);
            _material.SetVector("_MapBounds", new Vector4(mapMin.x, mapMin.y, mapWorldSize, mapWorldSize));
            ApplyLook();

            // Centred on the map, and larger than it by the margin on every side.
            float quadSize = mapWorldSize + outsideMarginCells * grid.CellSize * 2f;
            transform.position = new Vector3(mapMin.x + mapWorldSize * 0.5f, mapMin.y + mapWorldSize * 0.5f, transform.position.z);
            transform.localScale = new Vector3(quadSize, quadSize, 1f);

            _uploadedVersion = -1;
            Upload();
        }

        /// <summary>The four dials that shape the border, pushed to the material. Cheap enough to redo on any edit.</summary>
        void ApplyLook()
        {
            if (_material == null) return;

            _material.SetColor("_FogColor", fogColor);
            _material.SetFloat("_EdgeSoftness", borderSoftness);
            _material.SetFloat("_NoiseScale", noiseScale);
            _material.SetFloat("_NoiseWeight", noiseWeight);
        }

        /// <summary>
        /// So the border can be dialled in by hand, in Play mode, against the actual map - which is
        /// the only place it can honestly be judged. Editor-only: Unity never calls this in a build.
        ///
        /// The look is a material push. Changing the resolution instead re-runs Initialize, because
        /// the texture and its buffer are sized from it; the old texture is released first, or
        /// dragging that slider leaks one per notch.
        /// </summary>
        void OnValidate()
        {
            if (_material == null || _discovery == null) return;

            if (_texture != null && _texture.width == _discovery.Size * texelsPerCell)
            {
                ApplyLook();
                return;
            }

            if (_grid == null) return;

            Destroy(_texture);
            Initialize(_discovery, _grid);
        }

        /// <summary>
        /// One integer comparison per frame, and an upload only when the state has actually moved -
        /// which is a handful of times in a run (the starting disc, an extended radius, a mission).
        /// </summary>
        void LateUpdate()
        {
            if (_discovery == null || _discovery.Version == _uploadedVersion) return;
            Upload();
        }

        void Upload()
        {
            PackTexels(_discovery, texelsPerCell, _texels);
            _texture.SetPixelData(_texels, 0);
            _texture.Apply(updateMipmaps: false);
            _uploadedVersion = _discovery.Version;
        }

        /// <summary>
        /// Fills a texel buffer from the discovery state, row-major from the bottom row up.
        ///
        /// That order is not a choice: Texture2D's row 0 is the bottom one and v=0 is the bottom of
        /// the UV range, so writing cell row y into texel row y is what makes the texture line up
        /// with the world instead of arriving mirrored. Public and pure so exactly that can be
        /// asserted without a camera.
        /// </summary>
        public static void PackTexels(DiscoveryRuntime discovery, int texelsPerCell, byte[] texels)
        {
            if (discovery == null || texels == null || texelsPerCell < 1) return;

            int side = discovery.Size * texelsPerCell;
            if (texels.Length < side * side) return;

            for (int y = 0; y < side; y++)
            {
                int cellY = y / texelsPerCell;
                int row = y * side;

                for (int x = 0; x < side; x++)
                {
                    texels[row + x] = discovery.IsDiscovered(new GridCoord(x / texelsPerCell, cellY)) ? (byte)255 : (byte)0;
                }
            }
        }

        static Sprite CreateCenteredUnitSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
