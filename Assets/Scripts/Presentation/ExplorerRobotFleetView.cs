using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Exploration;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws the explorer robots wherever they are - parked at the base or out wandering.
    ///
    /// <b>Not a MonoBehaviour</b>, like the park before it: <c>GameRuntime</c> owns it and refreshes
    /// it from the one central Update, so there is nothing to wire into a scene and no per-robot
    /// <c>Update</c>. The runtime is authoritative for position and heading - this converts grid
    /// space to world space and turns a sprite, and decides nothing.
    /// </summary>
    public sealed class ExplorerRobotFleetView
    {
        /// <summary>How fast the sprite swings round to a new heading. The heading itself changes continuously, so this is only here to keep a restored save or a fresh departure from snapping.</summary>
        const float TurnDegreesPerSecond = 720f;

        /// <summary>Which way the art already points when unrotated - the same idea as ConveyorDefinition.ArtNativeDirection, so the rotation is measured against the pose the artist drew.</summary>
        const float ArtNativeBearingDegrees = 90f;

        readonly GridRuntime _grid;
        readonly ExplorerRobotSettings _settings;
        readonly BuildingShadowSettings _shadowSettings;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly List<GameObject> _views = new List<GameObject>();

        public ExplorerRobotFleetView(GridRuntime grid, ExplorerRobotSettings settings,
            BuildingShadowSettings shadowSettings)
        {
            _grid = grid;
            _settings = settings;
            _shadowSettings = shadowSettings;
        }

        /// <summary>How many markers stand in the world. Watched by a test: one per robot, created once and reused, never one per frame.</summary>
        public int MarkerCount => _views.Count;

        /// <summary>
        /// Puts every robot where the runtime says it is. Safe to call every frame; creates a view
        /// the first time it sees a robot and reuses it forever after.
        /// </summary>
        public void Refresh(ExplorerRobotSystem system, float deltaSeconds)
        {
            // Nothing is drawn before the fleet arrives: the robots exist as objects from the
            // first frame, and marking the ground with machines the player has not been given yet
            // would announce them early.
            if (system == null || !system.RobotsHaveAppeared || _grid == null || _settings == null) return;

            IReadOnlyList<ExplorerRobotRuntime> robots = system.Robots;
            float cellSize = _grid.CellSize;

            for (int i = 0; i < robots.Count; i++)
            {
                if (i >= _views.Count) _views.Add(CreateView(i, cellSize));

                Vector2 position = robots[i].Position;
                Transform view = _views[i].transform;
                view.position = new Vector3(position.x * cellSize, position.y * cellSize, 0f);

                FaceHeading(view, robots[i].HeadingDegrees, deltaSeconds);
            }
        }

        /// <summary>
        /// Turns the sprite to the heading the runtime holds. Read off the robot rather than from a
        /// frame-to-frame position delta: the heading is what the simulation actually steers, it is
        /// exact on the first frame of a sortie, and it survives a restored save without one
        /// spurious spin.
        /// </summary>
        void FaceHeading(Transform view, float headingDegrees, float deltaSeconds)
        {
            float targetZ = headingDegrees - ArtNativeBearingDegrees;
            float currentZ = view.rotation.eulerAngles.z;

            view.rotation = Quaternion.Euler(0f, 0f,
                Mathf.MoveTowardsAngle(currentZ, targetZ, TurnDegreesPerSecond * deltaSeconds));
        }

        GameObject CreateView(int index, float cellSize)
        {
            var view = new GameObject($"ExplorerRobot {index}");
            var renderer = view.AddComponent<SpriteRenderer>();
            renderer.sprite = _settings.RobotSprite != null
                ? _settings.RobotSprite
                : _spriteFactory.CreateSolidSquareSprite(Color.white);

            // The flying band, above everything depth-sorted. A wandering robot crosses the whole
            // base on its way out, and a depth-sorted one would keep disappearing behind buildings
            // it is passing - the same reason the builder drones are in this band. Fixed, so
            // nothing has to be re-ranked as it moves.
            renderer.sortingOrder = SortingBands.FlyingFirst;

            // Uniform fit, so the art keeps its own proportions inside one cell rather than being
            // stretched into a square.
            Vector2 nativeSize = renderer.sprite.bounds.size;
            float scale = cellSize / Mathf.Max(nativeSize.x, nativeSize.y);
            view.transform.localScale = new Vector3(scale, scale, 1f);

            if (_shadowSettings != null)
            {
                var shadow = view.AddComponent<DropShadow>();
                shadow.Settings = _shadowSettings;
            }

            return view;
        }
    }
}
