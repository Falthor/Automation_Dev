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

        /// <summary>
        /// The ghost previews the belt that will be built, so it sizes itself exactly as
        /// ConveyorView will: BuildingSpawner.ArtWorldSize, uniform fit, RenderOverscan under the
        /// same condition. It used to rely on whatever scale its GameObject was authored with,
        /// which happened to be close enough only while every conveyor's overscan stayed near 1.
        /// </summary>
        public void Show(ProceduralSpriteFactory spriteFactory, ConveyorDefinition definition, Direction rotation, Vector3 worldPosition, bool valid, float cellSize)
        {
            gameObject.SetActive(true);

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
            }

            // The ghost always previews the tool's own default shape, so a definition carrying
            // override art always wears it here - unlike a placed belt, which can have been
            // reshaped away from that shape and fall back to the placeholder.
            bool ownArt = BuildingSpawner.UsesOwnConveyorArt(definition, definition.DefaultShape);

            Direction artNativeDirection = Direction.North;
            Sprite sprite;
            if (ownArt)
            {
                sprite = definition.OverrideSprite;
                artNativeDirection = definition.ArtNativeDirection;
            }
            else
            {
                sprite = spriteFactory.CreateShapeSprite(definition.DefaultShape, definition.PlaceholderColor);
            }

            BuildingSpawner.FitSpriteUniform(_spriteRenderer, sprite,
                BuildingSpawner.ArtWorldSize(definition, cellSize, overscanned: ownArt));

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
