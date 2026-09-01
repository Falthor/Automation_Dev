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

        SpriteRenderer _spriteRenderer;

        void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = SortingOrder;
        }

        public void Show(Sprite sprite, Vector2 worldSize, Vector3 worldPosition, Direction rotation, bool valid)
        {
            gameObject.SetActive(true);
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();

            _spriteRenderer.sprite = sprite;
            _spriteRenderer.color = valid ? ValidTint : InvalidTint;

            Vector2 nativeSize = sprite.bounds.size;
            transform.localScale = new Vector3(worldSize.x / nativeSize.x, worldSize.y / nativeSize.y, 1f);
            transform.position = worldPosition;
            transform.rotation = Quaternion.Euler(0f, 0f, -rotation.ToRotationDegrees());
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
