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

        SpriteRenderer _spriteRenderer;

        // Pooled arrow slots: sized up as needed, extra ones deactivated rather than
        // destroyed/recreated every frame (Show() is called once per Update while a tool is armed).
        //
        // <b>One pool for both kinds.</b> There used to be a single output slot beside this list,
        // which said a building has at most one exit - true of every production building and of
        // nothing in the transport family: a Crossroad has two of each, and could not be previewed
        // at all. Entry and exit differ by a flag here, exactly as they do in the built view
        // (BuildingSpawner.SpawnDirectionalArrow), not by being two mechanisms.
        //
        // Independent transforms (not children of this sprite) so an arrow's own scale never
        // compounds with the ghost sprite's footprint-driven, often non-uniform scale - the same
        // reason BuildingSpawner keeps its arrows siblings of the sprite, not children of it.
        readonly List<Transform> _arrows = new List<Transform>();
        readonly List<SpriteRenderer> _arrowRenderers = new List<SpriteRenderer>();

        void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sortingOrder = SortingBands.PlacementPreview;
        }

        public void Show(Sprite sprite, Vector2 worldSize, Vector3 worldPosition, Direction rotation, bool valid,
            Sprite outputArrowSprite = null, Sprite inputArrowSprite = null,
            IReadOnlyList<(Vector3 position, Direction side, bool inward)> arrows = null, float arrowWorldSize = 0f,
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

            UpdateArrows(outputArrowSprite, inputArrowSprite, arrows, arrowWorldSize);
        }

        void UpdateArrows(Sprite outputSprite, Sprite inputSprite, IReadOnlyList<(Vector3 position, Direction side, bool inward)> arrows, float worldSize)
        {
            int count = arrows != null ? arrows.Count : 0;

            while (_arrows.Count < count)
            {
                var arrowGo = new GameObject("GhostArrow");
                arrowGo.transform.SetParent(transform.parent, false);
                var renderer = arrowGo.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = SortingBands.PlacementPreviewArrow;
                arrowGo.SetActive(false);
                _arrows.Add(arrowGo.transform);
                _arrowRenderers.Add(renderer);
            }

            for (int i = 0; i < _arrows.Count; i++)
            {
                (Vector3 position, Direction side, bool inward) = i < count ? arrows[i] : default;
                Sprite sprite = inward ? inputSprite : outputSprite;

                if (i >= count || sprite == null)
                {
                    _arrows[i].gameObject.SetActive(false);
                    continue;
                }

                _arrows[i].gameObject.SetActive(true);
                _arrowRenderers[i].sprite = sprite;
                _arrows[i].position = position;
                // `side` points away from the building on both paths - it is the exit side for an
                // output and the side a delivery comes from for an input - so an entry arrow is the
                // one that turns around to point at the building. Same rule as the built view's.
                Direction pointing = inward ? side.Opposite() : side;
                _arrows[i].rotation = Quaternion.Euler(0f, 0f, -pointing.ToRotationDegrees());
                _arrows[i].localScale = Vector3.one * worldSize;
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            foreach (Transform arrow in _arrows) arrow.gameObject.SetActive(false);
        }
    }
}
