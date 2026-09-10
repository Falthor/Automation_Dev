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
    ///
    /// <b>Two fields, two channels of one texture.</b> R is what has ever been discovered, G is what
    /// is observed right now (<see cref="ObservationRuntime"/>) - RG16, so the pair costs exactly
    /// what two R8 textures would and buys three things they would not: one upload instead of two,
    /// one sample instead of two, and a window the two fields cannot disagree about. They are read
    /// against the same origin at the same instant because they are the same fetch.
    ///
    /// <b>They change on completely different clocks, and the upload follows each.</b> Discovery
    /// moves rarely - a revelation - while observation moves whenever an observer does, so every
    /// frame a robot is walking. A discovery change repacks both channels; an observation change
    /// repacks only G, which is what keeps the per-frame cost to distance tests instead of 65 000
    /// chunk lookups.
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
        /// How heavily remembered ground is veiled, against the full opacity of the unknown.
        ///
        /// <b>The main dial of the third state, and the one thing that can make it invisible.</b> Too
        /// close to 1 and remembered ground reads as unknown, so the map has two visible states
        /// instead of three and the whole distinction is gone; at 0 there is no veil and it has two
        /// again, the other way round. A starting value, to be judged on screen.
        /// </summary>
        [SerializeField, Range(0f, 1f)] float rememberedVeil = 0.55f;

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

        /// <summary>Where the observers are this frame. Optional: null means nothing observes, so the whole discovered map reads as remembered - which is the truthful picture of a world with no observers rather than a broken one.</summary>
        ObservationRuntime _observation;

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

        /// <summary>The observation version already on the GPU, watched separately because it moves on a different clock - see the class summary.</summary>
        int _uploadedObservationVersion = -1;

        /// <summary>Window moves so far. Watched by a test: panning must not re-anchor once per frame.</summary>
        public int AnchorCount { get; private set; }

        /// <summary>The window's lowest-left cell - what the uploaded texels are relative to.</summary>
        public GridCoord WindowOrigin => _origin;

        public void Initialize(DiscoveryRuntime discovery, ObservationRuntime observation, GridRuntime grid,
            Camera worldCamera, float maxOrthographicSize)
        {
            if (discovery == null || grid == null || discovery.Size <= 0) return;

            _discovery = discovery;
            _observation = observation;
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

            // linear: true, like GroundCoverage's own field texture and for the same reason - a
            // channel sampled through a gamma curve arrives at the shader as a different number than
            // it was written, and both of these are compared against a threshold.
            //
            // RG16 rather than two R8 textures: R is discovery, G is observation. Same bytes, one
            // upload, one sample, and the two fields can never end up describing different windows.
            _texture = new Texture2D(side, side, TextureFormat.RG16, mipChain: false, linear: true)
            {
                name = "FogOfWar Discovery+Observation",
                filterMode = FilterMode.Bilinear,   // the per-cell fields only read as boundaries because of this
                wrapMode = TextureWrapMode.Clamp
            };
            _texels = new byte[side * side * BytesPerTexel];

            // The quad is the window, so it always covers the view - which is the same guarantee that
            // lets the window be small. Scale is set here; position moves with each anchoring.
            transform.localScale = new Vector3(windowCells * grid.CellSize, windowCells * grid.CellSize, 1f);

            _material.SetTexture("_FogTex", _texture);
            ApplyLook();

            _anchored = false;
            _uploadedVersion = -1;
            _uploadedObservationVersion = -1;
            FollowCamera();
        }

        /// <summary>RG16: discovery in R, observation in G. Named rather than written as a 2 everywhere, since every index below is a multiple of it.</summary>
        public const int BytesPerTexel = 2;

        /// <summary>Offset of the discovery channel within a texel.</summary>
        public const int DiscoveryChannel = 0;

        /// <summary>Offset of the observation channel within a texel.</summary>
        public const int ObservationChannel = 1;

        /// <summary>The four dials that shape the border, pushed to the material. Cheap enough to redo on any edit.</summary>
        void ApplyLook()
        {
            if (_material == null) return;

            _material.SetColor("_FogColor", fogColor);
            _material.SetFloat("_EdgeSoftness", borderSoftness);
            _material.SetFloat("_NoiseScale", noiseScale);
            _material.SetFloat("_NoiseWeight", noiseWeight);
            _material.SetFloat("_RememberedVeil", rememberedVeil);
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
            Initialize(_discovery, _observation, _grid, _camera, _viewHalfHeight);
        }

        /// <summary>
        /// Three reasons to touch the GPU: the window moved, the discovery state did, or the
        /// observers did. Everything else is two comparisons.
        ///
        /// The third one is the frequent one - it fires on every frame a robot is walking - so it
        /// takes the cheaper path and repacks only the observation channel. A still frame with a
        /// still fleet still costs nothing.
        /// </summary>
        void LateUpdate()
        {
            if (_discovery == null) return;

            if (FollowCamera()) return;   // an anchoring re-uploads on its own

            if (_discovery.Version != _uploadedVersion)
            {
                Upload();
                return;
            }

            if (_observation != null && _observation.Version != _uploadedObservationVersion) UploadObservation();
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

        /// <summary>Both channels: the window moved, or something was revealed.</summary>
        void Upload()
        {
            PackTexels(_discovery, _observation, _origin, windowCells, texelsPerCell, _texels);
            Push();
        }

        /// <summary>
        /// The observation channel alone, leaving discovery as it was already packed.
        ///
        /// This is the path taken on most frames a robot is out, and the reason the split exists: the
        /// full pack asks the chunk store for every texel in the window, which is 65 000 dictionary
        /// lookups at the shipped size, while this one is a handful of squared distances per texel.
        /// Discovery cannot have gone stale here - LateUpdate only comes this way when its version
        /// matched what is already uploaded.
        /// </summary>
        void UploadObservation()
        {
            PackObservationChannel(_discovery, _observation, _origin, windowCells, texelsPerCell, _texels);
            Push();
        }

        void Push()
        {
            _texture.SetPixelData(_texels, 0);
            _texture.Apply(updateMipmaps: false);

            _uploadedVersion = _discovery.Version;
            _uploadedObservationVersion = _observation?.Version ?? -1;
        }

        /// <summary>
        /// Fills a texel buffer from the discovery and observation state - R then G per texel -
        /// row-major from the bottom row up, for the window whose lowest-left cell is
        /// <paramref name="origin"/>.
        ///
        /// That order is not a choice: Texture2D's row 0 is the bottom one and v=0 is the bottom of
        /// the UV range, so writing the window's row y into texel row y is what makes the texture line
        /// up with the world instead of arriving mirrored. Public and pure so exactly that can be
        /// asserted without a camera.
        ///
        /// Cells outside the map read as undiscovered, which is what makes the world's edge fog over
        /// with no special case: there is nothing out there to have discovered.
        /// </summary>
        public static void PackTexels(DiscoveryRuntime discovery, ObservationRuntime observation, GridCoord origin,
            int windowCells, int texelsPerCell, byte[] texels)
        {
            if (!MayPack(discovery, windowCells, texelsPerCell, texels, out int side)) return;

            for (int y = 0; y < side; y++)
            {
                int cellY = origin.Y + y / texelsPerCell;
                int row = y * side;

                for (int x = 0; x < side; x++)
                {
                    var cell = new GridCoord(origin.X + x / texelsPerCell, cellY);
                    int texel = (row + x) * BytesPerTexel;

                    bool discovered = discovery.IsDiscovered(cell);
                    texels[texel + DiscoveryChannel] = discovered ? (byte)255 : (byte)0;
                    texels[texel + ObservationChannel] = Observed(discovered, observation, cell);
                }
            }
        }

        /// <summary>
        /// Rewrites only the observation channel, leaving the discovery channel untouched.
        ///
        /// The frequent path, and it does no chunk lookups at all beyond the one that gates each cell
        /// on having been discovered - see <see cref="UploadObservation"/>.
        /// </summary>
        public static void PackObservationChannel(DiscoveryRuntime discovery, ObservationRuntime observation,
            GridCoord origin, int windowCells, int texelsPerCell, byte[] texels)
        {
            if (!MayPack(discovery, windowCells, texelsPerCell, texels, out int side)) return;

            for (int y = 0; y < side; y++)
            {
                int cellY = origin.Y + y / texelsPerCell;
                int row = y * side;

                for (int x = 0; x < side; x++)
                {
                    int texel = (row + x) * BytesPerTexel;

                    // Read back rather than re-asked: whether this cell is discovered is already
                    // sitting in the channel beside it, and re-asking the chunk store is the cost
                    // this path exists to avoid.
                    bool discovered = texels[texel + DiscoveryChannel] != 0;

                    texels[texel + ObservationChannel] =
                        Observed(discovered, observation, new GridCoord(origin.X + x / texelsPerCell, cellY));
                }
            }
        }

        /// <summary>
        /// <b>Discovery gates observation in the data, not only in the shader.</b> A cell nobody has
        /// discovered packs as unobserved whatever stands on it, so the invariant "observed implies
        /// discovered" holds in the texture itself and the shader is never asked a contradictory
        /// question. Null observation means nothing observes, which reads as remembered everywhere.
        /// </summary>
        static byte Observed(bool discovered, ObservationRuntime observation, GridCoord cell)
            => discovered && observation != null && observation.IsObserved(cell) ? (byte)255 : (byte)0;

        static bool MayPack(DiscoveryRuntime discovery, int windowCells, int texelsPerCell, byte[] texels, out int side)
        {
            side = 0;
            if (discovery == null || texels == null || texelsPerCell < 1 || windowCells < 1) return false;

            side = windowCells * texelsPerCell;
            return texels.Length >= side * side * BytesPerTexel;
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
