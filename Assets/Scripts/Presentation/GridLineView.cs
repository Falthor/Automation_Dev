using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Purely visual cell-boundary overlay covering the grid extent - no gameplay data. Hidden
    /// by default: the grid only reads as useful help while a building is actually being placed,
    /// so its visibility is driven from construction state (see GameRuntime.Update).
    /// </summary>
    public sealed class GridLineView : MonoBehaviour
    {
        const int SortingOrder = 5; // above terrain (0/1), below buildings (10/11).

        /// <summary>Custom/GridLinesOverlay. An asset reference, not a Shader.Find by name - see ActionRadiusView for why, and docs/BUILD.md.</summary>
        [SerializeField] Shader overlayShader;

        [SerializeField] Color lineColor = new Color(0f, 0f, 0f, 0.35f);
        [SerializeField, Min(0.001f)] float lineThickness = 0.03f;

        SpriteRenderer _renderer;
        Material _material;

        public void Initialize(GridRuntime grid, int cellsWide)
        {
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sprite = CreateUnitSprite();
                _renderer.sortingOrder = SortingOrder;
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
        }

        /// <summary>Shows/hides the overlay. Safe to call every frame and before Initialize.</summary>
        public void SetVisible(bool visible)
        {
            if (_renderer != null) _renderer.enabled = visible;
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
