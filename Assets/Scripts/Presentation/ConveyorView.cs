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

            // Both shapes are fit the same way: a single uniform scale factor (preserving the
            // sprite's own aspect ratio) rather than independently stretching X and Y to force
            // the cell size - that per-axis stretch is what made a cropped straight belt (whose
            // art doesn't natively fill a square frame) look a different thickness than the
            // uncropped, already-square corner belt. Using the larger of the two required
            // ratios means the sprite is guaranteed to fully cover the cell on its shorter native
            // axis, at the cost of some natural overflow on the other axis - which is exactly the
            // seam-closing overlap RenderOverscan already provides for corners, so both shapes
            // now get their overlap from the same single mechanism.
            SetSpriteToWorldSizeUniform(_spriteRenderer, _spriteRenderer.sprite, desiredWorldSize);

            if (definition.OverrideSprite != null && orientation.Shape == definition.DefaultShape)
            {
                if (definition.RenderOverscan != 1f)
                {
                    transform.localScale *= definition.RenderOverscan;
                }

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

        static void SetSpriteToWorldSizeUniform(SpriteRenderer renderer, Sprite sprite, Vector2 desiredWorldSize)
        {
            renderer.sprite = sprite;
            Vector2 nativeSize = sprite.bounds.size;
            float scale = Mathf.Max(desiredWorldSize.x / nativeSize.x, desiredWorldSize.y / nativeSize.y);
            Vector3 localScale = renderer.transform.localScale;
            localScale.x = scale;
            localScale.y = scale;
            renderer.transform.localScale = localScale;
        }

        void RemoveFlipbookIfPresent()
        {
            var flipbook = GetComponent<SpriteFlipbook>();
            if (flipbook != null) Destroy(flipbook);
        }
    }
}
