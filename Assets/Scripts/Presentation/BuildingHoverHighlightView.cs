using Game.Core;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Outlines the grid cells occupied by whichever building is currently under the mouse, as a
    /// single rectangle around the whole footprint (not a per-cell grid) - four thin solid-color
    /// bars forming a frame, so thickness stays constant in world units regardless of footprint size.
    /// </summary>
    public sealed class BuildingHoverHighlightView : MonoBehaviour
    {
        const int SortingOrder = 13; // above buildings (9/10/11) and the ghost/output arrow (11/12).

        [SerializeField] Color lineColor = new Color(0.3f, 0.6f, 1f, 0.95f);
        [SerializeField, Min(0.001f)] float lineThickness = 0.08f;

        GridRuntime _grid;
        SpriteRenderer _top;
        SpriteRenderer _bottom;
        SpriteRenderer _left;
        SpriteRenderer _right;

        public void Initialize(GridRuntime grid)
        {
            _grid = grid;

            _top = CreateBar("Top");
            _bottom = CreateBar("Bottom");
            _left = CreateBar("Left");
            _right = CreateBar("Right");

            gameObject.SetActive(false);
        }

        SpriteRenderer CreateBar(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateUnitSprite();
            renderer.sortingOrder = SortingOrder;
            renderer.color = lineColor;
            return renderer;
        }

        public void Show(GridCoord footprintOrigin, Vector2Int footprintSize)
        {
            gameObject.SetActive(true);

            Vector3 min = _grid.CellToWorld(footprintOrigin);
            float width = footprintSize.x * _grid.CellSize;
            float height = footprintSize.y * _grid.CellSize;

            PlaceBar(_bottom, min.x + width * 0.5f, min.y + lineThickness * 0.5f, width, lineThickness);
            PlaceBar(_top, min.x + width * 0.5f, min.y + height - lineThickness * 0.5f, width, lineThickness);
            PlaceBar(_left, min.x + lineThickness * 0.5f, min.y + height * 0.5f, lineThickness, height);
            PlaceBar(_right, min.x + width - lineThickness * 0.5f, min.y + height * 0.5f, lineThickness, height);
        }

        static void PlaceBar(SpriteRenderer bar, float x, float y, float width, float height)
        {
            bar.transform.position = new Vector3(x, y, 0f);
            bar.transform.localScale = new Vector3(width, height, 1f);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
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
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
