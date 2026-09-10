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
            renderer.sortingOrder = SortingBands.HoverOutline;
            renderer.color = lineColor;
            return renderer;
        }

        public void Show(GridCoord footprintOrigin, Vector2Int footprintSize)
        {
            Vector3 min = _grid.CellToWorld(footprintOrigin);
            ShowRect(min.x, min.y, footprintSize.x * _grid.CellSize, footprintSize.y * _grid.CellSize);
        }

        /// <summary>
        /// A halo around something that is not a grid occupant: an explorer robot, which stands
        /// <b>between</b> cells because its position is continuous.
        ///
        /// Snapping it to the cell underneath would be simpler and visibly wrong - the outline would
        /// jump a whole cell at a time while the robot slid smoothly inside it. Given a centre and a
        /// size in world units, it follows exactly.
        /// </summary>
        public void ShowAt(Vector2 centreWorld, float sizeWorld)
        {
            ShowRect(centreWorld.x - sizeWorld * 0.5f, centreWorld.y - sizeWorld * 0.5f, sizeWorld, sizeWorld);
        }

        void ShowRect(float minX, float minY, float width, float height)
        {
            gameObject.SetActive(true);

            PlaceBar(_bottom, minX + width * 0.5f, minY + lineThickness * 0.5f, width, lineThickness);
            PlaceBar(_top, minX + width * 0.5f, minY + height - lineThickness * 0.5f, width, lineThickness);
            PlaceBar(_left, minX + lineThickness * 0.5f, minY + height * 0.5f, lineThickness, height);
            PlaceBar(_right, minX + width - lineThickness * 0.5f, minY + height * 0.5f, lineThickness, height);
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
