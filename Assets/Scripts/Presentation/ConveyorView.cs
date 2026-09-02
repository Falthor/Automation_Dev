using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>Presentation of a placed conveyor: reflects ConveyorRuntime.Orientation as a sprite.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ConveyorView : MonoBehaviour
    {
        SpriteRenderer _spriteRenderer;

        // Terrain layers use sortingOrder 0 (Base) and 1 (Top) - buildings must render above both.
        const int SortingOrder = 10;

        // The straight belt art has a thin bezel baked into its edges, leaving a visible sliver
        // of terrain between adjacent placed conveyors when sized to exactly 1 cell. Stretching
        // slightly along the belt's own length axis closes that gap without affecting its width.
        const float LengthStretchFactor = 1.08f;

        void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = SortingOrder;
        }

        public void Sync(ConveyorRuntime runtime, ProceduralSpriteFactory spriteFactory, ConveyorDefinition definition, float cellSize)
        {
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            ConveyorOrientation orientation = runtime.Orientation;
            Direction artNativeDirection = Direction.North;

            // The override art only matches the definition's own default shape - a conveyor
            // reshaped away from it (e.g. straight -> corner via a drag turn) falls back to
            // the procedural placeholder for whatever shape it now actually is.
            if (definition.OverrideSprite != null && orientation.Shape == definition.DefaultShape)
            {
                _spriteRenderer.sprite = definition.OverrideSprite;
                artNativeDirection = definition.ArtNativeDirection;
            }
            else
            {
                _spriteRenderer.sprite = spriteFactory.CreateShapeSprite(orientation.Shape, definition.PlaceholderColor);
            }

            _spriteRenderer.color = Color.white;

            Vector2 desiredWorldSize = new Vector2(cellSize, cellSize) * definition.FootprintSize;
            BuildingSpawner.SetSpriteToWorldSize(_spriteRenderer, _spriteRenderer.sprite, desiredWorldSize);

            if (definition.OverrideSprite != null && orientation.Shape == definition.DefaultShape)
            {
                Vector3 stretched = transform.localScale;
                stretched.x *= LengthStretchFactor;
                transform.localScale = stretched;
            }

            int rotationDegrees = orientation.Rotation.ToRotationDegrees() - artNativeDirection.ToRotationDegrees();
            transform.rotation = Quaternion.Euler(0f, 0f, -rotationDegrees);

            Vector3 scale = transform.localScale;
            float absX = Mathf.Abs(scale.x);
            scale.x = orientation.Mirrored ? -absX : absX;
            transform.localScale = scale;
        }
    }
}
