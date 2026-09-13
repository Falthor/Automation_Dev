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

        /// <summary>The Auto halo's colour (MAP.md) - the same blue the Auto button itself lights up with (.recipe-action-button-on), so the panel and the world read as one signal.</summary>
        static readonly Color AutoHaloColor = new Color(0.33f, 0.87f, 0.96f, 0.55f);

        /// <summary>How much wider than the robot's own sprite the halo sits - the same idea as DepositHoverGlowView's paddingFactor, so it reads as a ring around the robot rather than a second copy of it.</summary>
        const float AutoHaloSizeFactor = 1.8f;

        readonly GridRuntime _grid;
        readonly ExplorerRobotSettings _settings;
        readonly BuildingShadowSettings _shadowSettings;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly List<GameObject> _views = new List<GameObject>();

        /// <summary>One per robot, parallel to _views - a child of the same GameObject, so it moves and is destroyed with it for free.</summary>
        readonly List<SpriteRenderer> _autoHalos = new List<SpriteRenderer>();

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

                // A halo is drawn for every robot currently in Auto, selected or not - distinct
                // from BuildingHoverHighlightView's single, selection-only outline (MAP.md).
                _autoHalos[i].enabled = robots[i].Auto;
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

            _autoHalos.Add(CreateAutoHalo(view.transform, cellSize, scale));

            return view;
        }

        /// <summary>
        /// A child of the robot's own transform, so it tracks position and (harmlessly, being
        /// radially symmetric) rotation for free. Its local scale corrects for the parent's own
        /// <paramref name="parentScale"/> so the halo's world size is <see cref="AutoHaloSizeFactor"/>
        /// times the robot's, regardless of what the parent's scale happens to be.
        /// </summary>
        SpriteRenderer CreateAutoHalo(Transform parent, float cellSize, float parentScale)
        {
            var halo = new GameObject("AutoHalo");
            halo.transform.SetParent(parent, worldPositionStays: false);
            halo.transform.localPosition = Vector3.zero;

            var renderer = halo.AddComponent<SpriteRenderer>();
            renderer.sprite = _spriteFactory.CreateRadialGlowSprite(AutoHaloColor);
            renderer.sortingOrder = SortingBands.FlyingGlow;
            renderer.enabled = false;

            Vector2 haloNativeSize = renderer.sprite.bounds.size;
            float desiredWorldSize = cellSize * AutoHaloSizeFactor;
            float localScale = desiredWorldSize / Mathf.Max(haloNativeSize.x, haloNativeSize.y) / Mathf.Max(parentScale, 0.0001f);
            halo.transform.localScale = new Vector3(localScale, localScale, 1f);

            return renderer;
        }
    }
}
