using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>Purely visual cell-boundary overlay covering the grid extent - no gameplay data.</summary>
    public sealed class GridLineView : MonoBehaviour
    {
        const string ShaderName = "Custom/GridLinesOverlay";
        const int SortingOrder = 5; // above terrain (0/1), below buildings (10/11).

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
                _material = new Material(Shader.Find(ShaderName)) { name = "GridLines (Instance)" };
                _renderer.sharedMaterial = _material;
            }

            float worldSize = cellsWide * grid.CellSize;
            transform.localScale = new Vector3(worldSize, worldSize, 1f);
            transform.position = grid.CellToWorld(new Game.Core.GridCoord(0, 0));

            _material.SetFloat("_CellSize", grid.CellSize);
            _material.SetFloat("_LineThickness", lineThickness);
            _material.SetColor("_LineColor", lineColor);
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
