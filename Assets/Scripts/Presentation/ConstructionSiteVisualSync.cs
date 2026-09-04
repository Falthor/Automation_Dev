using System.Collections.Generic;
using Game.Core;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws every pending construction site in blue (TASK_05_ROBOT_CONSTRUCTEUR.md §3: valid
    /// preview stays green, invalid red, a chantier waiting or in progress is blue). Purely a view
    /// over ConstructionSiteSystem's runtime state, on the same pooled-view/LateUpdate model as
    /// ItemVisualSync - it owns no gameplay state and never decides anything. A segment's real view
    /// is spawned by BuildingSpawner only once the site materializes it, so the two never overlap.
    /// </summary>
    public sealed class ConstructionSiteVisualSync : MonoBehaviour
    {
        const int SortingOrder = 10;

        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Color siteTint = new Color(0.25f, 0.55f, 1f, 0.55f);

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
                var renderer = view.AddComponent<SpriteRenderer>();
                renderer.sprite = _spriteFactory.CreateSolidSquareSprite(Color.white);
                renderer.color = siteTint;
                renderer.sortingOrder = SortingOrder;
                _views[segment] = view;
            }

            Vector2Int footprint = segment.Definition.FootprintSize;
            view.transform.position = gameRuntime.Grid.FootprintCenterToWorld(segment.Cell, footprint);

            var spriteRenderer = view.GetComponent<SpriteRenderer>();
            Vector2 nativeSize = spriteRenderer.sprite.bounds.size;
            float cellSize = gameRuntime.Grid.CellSize;
            view.transform.localScale = new Vector3(
                footprint.x * cellSize / nativeSize.x,
                footprint.y * cellSize / nativeSize.y,
                1f);
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
