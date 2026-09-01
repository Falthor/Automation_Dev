using Game.Core;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Generic construction ghost for non-conveyor buildings (Extractor, Storage, ...): a
    /// footprint-sized, tinted copy of the building's own sprite following the hovered cell.
    /// ConveyorGhostView stays separate - conveyors need shape/orientation logic this doesn't.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BuildingGhostView : MonoBehaviour
    {
        static readonly Color ValidTint = new Color(0.3f, 1f, 0.3f, 0.55f);
        static readonly Color InvalidTint = new Color(1f, 0.3f, 0.3f, 0.55f);
        const int SortingOrder = 11;
        const int ArrowSortingOrder = 12;

        SpriteRenderer _spriteRenderer;

        // Independent transform (not a child of this sprite) so the arrow's own scale never
        // compounds with the ghost sprite's footprint-driven, often non-uniform scale - the same
        // reason BuildingSpawner keeps its OutputArrow a sibling of the sprite, not a child of it.
        Transform _outputArrow;
        SpriteRenderer _outputArrowRenderer;

        void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = SortingOrder;

            var arrowGo = new GameObject("GhostOutputArrow");
            arrowGo.transform.SetParent(transform.parent, false);
            _outputArrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
            _outputArrowRenderer.sortingOrder = ArrowSortingOrder;
            _outputArrow = arrowGo.transform;
            arrowGo.SetActive(false);
        }

        public void Show(Sprite sprite, Vector2 worldSize, Vector3 worldPosition, Direction rotation, bool valid,
            Sprite outputArrowSprite = null, Vector3? outputArrowWorldPosition = null, float outputArrowWorldSize = 0f)
        {
            gameObject.SetActive(true);
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();

            _spriteRenderer.sprite = sprite;
            _spriteRenderer.color = valid ? ValidTint : InvalidTint;

            Vector2 nativeSize = sprite.bounds.size;
            transform.localScale = new Vector3(worldSize.x / nativeSize.x, worldSize.y / nativeSize.y, 1f);
            transform.position = worldPosition;
            transform.rotation = Quaternion.Euler(0f, 0f, -rotation.ToRotationDegrees());

            if (outputArrowSprite != null && outputArrowWorldPosition.HasValue)
            {
                _outputArrow.gameObject.SetActive(true);
                _outputArrowRenderer.sprite = outputArrowSprite;
                _outputArrow.position = outputArrowWorldPosition.Value;
                _outputArrow.rotation = transform.rotation;
                _outputArrow.localScale = Vector3.one * outputArrowWorldSize;
            }
            else
            {
                _outputArrow.gameObject.SetActive(false);
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (_outputArrow != null) _outputArrow.gameObject.SetActive(false);
        }
    }
}
