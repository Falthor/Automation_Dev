using System.Collections.Generic;
using Game.Gameplay.Sites;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws each builder robot, one cell wide, with a ground shadow thrown further than a
    /// building's because a drone flies (see DropShadow.HeightMultiplier). Falls back to a plain
    /// coloured square when no drone art is assigned, which is what TASK_05_ROBOT_CONSTRUCTEUR.md §2
    /// asked for and what a scene without the asset still gets.
    ///
    /// The runtime is authoritative for position: this view only converts
    /// BuilderRobotRuntime.Position (grid-space, advanced by the central tick) into world space,
    /// and never moves a robot itself.
    /// </summary>
    public sealed class BuilderRobotVisualSync : MonoBehaviour
    {
        const int SortingOrder = 14;

        [SerializeField] GameRuntime gameRuntime;

        /// <summary>Drone art shown for every robot. Optional: null falls back to the procedural square.</summary>
        [SerializeField] Sprite robotSprite;

        /// <summary>Fraction of a cell the robot occupies. 1 fills its cell exactly; the sprite keeps its aspect ratio inside that, so a non-square drone is never stretched.</summary>
        [SerializeField, Range(0.1f, 1f)] float robotVisualScale = 1f;

        /// <summary>
        /// Kept from the square-placeholder era and still useful with real art: the two robots must
        /// be told apart at a glance, so the second one is tinted. White leaves the art untouched.
        /// </summary>
        [SerializeField] Color firstRobotColor = Color.white;
        [SerializeField] Color secondRobotColor = new Color(0.62f, 0.82f, 1f, 1f);

        /// <summary>How far the drone's shadow falls compared to a building's, i.e. how high it flies.</summary>
        [SerializeField, Min(0f)] float flightHeight = 3f;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly List<GameObject> _views = new List<GameObject>();

        void LateUpdate()
        {
            if (gameRuntime == null || gameRuntime.ConstructionSites == null || gameRuntime.Grid == null) return;

            IReadOnlyList<BuilderRobotRuntime> robots = gameRuntime.ConstructionSites.Robots;
            float cellSize = gameRuntime.Grid.CellSize;

            for (int i = 0; i < robots.Count; i++)
            {
                if (i >= _views.Count) _views.Add(CreateRobotView(i, cellSize));

                Vector2 gridPosition = robots[i].Position;
                _views[i].transform.position = new Vector3(gridPosition.x * cellSize, gridPosition.y * cellSize, 0f);
            }
        }

        GameObject CreateRobotView(int index, float cellSize)
        {
            var view = new GameObject($"BuilderRobot {index}");
            var renderer = view.AddComponent<SpriteRenderer>();
            renderer.sprite = robotSprite != null ? robotSprite : _spriteFactory.CreateSolidSquareSprite(Color.white);
            renderer.color = index == 0 ? firstRobotColor : secondRobotColor;
            renderer.sortingOrder = SortingOrder;

            // Uniform fit, so the drone's own proportions survive - a per-axis stretch would squash
            // a 1275x1233 sprite into a square.
            Vector2 nativeSize = renderer.sprite.bounds.size;
            float desired = cellSize * robotVisualScale;
            float scale = desired / Mathf.Max(nativeSize.x, nativeSize.y);
            view.transform.localScale = new Vector3(scale, scale, 1f);

            if (gameRuntime.ShadowSettings != null)
            {
                var shadow = view.AddComponent<DropShadow>();
                shadow.Settings = gameRuntime.ShadowSettings;
                shadow.HeightMultiplier = flightHeight;
            }

            return view;
        }
    }
}
