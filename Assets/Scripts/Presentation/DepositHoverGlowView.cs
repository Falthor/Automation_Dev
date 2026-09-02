using Game.Core;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// A soft glowing halo shown around whichever ore deposit currently sits under the mouse.
    /// Deliberately distinct from BuildingHoverHighlightView's thin rectangle outline (buildings)
    /// - a deposit is world content the player interacts with by placing an extractor on it, not
    /// a placed building, so it gets its own more eye-catching hover treatment.
    /// </summary>
    public sealed class DepositHoverGlowView : MonoBehaviour
    {
        const int SortingOrder = 8; // below the deposit sprite (9) - reads as a halo behind/around it.

        [SerializeField] Color glowColor = new Color(1f, 0.85f, 0.3f, 0.75f);
        [SerializeField, Min(1f)] float paddingFactor = 1.5f;

        SpriteRenderer _renderer;
        GridRuntime _grid;

        public void Initialize(GridRuntime grid, ProceduralSpriteFactory spriteFactory)
        {
            _grid = grid;

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = spriteFactory.CreateRadialGlowSprite(glowColor);
            _renderer.sortingOrder = SortingOrder;

            gameObject.SetActive(false);
        }

        public void Show(GridCoord footprintOrigin, Vector2Int footprintSize)
        {
            gameObject.SetActive(true);

            Vector3 min = _grid.CellToWorld(footprintOrigin);
            float width = footprintSize.x * _grid.CellSize * paddingFactor;
            float height = footprintSize.y * _grid.CellSize * paddingFactor;

            transform.position = new Vector3(
                min.x + footprintSize.x * _grid.CellSize * 0.5f,
                min.y + footprintSize.y * _grid.CellSize * 0.5f,
                0f);

            Vector2 nativeSize = _renderer.sprite.bounds.size;
            transform.localScale = new Vector3(width / nativeSize.x, height / nativeSize.y, 1f);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
