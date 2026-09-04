using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws every pending construction site as a blue ghost of the building it will become
    /// (TASK_05_ROBOT_CONSTRUCTEUR.md §3: valid preview stays green, invalid red, a chantier
    /// waiting or in progress is blue). Deliberately the same visual vocabulary as
    /// BuildingGhostView's placement preview - same sprite resolution, same tint-the-real-art
    /// approach - so the player reads one continuous language: green "I am about to place this",
    /// blue "it is placed but not built yet", then the real building.
    ///
    /// Purely a view over ConstructionSiteSystem's runtime state, on the same
    /// pooled-view/LateUpdate model as ItemVisualSync - it owns no gameplay state and decides
    /// nothing. A segment's real view is spawned by BuildingSpawner only once the site
    /// materializes it, so the two never overlap.
    /// </summary>
    public sealed class ConstructionSiteVisualSync : MonoBehaviour
    {
        const int SortingOrder = 10;

        [SerializeField] GameRuntime gameRuntime;

        /// <summary>Multiplied over the building's own art, so what shows is a blue-shadowed silhouette of the real thing rather than a flat rectangle.</summary>
        [SerializeField] Color siteTint = new Color(0.35f, 0.6f, 1f, 0.6f);

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly Dictionary<BuildingRuntime, GameObject> _views = new Dictionary<BuildingRuntime, GameObject>();
        readonly HashSet<BuildingRuntime> _liveKeys = new HashSet<BuildingRuntime>();

        void LateUpdate()
        {
            if (gameRuntime == null || gameRuntime.ConstructionSites == null || gameRuntime.Grid == null) return;

            _liveKeys.Clear();

            foreach (ConstructionSiteRuntime site in gameRuntime.ConstructionSites.Sites)
            {
                for (int i = site.MaterializedCount; i < site.Segments.Count; i++)
                {
                    BuildingRuntime segment = site.Segments[i];
                    _liveKeys.Add(segment);
                    SyncView(segment);
                }
            }

            RemoveStaleViews();
        }

        void SyncView(BuildingRuntime segment)
        {
            if (!_views.TryGetValue(segment, out GameObject view) || view == null)
            {
                view = new GameObject($"ConstructionSite {segment.Cell}");
                var created = view.AddComponent<SpriteRenderer>();
                created.sortingOrder = SortingOrder;
                _views[segment] = view;
            }

            var renderer = view.GetComponent<SpriteRenderer>();
            BuildingDefinition definition = segment.Definition;

            renderer.sprite = ResolveSprite(segment, definition);
            renderer.color = siteTint;

            view.transform.position = segment is ConveyorRuntime
                ? gameRuntime.Grid.CellCenterToWorld(segment.Cell)
                : gameRuntime.Grid.FootprintCenterToWorld(segment.Cell, definition.FootprintSize);

            ScaleToFootprint(view.transform, renderer.sprite, definition);
            ApplyRotation(view.transform, segment, definition);
        }

        /// <summary>
        /// Same resolution the real views use: a conveyor shows its own art only while its
        /// current shape still matches the definition it was placed from (ConveyorView's rule),
        /// otherwise the procedural shape sprite - which always matches the actual straight/corner
        /// shape, so a drag-turned segment still reads as a corner while pending. Any other
        /// building shows its art, or its procedural placeholder colour when it has none
        /// (BuildingGhostView's own fallback).
        /// </summary>
        Sprite ResolveSprite(BuildingRuntime segment, BuildingDefinition definition)
        {
            if (segment is ConveyorRuntime conveyor && definition is ConveyorDefinition conveyorDefinition)
            {
                bool artMatchesShape = conveyorDefinition.OverrideSprite != null
                    && conveyor.Orientation.Shape == conveyorDefinition.DefaultShape;
                return artMatchesShape
                    ? conveyorDefinition.OverrideSprite
                    : _spriteFactory.CreateShapeSprite(conveyor.Orientation.Shape, definition.PlaceholderColor);
            }

            return definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
        }

        /// <summary>Uniform fit (preserving the art's aspect ratio), matching ConveyorView.SetSpriteToWorldSizeUniform - a per-axis stretch is what used to make belts look a different thickness than their neighbours.</summary>
        void ScaleToFootprint(Transform target, Sprite sprite, BuildingDefinition definition)
        {
            float cellSize = gameRuntime.Grid.CellSize;
            Vector2 desiredWorldSize = new Vector2(cellSize, cellSize) * definition.FootprintSize;
            Vector2 nativeSize = sprite.bounds.size;
            float scale = Mathf.Max(desiredWorldSize.x / nativeSize.x, desiredWorldSize.y / nativeSize.y);
            target.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// A conveyor ghost turns with its own orientation (mirroring included, exactly as
        /// ConveyorView does - a corner's chirality is otherwise wrong half the time); a
        /// Splitter/Crossroad turns with its facing, measured against its art's native side; every
        /// other building's view never rotates, so its ghost must not either.
        /// </summary>
        void ApplyRotation(Transform target, BuildingRuntime segment, BuildingDefinition definition)
        {
            if (segment is ConveyorRuntime conveyor)
            {
                ConveyorOrientation orientation = conveyor.Orientation;
                Direction artNativeDirection = definition is ConveyorDefinition conveyorDefinition
                    && conveyorDefinition.OverrideSprite != null
                    && orientation.Shape == conveyorDefinition.DefaultShape
                        ? conveyorDefinition.ArtNativeDirection
                        : Direction.North;

                Direction effectiveNative = orientation.Mirrored ? artNativeDirection.Opposite() : artNativeDirection;
                int conveyorDegrees = orientation.Rotation.ToRotationDegrees() - effectiveNative.ToRotationDegrees();
                target.rotation = Quaternion.Euler(0f, 0f, -conveyorDegrees);

                Vector3 scale = target.localScale;
                scale.x = orientation.Mirrored ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
                target.localScale = scale;
                return;
            }

            Direction artNative;
            if (definition is SplitterDefinition splitter) artNative = splitter.ArtNativeEntrySide;
            else if (definition is CrossroadDefinition) artNative = Direction.North;
            else
            {
                target.rotation = Quaternion.identity;
                return;
            }

            int degrees = segment.FacingRotation.ToRotationDegrees() - artNative.ToRotationDegrees();
            target.rotation = Quaternion.Euler(0f, 0f, -degrees);
        }

        void RemoveStaleViews()
        {
            List<BuildingRuntime> stale = null;
            foreach (var kvp in _views)
            {
                if (_liveKeys.Contains(kvp.Key)) continue;
                (stale ??= new List<BuildingRuntime>()).Add(kvp.Key);
            }

            if (stale == null) return;
            foreach (BuildingRuntime key in stale)
            {
                if (_views[key] != null) Destroy(_views[key]);
                _views.Remove(key);
            }
        }
    }
}
