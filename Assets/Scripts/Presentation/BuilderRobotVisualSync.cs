using System.Collections.Generic;
using Game.Gameplay.Sites;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws each builder robot as a plain moving square (TASK_05_ROBOT_CONSTRUCTEUR.md §2: for
    /// this first pass a black square is enough - the point of the task is the behavior, not the
    /// look; the only display requirement is that the two robots be told apart at a glance, so the
    /// second one is a lighter shade). The runtime is authoritative for position: this view only
    /// converts BuilderRobotRuntime.Position (grid-space, advanced by the central tick) into world
    /// space, and never moves a robot itself.
    /// </summary>
    public sealed class BuilderRobotVisualSync : MonoBehaviour
    {
        const int SortingOrder = 14;

        [SerializeField] GameRuntime gameRuntime;
        [SerializeField, Range(0.1f, 1f)] float robotVisualScale = 0.45f;
        [SerializeField] Color firstRobotColor = new Color(0.05f, 0.05f, 0.07f, 1f);
        [SerializeField] Color secondRobotColor = new Color(0.32f, 0.34f, 0.40f, 1f);

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
            renderer.sprite = _spriteFactory.CreateSolidSquareSprite(Color.white);
            renderer.color = index == 0 ? firstRobotColor : secondRobotColor;
            renderer.sortingOrder = SortingOrder;

            Vector2 nativeSize = renderer.sprite.bounds.size;
            float desired = cellSize * robotVisualScale;
            view.transform.localScale = new Vector3(desired / nativeSize.x, desired / nativeSize.y, 1f);

            return view;
        }
    }
}
