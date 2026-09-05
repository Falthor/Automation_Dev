using System.Collections.Generic;
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

        // Independent transforms (not children of this sprite) so an arrow's own scale never
        // compounds with the ghost sprite's footprint-driven, often non-uniform scale - the same
        // reason BuildingSpawner keeps its arrows siblings of the sprite, not children of it.
        Transform _outputArrow;
        SpriteRenderer _outputArrowRenderer;

        // Pooled entry-arrow slots: sized up as needed, extra ones deactivated rather than
        // destroyed/recreated every frame (Show() is called once per Update while a tool is armed).
        readonly List<Transform> _inputArrows = new List<Transform>();
        readonly List<SpriteRenderer> _inputArrowRenderers = new List<SpriteRenderer>();

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
            Sprite outputArrowSprite = null, Vector3? outputArrowWorldPosition = null, float outputArrowWorldSize = 0f,
            Sprite inputArrowSprite = null, IReadOnlyList<(Vector3 position, Direction direction)> inputArrows = null,
            bool rotateSprite = false, Direction artNativeDirection = default)
        {
            gameObject.SetActive(true);
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();

            _spriteRenderer.sprite = sprite;
            _spriteRenderer.color = valid ? ValidTint : InvalidTint;

            // Uniform, never per axis - the fourth and last path to ask BuildingSpawner rather than
            // do its own arithmetic. A per-axis stretch is a no-op on square art and squashes a
            // sprite deliberately drawn taller than its footprint (the Core, the Foundry) into a
            // square, which is exactly the height it was drawn to convey.
            BuildingSpawner.FitSpriteUniform(_spriteRenderer, sprite, worldSize);
            transform.position = worldPosition;

            // Most buildings never rotate their sprite - rotating only moves input/output arrows
            // (matches BuildingSpawner.SpawnStandardView). The "+"-shaped Splitter/Crossroad are the
            // exception: their real view DOES rotate the sprite (SpawnRotatingCrossView), so the
            // ghost must mirror that exact formula or it previews a different facing than what
            // gets built.
            if (rotateSprite)
            {
                int rotationDegrees = rotation.ToRotationDegrees() - artNativeDirection.ToRotationDegrees();
                transform.rotation = Quaternion.Euler(0f, 0f, -rotationDegrees);
            }
            else
            {
                transform.rotation = Quaternion.identity;
            }

            if (outputArrowSprite != null && outputArrowWorldPosition.HasValue)
            {
                _outputArrow.gameObject.SetActive(true);
                _outputArrowRenderer.sprite = outputArrowSprite;
                _outputArrow.position = outputArrowWorldPosition.Value;
                _outputArrow.rotation = Quaternion.Euler(0f, 0f, -rotation.ToRotationDegrees());
                _outputArrow.localScale = Vector3.one * outputArrowWorldSize;
            }
            else
            {
                _outputArrow.gameObject.SetActive(false);
            }

            UpdateInputArrows(inputArrowSprite, inputArrows, outputArrowWorldSize);
        }

        void UpdateInputArrows(Sprite sprite, IReadOnlyList<(Vector3 position, Direction direction)> arrows, float worldSize)
        {
            int count = sprite != null && arrows != null ? arrows.Count : 0;

            while (_inputArrows.Count < count)
            {
                var arrowGo = new GameObject("GhostInputArrow");
                arrowGo.transform.SetParent(transform.parent, false);
                var renderer = arrowGo.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = ArrowSortingOrder;
                arrowGo.SetActive(false);
                _inputArrows.Add(arrowGo.transform);
                _inputArrowRenderers.Add(renderer);
            }

            for (int i = 0; i < _inputArrows.Count; i++)
            {
                if (i >= count)
                {
                    _inputArrows[i].gameObject.SetActive(false);
                    continue;
                }

                (Vector3 position, Direction direction) = arrows[i];
                _inputArrows[i].gameObject.SetActive(true);
                _inputArrowRenderers[i].sprite = sprite;
                _inputArrows[i].position = position;
                // Entry arrows point inward (toward the building), the opposite of their own side.
                _inputArrows[i].rotation = Quaternion.Euler(0f, 0f, -direction.Opposite().ToRotationDegrees());
                _inputArrows[i].localScale = Vector3.one * worldSize;
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            if (_outputArrow != null) _outputArrow.gameObject.SetActive(false);
            foreach (Transform arrow in _inputArrows) arrow.gameObject.SetActive(false);
        }
    }
}
