using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>Follows the mouse-derived cell and tints green/red based on placement validity.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ConveyorGhostView : MonoBehaviour
    {
        static readonly Color ValidTint = new Color(0.3f, 1f, 0.3f, 0.55f);
        static readonly Color InvalidTint = new Color(1f, 0.3f, 0.3f, 0.55f);

        SpriteRenderer _spriteRenderer;

        // Terrain layers use sortingOrder 0 (Base) and 1 (Top); ConveyorView uses 10 - the
        // ghost must render above both (>=10, and above real buildings so it's never obscured).
        const int SortingOrder = 11;

        void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = SortingOrder;
        }

        public void Show(ProceduralSpriteFactory spriteFactory, ConveyorDefinition definition, Direction rotation, Vector3 worldPosition, bool valid)
        {
            gameObject.SetActive(true);

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            Direction artNativeDirection = Direction.North;
            if (definition.OverrideSprite != null)
            {
                _spriteRenderer.sprite = definition.OverrideSprite;
                artNativeDirection = definition.ArtNativeDirection;
            }
            else
            {
                _spriteRenderer.sprite = spriteFactory.CreateShapeSprite(definition.DefaultShape, definition.PlaceholderColor);
            }

            _spriteRenderer.color = valid ? ValidTint : InvalidTint;

            transform.position = worldPosition;
            int rotationDegrees = rotation.ToRotationDegrees() - artNativeDirection.ToRotationDegrees();
            transform.rotation = Quaternion.Euler(0f, 0f, -rotationDegrees);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
