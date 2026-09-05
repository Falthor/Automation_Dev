using System.Collections.Generic;
using Game.Core;
using Game.Gameplay.Sites;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws each builder robot, one cell wide, turned to face where it is travelling, with a
    /// ground shadow thrown further than a building's because a drone flies (see
    /// DropShadow.HeightMultiplier). Falls back to a plain coloured square when no drone art is
    /// assigned, which is what TASK_05_ROBOT_CONSTRUCTEUR.md §2 asked for and what a scene without
    /// the asset still gets.
    ///
    /// The runtime is authoritative for position: this view only converts
    /// BuilderRobotRuntime.Position (grid-space, advanced by the central tick) into world space,
    /// and never moves a robot itself. Heading is presentation too - nothing in the simulation
    /// depends on which way a drone points, so it is derived here rather than stored and saved.
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

        /// <summary>Which way the drone art already points when unrotated - the same idea as ConveyorDefinition.ArtNativeDirection, so the rotation below is measured against the pose the artist drew rather than against nothing.</summary>
        [SerializeField] Direction artNativeDirection = Direction.North;

        /// <summary>How fast a drone swings around to a new heading. High enough to look responsive, low enough that a turn is a turn rather than a teleport; 0 snaps instantly.</summary>
        [SerializeField, Min(0f)] float turnDegreesPerSecond = 540f;

        // Below this, the robot has effectively arrived and there is no meaningful heading left -
        // it keeps whichever way it was already facing instead of snapping back to its art pose.
        const float MinimumHeadingDistance = 0.0001f;

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

                FaceTravelDirection(_views[i].transform, robots[i]);
            }
        }

        /// <summary>
        /// Turns the drone to face where it is heading. The heading is read from the runtime's own
        /// MoveTarget rather than from a frame-to-frame position delta: it is the direction the
        /// robot is actually travelling in, it is exact from the first frame of a leg, and it
        /// survives a restored save without one spurious spin. The view still never moves anything -
        /// this is pure presentation, no robot decides anything by where it points.
        /// </summary>
        void FaceTravelDirection(Transform view, BuilderRobotRuntime robot)
        {
            Vector2 heading = robot.MoveTarget - robot.Position;
            if (heading.sqrMagnitude < MinimumHeadingDistance * MinimumHeadingDistance) return;

            // Clockwise-from-North, matching Direction.ToRotationDegrees, then negated for Unity's
            // counter-clockwise-positive Z - the same formula ConveyorView uses for its art.
            float headingDegrees = Mathf.Atan2(heading.x, heading.y) * Mathf.Rad2Deg;
            float targetZ = -(headingDegrees - artNativeDirection.ToRotationDegrees());

            float currentZ = view.rotation.eulerAngles.z;
            float nextZ = turnDegreesPerSecond > 0f
                ? Mathf.MoveTowardsAngle(currentZ, targetZ, turnDegreesPerSecond * Time.deltaTime)
                : targetZ;

            view.rotation = Quaternion.Euler(0f, 0f, nextZ);
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
