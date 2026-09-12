using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Purely visual boundary overlay covering the grid extent - no gameplay data. It carries two
    /// trames in one material: the cells, and the chunks everything region-shaped aligns on
    /// (MAP.md §1).
    ///
    /// Hidden by default, and the two trames are shown for different reasons: the cell grid is a
    /// placement aid that appears on its own while a building is armed, while both together are what
    /// the grid shortcut puts up. Both are driven from GameRuntime.Update, which owns this view's
    /// reference and lifecycle.
    /// </summary>
    public sealed class GridLineView : MonoBehaviour
    {

        /// <summary>Custom/GridLinesOverlay. An asset reference, not a Shader.Find by name - see ActionRadiusView for why, and docs/BUILD.md.</summary>
        [SerializeField] Shader overlayShader;

        [SerializeField] Color lineColor = new Color(0f, 0f, 0f, 0.35f);
        [SerializeField, Min(0.001f)] float lineThickness = 0.03f;

        /// <summary>
        /// Heavier and more contrasted than a cell boundary on purpose: at one line every 64 cells,
        /// a chunk edge drawn like a cell edge is indistinguishable from the trame it sits on.
        /// Defaulted here rather than wired in the scene, so no scene has to be touched to gain them.
        /// </summary>
        [SerializeField] Color chunkLineColor = new Color(0.1f, 0.5f, 0.9f, 0.75f);
        [SerializeField, Min(0.001f)] float chunkLineThickness = 0.12f;

        SpriteRenderer _renderer;
        Material _material;

        public void Initialize(GridRuntime grid, int cellsWide, int chunkSizeCells)
        {
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sprite = CreateUnitSprite();
                _renderer.sortingOrder = SortingBands.GridLines;
                _material = new Material(overlayShader) { name = "GridLines (Instance)" };
                _renderer.sharedMaterial = _material;
                _renderer.enabled = false;
            }

            float worldSize = cellsWide * grid.CellSize;
            transform.localScale = new Vector3(worldSize, worldSize, 1f);
            transform.position = grid.CellToWorld(new Game.Core.GridCoord(0, 0));

            _material.SetFloat("_CellSize", grid.CellSize);
            _material.SetFloat("_LineThickness", lineThickness);
            _material.SetColor("_LineColor", lineColor);

            // The chunk trame is expressed in world units, like the cell one: the shader knows about
            // spacings, not about cells.
            _material.SetFloat("_ChunkSize", Mathf.Max(1, chunkSizeCells) * grid.CellSize);
            _material.SetFloat("_ChunkLineThickness", chunkLineThickness);
            _material.SetColor("_ChunkLineColor", chunkLineColor);
        }

        /// <summary>Shows/hides the overlay. Safe to call every frame and before Initialize.</summary>
        public void SetVisible(bool visible)
        {
            if (_renderer != null) _renderer.enabled = visible;
        }

        /// <summary>
        /// Whether the chunk trame is drawn on top of the cell one. Separate from
        /// <see cref="SetVisible"/> because the placement aid wants the cells alone: chunk boundaries
        /// answer no question about where a building goes, and at this weight they would only compete
        /// with the footprint being positioned.
        /// </summary>
        public void SetChunkLinesVisible(bool visible)
        {
            if (_material != null) _material.SetFloat("_ChunkOpacity", visible ? 1f : 0f);
        }

        static Sprite CreateUnitSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0f, 0f), 1f);
        }
    }
}
