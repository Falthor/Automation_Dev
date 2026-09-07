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
    /// <b>The texture follows the camera; its size has nothing to do with the map's.</b> One texel
    /// per cell over the whole world is 16 MB at 4 000 cells and past most GPUs' maximum texture
    /// size beyond 8 192. But the zoom-out is capped, so the player never sees more than a bounded
    /// patch of world at once: the texture covers a window a little larger than the widest possible
    /// view and moves with it. A 300-cell map and a 10 000-cell map cost exactly the same.
    ///
    /// The window is re-anchored only when the camera approaches its edge, which is what keeps this
    /// from re-uploading every frame - the same trade <see cref="DepthSortLadder"/> makes for draw
    /// order, for the same reason.
    ///
    /// One texel per cell in FilterMode.Bilinear. The interpolation between texels is what turns a
    /// per-cell binary field into a boundary the shader can cut anywhere, and the shader's noise then
    /// breaks that boundary up so it does not read as a circle.
    /// </summary>
    public sealed class FogOfWarView : MonoBehaviour
    {
        /// <summary>Custom/FogOfWar. An asset reference, not a Shader.Find by name - see ActionRadiusView for why, and docs/BUILD.md.</summary>
        [SerializeField] Shader fogShader;

        /// <summary>
        /// Fully opaque, and that is a requirement rather than a taste. Fog used to sit at 0.96 over
        /// terrain that existed everywhere, so the 4 % that leaked through showed a little more
        /// terrain and read as atmosphere. Past the edge of the world there is no terrain, and the
        /// same 4 % showed the camera's skybox - a blue wash where undiscovered ground was a brown
        /// one, which draws the map's border for the player. Measured: at alpha 1 the outside of the
        /// map and the undiscovered inside come out at exactly the same colour.
        ///
        /// The same will be true inside the map once terrain is generated lazily: a hole in the fog's
        /// opacity would then show not a landscape but nothing at all.
        /// </summary>
        [SerializeField] Color fogColor = new Color(0.02f, 0.03f, 0.05f, 1f);

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
        /// Texels per cell along each axis. One is enough for a border broken up by the shader's own
        /// noise; raise it if the edge still reads as too coarse. Now that the texture is a window
        /// rather than the map, the cost is bounded: at 256 cells that is 65 KB at 1 and 262 KB at 2,
        /// whatever the map's size.
        /// </summary>
        [SerializeField, Range(1, 4)] int texelsPerCell = 1;

        /// <summary>
        /// The window's side, in cells. Has to be comfortably wider than the widest possible view, or
        /// there is no margin left and the window re-anchors every frame - a warning says so at
        /// startup if it is too small. What is left over after the view is the slack the camera pans
        /// through between two re-anchorings.
        /// </summary>
        [SerializeField, Min(16)] int windowCells = 256;

        DiscoveryRuntime _discovery;
        GridRuntime _grid;
        Camera _camera;

        SpriteRenderer _renderer;
        Material _material;
        Texture2D _texture;

        /// <summary>Allocated once, at Initialize, and refilled in place - this must not allocate on a frame.</summary>
        byte[] _texels;

        /// <summary>The window's lowest-left cell. Everything uploaded is relative to it.</summary>
        GridCoord _origin;

        /// <summary>Half the widest view, in world units, taken from the zoom-out cap and the camera's aspect.</summary>
        float _viewHalfWidth;
        float _viewHalfHeight;

        bool _anchored;
        bool _reportedTooSmall;

        /// <summary>The discovery version already on the GPU. -1 is "nothing uploaded yet", which no real version equals.</summary>
        int _uploadedVersion = -1;

        /// <summary>Window moves so far. Watched by a test: panning must not re-anchor once per frame.</summary>
        public int AnchorCount { get; private set; }

        /// <summary>The window's lowest-left cell - what the uploaded texels are relative to.</summary>
        public GridCoord WindowOrigin => _origin;

        public void Initialize(DiscoveryRuntime discovery, GridRuntime grid, Camera worldCamera, float maxOrthographicSize)
        {
            if (discovery == null || grid == null || discovery.Size <= 0) return;

            _discovery = discovery;
            _grid = grid;
            _camera = worldCamera;

            float aspect = worldCamera != null && worldCamera.aspect > 0f ? worldCamera.aspect : 16f / 9f;
            _viewHalfHeight = Mathf.Max(0f, maxOrthographicSize);
            _viewHalfWidth = _viewHalfHeight * aspect;

            int side = windowCells * texelsPerCell;

            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sprite = CreateCenteredUnitSprite();
                _renderer.sortingOrder = SortingBands.Fog;
                _material = new Material(fogShader) { name = "FogOfWar (Instance)" };
                _renderer.sharedMaterial = _material;
            }

            if (_texture != null) Destroy(_texture);

            // linear: true, like GroundCoverage's own field texture and for the same reason - an R8
            // sampled through a gamma curve arrives at the shader as a different number than it was
            // written, and this one is compared against a threshold.
            _texture = new Texture2D(side, side, TextureFormat.R8, mipChain: false, linear: true)
            {
                name = "FogOfWar Discovery",
                filterMode = FilterMode.Bilinear,   // the per-cell field only reads as a boundary because of this
                wrapMode = TextureWrapMode.Clamp
            };
            _texels = new byte[side * side];

            // The quad is the window, so it always covers the view - which is the same guarantee that
            // lets the window be small. Scale is set here; position moves with each anchoring.
            transform.localScale = new Vector3(windowCells * grid.CellSize, windowCells * grid.CellSize, 1f);

            _material.SetTexture("_FogTex", _texture);
            ApplyLook();

            _anchored = false;
            _uploadedVersion = -1;
            FollowCamera();
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
        /// The look is a material push. Changing the resolution or the window size instead re-runs
        /// Initialize, because the texture and its buffer are sized from them.
        /// </summary>
        void OnValidate()
        {
            if (_material == null || _discovery == null) return;

            if (_texture != null && _texture.width == windowCells * texelsPerCell)
            {
                ApplyLook();
                return;
            }

            if (_grid == null) return;
            Initialize(_discovery, _grid, _camera, _viewHalfHeight);
        }

        /// <summary>
        /// Two reasons to touch the GPU, and neither happens on most frames: the window moved, or the
        /// discovery state did. Everything else is one comparison.
        /// </summary>
        void LateUpdate()
        {
            if (_discovery == null) return;

            if (FollowCamera()) return;   // an anchoring re-uploads on its own
            if (_discovery.Version == _uploadedVersion) return;

            Upload();
        }

        /// <summary>
        /// Moves the window if the camera has left its slack, and answers whether it did.
        ///
        /// The origin is snapped to whole cells: the texture is one texel per cell, so an origin
        /// landing mid-cell would shift every texel against the world by a fraction and make the fog
        /// crawl as the camera moves.
        /// </summary>
        bool FollowCamera()
        {
            if (_camera == null || _grid == null) return false;

            Vector3 cameraWorld = _camera.transform.position;
            GridCoord wanted = WindowOriginFor(cameraWorld);

            if (_anchored && WithinSlack(cameraWorld)) return false;

            _origin = wanted;
            _anchored = true;
            AnchorCount++;

            Vector3 windowMin = _grid.CellToWorld(_origin);
            float windowWorldSize = windowCells * _grid.CellSize;

            // Stated to the shader rather than left to be inferred from the quad's transform, exactly
            // as Custom/GroundCoverage does with _ZoneBounds.
            _material.SetVector("_WindowBounds", new Vector4(windowMin.x, windowMin.y, windowWorldSize, windowWorldSize));
            transform.position = new Vector3(windowMin.x + windowWorldSize * 0.5f, windowMin.y + windowWorldSize * 0.5f, transform.position.z);

            Upload();
            return true;
        }

        /// <summary>The window that centres on the camera, snapped to whole cells.</summary>
        GridCoord WindowOriginFor(Vector3 cameraWorld)
        {
            GridCoord cameraCell = _grid.WorldToCell(cameraWorld);
            int half = windowCells / 2;
            return new GridCoord(cameraCell.X - half, cameraCell.Y - half);
        }

        /// <summary>
        /// Whether the widest possible view still fits inside the current window. Asked against the
        /// zoom-out cap rather than the camera's current size, so zooming out never outruns the
        /// window - the answer must not depend on how far the player happens to be zoomed right now.
        /// </summary>
        bool WithinSlack(Vector3 cameraWorld)
        {
            Vector3 windowMin = _grid.CellToWorld(_origin);
            float windowWorldSize = windowCells * _grid.CellSize;

            float slackX = windowWorldSize * 0.5f - _viewHalfWidth;
            float slackY = windowWorldSize * 0.5f - _viewHalfHeight;

            if ((slackX <= 0f || slackY <= 0f) && !_reportedTooSmall)
            {
                _reportedTooSmall = true;
                Debug.LogError($"FogOfWarView: a window of {windowCells} cells cannot contain the widest view "
                    + $"({_viewHalfWidth * 2f} x {_viewHalfHeight * 2f} world units). Raise windowCells or lower the zoom-out cap.", this);
            }

            Vector2 windowCenter = new Vector2(windowMin.x + windowWorldSize * 0.5f, windowMin.y + windowWorldSize * 0.5f);
            return Mathf.Abs(cameraWorld.x - windowCenter.x) <= slackX
                && Mathf.Abs(cameraWorld.y - windowCenter.y) <= slackY;
        }

        void Upload()
        {
            PackTexels(_discovery, _origin, windowCells, texelsPerCell, _texels);
            _texture.SetPixelData(_texels, 0);
            _texture.Apply(updateMipmaps: false);
            _uploadedVersion = _discovery.Version;
        }

        /// <summary>
        /// Fills a texel buffer from the discovery state, row-major from the bottom row up, for the
        /// window whose lowest-left cell is <paramref name="origin"/>.
        ///
        /// That order is not a choice: Texture2D's row 0 is the bottom one and v=0 is the bottom of
        /// the UV range, so writing the window's row y into texel row y is what makes the texture line
        /// up with the world instead of arriving mirrored. Public and pure so exactly that can be
        /// asserted without a camera.
        ///
        /// Cells outside the map read as undiscovered, which is what makes the world's edge fog over
        /// with no special case: there is nothing out there to have discovered.
        /// </summary>
        public static void PackTexels(DiscoveryRuntime discovery, GridCoord origin, int windowCells, int texelsPerCell, byte[] texels)
        {
            if (discovery == null || texels == null || texelsPerCell < 1 || windowCells < 1) return;

            int side = windowCells * texelsPerCell;
            if (texels.Length < side * side) return;

            for (int y = 0; y < side; y++)
            {
                int cellY = origin.Y + y / texelsPerCell;
                int row = y * side;

                for (int x = 0; x < side; x++)
                {
                    texels[row + x] = discovery.IsDiscovered(new GridCoord(origin.X + x / texelsPerCell, cellY)) ? (byte)255 : (byte)0;
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
