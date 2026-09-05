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

        // RenderOverscan makes adjacent conveyors slightly overlap at the seam on purpose
        // (closing the belt art's own bezel gap). With every conveyor sharing the same
        // sortingOrder, that overlap's draw order was resolved by instantiation order -
        // unstable across placements, so which sprite won at a given seam could flip depending on
        // build order. Alternating the order by cell parity (see Sync) instead makes every seam's
        // winner fixed and predictable, regardless of placement order.
        const int SortingOrderParityOffset = 1;

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

            // Adjacent cells always differ in (X+Y) parity, so this guarantees two neighboring
            // conveyors never share a sortingOrder - see SortingOrderParityOffset above.
            _spriteRenderer.sortingOrder = SortingOrder + ((runtime.Cell.X + runtime.Cell.Y) & 1) * SortingOrderParityOffset;

            ConveyorOrientation orientation = runtime.Orientation;
            Direction artNativeDirection = Direction.North;

            // The override art only matches the definition's own default shape - a conveyor
            // reshaped away from it (e.g. straight -> corner via a drag turn) falls back to
            // the procedural placeholder for whatever shape it now actually is.
            bool ownArt = BuildingSpawner.UsesOwnConveyorArt(definition, orientation.Shape);

            if (ownArt)
            {
                _spriteRenderer.sprite = definition.OverrideSprite;
                artNativeDirection = definition.ArtNativeDirection;
            }
            else
            {
                _spriteRenderer.sprite = spriteFactory.CreateShapeSprite(orientation.Shape, definition.PlaceholderColor);
            }

            _spriteRenderer.color = Color.white;

            // Both shapes are fit the same way: a single uniform scale factor preserving the
            // sprite's aspect ratio, never a per-axis stretch - see BuildingSpawner.FitSpriteUniform
            // for why. The overflow that fit produces on the longer axis is exactly the seam-closing
            // overlap RenderOverscan gives corners, so both shapes get their overlap from one
            // mechanism. The size itself comes from BuildingSpawner.ArtWorldSize, which every view
            // that must line up with a built conveyor - the placement ghost, the construction
            // silhouette - reads too.
            BuildingSpawner.FitSpriteUniform(_spriteRenderer, _spriteRenderer.sprite,
                BuildingSpawner.ArtWorldSize(definition, cellSize, overscanned: ownArt));

            if (ownArt)
            {
                if (definition.AnimationFrames != null && definition.AnimationFrames.Length >= 2)
                {
                    var flipbook = GetComponent<SpriteFlipbook>();
                    if (flipbook == null) flipbook = gameObject.AddComponent<SpriteFlipbook>();
                    flipbook.Initialize(definition.AnimationFrames, definition.AnimationFps);
                }
                else
                {
                    RemoveFlipbookIfPresent();
                }
            }
            else
            {
                // Reshaped away from the animated default shape (e.g. straight -> corner via a
                // drag turn) - the flipbook would otherwise keep overwriting _spriteRenderer.sprite
                // every frame with the old shape's animation.
                RemoveFlipbookIfPresent();
            }

            // Mirroring flips the sprite across its own vertical (world-X) axis before rotation
            // is applied. Since the corner art's native entry side sits on that same East/West
            // axis, the flip swaps which native pose the rotation is measured against - using
            // the calibration's opposite direction reproduces the mirrored entry/exit table
            // exactly (verified against every (rotation, mirrored) combination for the corner
            // art). Straight never mirrors, so this is a no-op for it.
            Direction effectiveArtNativeDirection = orientation.Mirrored ? artNativeDirection.Opposite() : artNativeDirection;
            int rotationDegrees = orientation.Rotation.ToRotationDegrees() - effectiveArtNativeDirection.ToRotationDegrees();
            transform.rotation = Quaternion.Euler(0f, 0f, -rotationDegrees);

            Vector3 scale = transform.localScale;
            float absX = Mathf.Abs(scale.x);
            scale.x = orientation.Mirrored ? -absX : absX;
            transform.localScale = scale;
        }


        void RemoveFlipbookIfPresent()
        {
            var flipbook = GetComponent<SpriteFlipbook>();
            if (flipbook != null) Destroy(flipbook);
        }
    }
}
