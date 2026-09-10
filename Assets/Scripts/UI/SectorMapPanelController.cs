using System.Collections.Generic;
using Game.Gameplay.Buildings;
using Game.Gameplay.Exploration;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The zoomed-out map panel.
    ///
    /// <b>It is a map, and only a map.</b> Nothing is designated here: no target, no zone, no sector.
    /// It answers two questions - what ground has been opened, and where the robots are - and both are
    /// drawn rather than written, which is why there is no side pane and no breadcrumb. The sector
    /// division was never something the player should point at; it is how the world is cut up
    /// internally.
    ///
    /// The panel is a view over `SectorMapImage` and the robot system, and owns no state of its own
    /// beyond the reusable lists it hands the element.
    /// </summary>
    public sealed class SectorMapPanelController : MonoBehaviour
    {
        public const string PanelName = "sectormap";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _root;
        SectorMapElement _map;
        Label _fleet;

        /// <summary>Reused across frames so a still map allocates nothing.</summary>
        readonly List<MapBuildingCell> _buildingCells = new List<MapBuildingCell>();
        readonly List<MapRobotMark> _robotMarks = new List<MapRobotMark>();

        /// <summary>How many buildings the cell list was built from. Expanding footprints allocates, so it is only redone when the count moves.</summary>
        int _buildingCount = -1;

        bool _bound;

        /// <summary>How much wider than the robots' range the opening view is, so the ring is inside the frame rather than exactly on its edge.</summary>
        const float OpeningMargin = 1.15f;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("SectorMapPanelRoot");
            _fleet = panelRoot.Q<Label>("SectorMapFleet");
            panelRoot.Q<Button>("SectorMapCloseButton").clicked += Hide;

            _map = new SectorMapElement();
            panelRoot.Q<VisualElement>("SectorMapViewport").Add(_map);

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;
        }

        void OnDestroy()
        {
            if (gameRuntime != null && gameRuntime.Selection != null)
                gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName)
        {
            bool visible = panelName == PanelName;
            _root.EnableInClassList("hidden", !visible);

            // While the map is up, ZQSD belongs to it. Set on the runtime rather than reached for by
            // the camera so it is cleared by the same event that closes the panel - a flag nobody
            // remembers to lower is a camera that never moves again.
            gameRuntime.KeyboardOwnedByPanel = visible;

            // Opening always finds the player rather than wherever they last dragged to. A map that
            // opens somewhere unexpected costs a moment of "where am I" every single time.
            if (visible && Bind()) FrameRange();
        }

        void Hide()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            gameRuntime.Selection.CloseGlobalPanel();
        }

        /// <summary>Hands the element the map image once the world exists. Answers whether it is usable.</summary>
        bool Bind()
        {
            if (_bound) return true;
            if (gameRuntime.SectorMap == null || gameRuntime.Sectors == null || gameRuntime.World == null) return false;

            _map.Bind(gameRuntime.SectorMap, gameRuntime.Sectors.SectorSizeCells, gameRuntime.World.CoreCenterCells);

            _bound = true;
            return true;
        }

        /// <summary>Opens on the whole of the ground the robots can reach: the first thing to see is how far one may go, not how far one has got.</summary>
        void FrameRange()
        {
            float range = gameRuntime.ExplorerRangeCells;
            _map.FrameCells(gameRuntime.World.CoreCenterCells,
                (range > 0f ? range : 200f) * OpeningMargin);
        }

        void Update()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            if (!Bind()) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            if (keyboard != null) _map.PanByKeyboard(PanDirection(keyboard), Time.unscaledDeltaTime);

            // The image rebuilds itself only when discovery moved, so this is a version comparison on
            // a still frame - see SectorMapImage. A rebuild can have brought a chunk into existence,
            // which is the only moment the element needs a child it does not already have.
            if (gameRuntime.SectorMap.Refresh()) _map.SyncTiles();

            _map.SetCoreRadius(gameRuntime.World.ActionRadiusCells);
            _map.SetOuterRingRadius(gameRuntime.ExplorerRangeCells);

            RenderBuildings();
            RenderRobots();
        }

        /// <summary>
        /// The base, expanded to the cells it really occupies.
        ///
        /// <b>Rebuilt only when the building count moves.</b> Expanding footprints allocates an array
        /// per building, and nothing can be built or demolished while the map covers the screen.
        /// </summary>
        void RenderBuildings()
        {
            if (gameRuntime.Transport == null) return;

            int count = 0;
            foreach (BuildingRuntime building in gameRuntime.Transport.GetAllBuildings()) count++;
            if (count == _buildingCount) return;

            _buildingCount = count;
            _buildingCells.Clear();

            foreach (BuildingRuntime building in gameRuntime.Transport.GetAllBuildings())
            {
                bool belt = IsBelt(building);
                foreach (Vector2Int offset in building.Definition.FootprintCells)
                {
                    _buildingCells.Add(new MapBuildingCell(
                        building.Cell.X + offset.x, building.Cell.Y + offset.y, belt));
                }
            }

            _map.SetBuildings(_buildingCells);
        }

        /// <summary>What carries rather than transforms. The same three types the building cap exempts, and for the same reason: they are the network, not the works.</summary>
        static bool IsBelt(BuildingRuntime building)
            => building is ConveyorRuntime || building is SplitterRuntime || building is CrossroadRuntime;

        /// <summary>
        /// Where the robots are, and which one is being inspected. Rebuilt every frame into a reused
        /// list - a wandering robot moves continuously, so there is nothing to compare against that
        /// would let this be skipped; the element itself is what decides whether to repaint.
        /// </summary>
        void RenderRobots()
        {
            ExplorerRobotSystem robots = gameRuntime.ExplorerRobots;
            if (robots == null || !robots.RobotsHaveAppeared)
            {
                _robotMarks.Clear();
                _map.SetRobots(_robotMarks);
                _fleet.text = string.Empty;
                return;
            }

            ExplorerRobotRuntime selected = gameRuntime.Selection.SelectedExplorerRobot;

            _robotMarks.Clear();
            IReadOnlyList<ExplorerRobotRuntime> fleet = robots.Robots;
            int cards = 0;

            for (int i = 0; i < fleet.Count; i++)
            {
                ExplorerRobotRuntime robot = fleet[i];
                cards += robot.Cards;

                _robotMarks.Add(new MapRobotMark(robot.Position,
                    ReferenceEquals(robot, selected),
                    robot.State != ExplorerRobotState.Idle));
            }

            _map.SetRobots(_robotMarks);

            // The one line of text on this screen, and it is about the fleet rather than about the
            // ground: how many are out and what they are carrying is what decides whether to go and
            // find one.
            _fleet.text = $"{robots.OutCount}/{fleet.Count} dehors · {cards} datacards";
        }

        static Vector2 PanDirection(Keyboard keyboard)
        {
            var move = Vector2.zero;
            if (keyboard.zKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.qKey.isPressed) move.x -= 1f;
            return move;
        }
    }
}
