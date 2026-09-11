using System.Collections.Generic;
using Game.Core;
using Game.Gameplay.Buildings;
using Game.Gameplay.Exploration;
using Game.Grid;
using Game.Gameplay.Wrecks;
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
        readonly List<MapWreckMark> _wreckMarks = new List<MapWreckMark>();
        readonly List<MapDepositMark> _depositMarks = new List<MapDepositMark>();

        /// <summary>How many deposits existed last time the marks were built. A deposit never moves and is never removed until mined out, so a count is enough to notice a new one.</summary>
        int _depositCount = -1;

        /// <summary>
        /// The discovery version the marks were built against.
        ///
        /// The count alone is not enough, and that is the whole of what was wrong: what changes as a
        /// robot walks is not how many deposits exist - they all exist from the moment their sector
        /// materialises - but how many of them have been <b>seen</b>. Guarding on the count only
        /// meant the first build of the map was also the last.
        /// </summary>
        int _depositDiscoveryVersion = -1;

        /// <summary>Found rather than wired, following the camera controllers' own precedent: there is one of each in the scene, and a missing one only means a double click cannot travel.</summary>
        CameraPanController _cameraPan;

        /// <summary>How many wrecks were on the map last time it was built. Rebuilt only when one more is found - a wreck never moves.</summary>
        int _wreckCount = -1;

        /// <summary>How many buildings the cell list was built from. Expanding footprints allocates, so it is only redone when the count moves.</summary>
        int _buildingCount = -1;

        bool _bound;

        /// <summary>How much wider than the robots' range the opening view is, so the ring is inside the frame rather than exactly on its edge.</summary>
        const float OpeningMargin = 1.15f;

        // The world camera's own four actions, not a second set. Resolved once - FindAction walks
        // the maps, which has no business happening per frame.
        InputAction _panNorth;
        InputAction _panSouth;
        InputAction _panEast;
        InputAction _panWest;

        void Start()
        {
            _panNorth = InputBindings.Find(InputActionCatalogue.PanNorth);
            _panSouth = InputBindings.Find(InputActionCatalogue.PanSouth);
            _panEast = InputBindings.Find(InputActionCatalogue.PanEast);
            _panWest = InputBindings.Find(InputActionCatalogue.PanWest);

            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("SectorMapPanelRoot");
            _fleet = panelRoot.Q<Label>("SectorMapFleet");
            panelRoot.Q<Button>("SectorMapCloseButton").clicked += Hide;

            _cameraPan = FindAnyObjectByType<CameraPanController>();

            _map = new SectorMapElement();
            _map.TravelRequested += TravelTo;
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

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.GlobalPanel))
            {
                Hide();
                return;
            }

            _map.PanByKeyboard(PanDirection(), Time.unscaledDeltaTime);

            // The image rebuilds itself only when discovery moved, so this is a version comparison on
            // a still frame - see SectorMapImage. A rebuild can have brought a chunk into existence,
            // which is the only moment the element needs a child it does not already have.
            if (gameRuntime.SectorMap.Refresh()) _map.SyncTiles();

            _map.SetCoreRadius(gameRuntime.World.ActionRadiusCells);
            _map.SetOuterRingRadius(gameRuntime.ExplorerRangeCells);

            RenderBuildings();
            RenderDeposits();
            RenderWrecks();
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
        /// Every deposit cell a robot has actually seen, one mark per cell so a cluster reads as a
        /// patch - and a cluster half opened reads as half a patch, which is the truth about it.
        ///
        /// <b>Discovery is the gate, and it was missing.</b> A deposit exists from the moment its
        /// sector materialises, which has nothing to do with anybody having been there: sectors
        /// materialise as the world is generated around the base, while discovery is per cell and
        /// comes from a robot's 6-cell reveal. Without the gate the map showed the ore of the whole
        /// materialised world - deposits sitting in black, far outside the explored corridor and
        /// beyond the robots' own range, which is exactly how it was reported.
        ///
        /// <b>Rebuilt when either fact moves</b>: a new deposit, or new ground. The discovery version
        /// changes on every reveal, so this rebuilds while a robot walks and stands still otherwise;
        /// <c>SetDeposits</c> then repaints only if the marks actually differ.
        /// </summary>
        void RenderDeposits()
        {
            IReadOnlyList<DepositRuntime> deposits = gameRuntime.World?.OreDeposits;
            DiscoveryRuntime discovery = gameRuntime.Discovery;

            int count = deposits?.Count ?? 0;
            int discoveryVersion = discovery?.Version ?? 0;
            if (count == _depositCount && discoveryVersion == _depositDiscoveryVersion) return;

            _depositCount = count;
            _depositDiscoveryVersion = discoveryVersion;
            _depositMarks.Clear();

            for (int i = 0; i < count; i++)
            {
                DepositRuntime deposit = deposits[i];
                MapOreKind ore = MapDepositMark.OreFor(deposit.ItemId);

                foreach (Vector2Int offset in deposit.Definition.FootprintCells)
                {
                    var cell = new GridCoord(deposit.Origin.X + offset.x, deposit.Origin.Y + offset.y);

                    // No discovery data at all means no restriction, the convention the rest of the
                    // project already uses for a missing system - a headless test draws everything.
                    if (discovery != null && !discovery.IsDiscovered(cell)) continue;

                    _depositMarks.Add(new MapDepositMark(cell.X, cell.Y, ore));
                }
            }

            _map.SetDeposits(_depositMarks);
        }

        /// <summary>
        /// The wrecks the player has found.
        ///
        /// Rebuilt on a count rather than every frame: a wreck never moves, so the only thing that
        /// can change is that there is one more of them.
        /// </summary>
        void RenderWrecks()
        {
            WreckField wrecks = gameRuntime.Wrecks;
            int found = wrecks?.DiscoveredCount ?? 0;
            if (found == _wreckCount) return;

            _wreckCount = found;
            _wreckMarks.Clear();

            if (wrecks != null)
            {
                IReadOnlyList<WreckSite> sites = wrecks.Sites;
                for (int i = 0; i < sites.Count; i++)
                {
                    if (sites[i].Discovered) _wreckMarks.Add(new MapWreckMark(sites[i].CentreCells));
                }
            }

            _map.SetWrecks(_wreckMarks);
        }

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

        /// <summary>
        /// The same four actions the world camera pans with - not the same keys by coincidence, the
        /// same actions. The two used to hold separate literal copies of one cluster, which is the
        /// duplication the binding table exists to end.
        /// </summary>
        /// <summary>
        /// Takes the player where they double-clicked, and closes the map.
        ///
        /// Closing is the point as much as the move: a map left open over the place it just took you
        /// to is a map you have to dismiss before you can see what you asked for.
        /// </summary>
        void TravelTo(Vector2 cellPosition)
        {
            if (_cameraPan == null) return;

            _cameraPan.CentreOnCell(cellPosition);
            Hide();
        }

        Vector2 PanDirection()
        {
            var move = Vector2.zero;
            if (InputBindings.IsPressed(_panNorth)) move.y += 1f;
            if (InputBindings.IsPressed(_panSouth)) move.y -= 1f;
            if (InputBindings.IsPressed(_panEast)) move.x += 1f;
            if (InputBindings.IsPressed(_panWest)) move.x -= 1f;
            return move;
        }
    }
}
