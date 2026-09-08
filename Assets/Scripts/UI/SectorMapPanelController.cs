using System.Collections.Generic;
using Game.Gameplay.Missions;
using Game.Gameplay.Sectors;
using Game.Grid;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The zoomed-out map panel: where a mission's target is designated (SPEC_EXPEDITIONS.md §5.1).
    ///
    /// <b>Hovering never reveals what a robot has not reported.</b> An unreconnoitred sector shows its
    /// state and nothing else — no name, no risk, no available missions. That is not a display detail:
    /// the whole reason expeditions exist is that the Core is blind out there, and a tooltip that
    /// answered would make sending a robot pointless. The check is on the sector's discovery, and it
    /// is the first thing this class does with a hovered index.
    /// </summary>
    public sealed class SectorMapPanelController : MonoBehaviour
    {
        public const string PanelName = "sectormap";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _root;
        SectorMapElement _map;
        Label _hoverName;
        Label _hoverDetail;

        Label _targetName;
        Label _targetState;
        VisualElement _missionList;
        Label _fleet;

        bool _bound;

        /// <summary>Reused each frame rather than allocated: this is rebuilt every Update and holds at most MaxConcurrentMissions entries.</summary>
        readonly List<int> _missionTargets = new List<int>();

        /// <summary>Every kind, always in this order, so the same mission is always in the same place in the list.</summary>
        static readonly MissionKind[] Kinds =
        {
            MissionKind.Prospection,
            MissionKind.ExplorationLointaine,
            MissionKind.Recuperation
        };

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("SectorMapPanelRoot");
            _hoverName = panelRoot.Q<Label>("SectorMapHoverName");
            _hoverDetail = panelRoot.Q<Label>("SectorMapHoverDetail");
            panelRoot.Q<Button>("SectorMapCloseButton").clicked += Hide;

            _targetName = panelRoot.Q<Label>("SectorMapTargetName");
            _targetState = panelRoot.Q<Label>("SectorMapTargetState");
            _missionList = panelRoot.Q<VisualElement>("SectorMapMissionList");
            _fleet = panelRoot.Q<Label>("SectorMapFleet");

            _map = new SectorMapElement();
            _map.HoveredSectorChanged += OnHoveredSectorChanged;
            _map.SelectedSectorChanged += _ => RenderTarget();
            panelRoot.Q<VisualElement>("SectorMapViewport").Add(_map);

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;

            ShowNothingHovered();
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

            // Opening always finds the player, rather than wherever they last dragged to. A map that
            // opens somewhere unexpected costs a moment of "where am I" every single time. The aim
            // is dropped with it: a target chosen last time is a decision that has since gone stale.
            if (visible && Bind())
            {
                _map.CentreOnCore();
                _map.ClearSelection();
                RenderTarget();
            }
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

            _map.Bind(gameRuntime.SectorMap.Texture, gameRuntime.SectorMap.SizeSectors,
                gameRuntime.Sectors.SectorSizeCells, gameRuntime.World.CoreCenterCells);

            _bound = true;
            return true;
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
            // a still frame - see SectorMapImage.
            gameRuntime.SectorMap.Refresh();
            _map.SetCoreRadius(gameRuntime.World.ActionRadiusCells);

            _missionTargets.Clear();
            if (gameRuntime.Missions != null)
            {
                foreach (MissionRuntime mission in gameRuntime.Missions.InFlight) _missionTargets.Add(mission.TargetSector);
            }
            _map.SetMissionTargets(_missionTargets);

            RenderTarget();
        }

        // ---- The aimed sector, and what may be sent to it ----

        /// <summary>
        /// Rebuilds the side pane for whatever is currently aimed at.
        ///
        /// Called on selection and every frame, because a refusal is not a property of the target: a
        /// slot frees when a mission lands, a robot's last charge is spent by the launch before it.
        /// A button that was enabled when the sector was picked would otherwise stay enabled after
        /// the reason for it went away.
        /// </summary>
        void RenderTarget()
        {
            if (_targetName == null || gameRuntime.Missions == null) return;

            int sector = _map.SelectedSector;
            _fleet.text = FleetLine();

            if (sector < 0)
            {
                _targetName.text = "Aucune cible";
                _targetState.text = "Cliquez un secteur sur la carte.";
                _missionList.Clear();
                return;
            }

            bool unknown = gameRuntime.Sectors.IsWhollyUnknown(sector, gameRuntime.Discovery);

            // Same rule as the hover, and for the same reason: what a robot has not reported is not
            // the Core's to know. A target can be aimed at without being described.
            _targetName.text = unknown ? "Secteur non reconnu" : gameRuntime.SectorCatalog.NameOf(sector);
            _targetState.text = unknown
                ? "Aucun robot n'y est allé."
                : $"Risque estimé : {RiskLabel(gameRuntime.SectorCatalog.RiskOf(sector))}";

            // In words as well as on the map: the amber outline says where, this says how long.
            string underway = MissionUnderwayTo(sector);
            if (underway != null) _targetState.text += "\n" + underway;

            RenderMissions(sector);
        }

        void RenderMissions(int sector)
        {
            _missionList.Clear();

            float radius = gameRuntime.World.ActionRadiusCells;
            foreach (MissionKind kind in Kinds)
            {
                MissionSystem.LaunchRefusal refusal = gameRuntime.Missions.CanLaunch(kind, sector, radius);

                // A kind the target's distance simply does not admit is left out rather than listed
                // as impossible: the bands are a property of where you are pointing, not a fault to
                // report, and three greyed rows on every sector would drown the one that can go.
                if (refusal == MissionSystem.LaunchRefusal.WrongBand
                    || refusal == MissionSystem.LaunchRefusal.NotASector) continue;

                _missionList.Add(BuildMissionRow(kind, sector, refusal));
            }

            if (_missionList.childCount != 0) return;

            var none = new Label("Aucune mission possible sur cette cible.");
            none.AddToClassList("sector-map-mission-none");
            _missionList.Add(none);
        }

        VisualElement BuildMissionRow(MissionKind kind, int sector, MissionSystem.LaunchRefusal refusal)
        {
            var row = new VisualElement();
            row.AddToClassList("sector-map-mission-row");

            var name = new Label(KindLabel(kind));
            name.AddToClassList("sector-map-mission-name");
            row.Add(name);

            // Always "estimee". SPEC_EXPEDITIONS.md 5.2 forbids ever presenting a mission's numbers
            // as certainties - the duration is the one figure shown at all, and it is a forecast.
            var duration = new Label($"~ {FormatDuration(gameRuntime.Missions.DurationOf(kind, sector))}");
            duration.AddToClassList("sector-map-mission-duration");
            row.Add(duration);

            if (refusal == MissionSystem.LaunchRefusal.None)
            {
                var launch = new Button(() => Launch(kind, sector)) { text = "Lancer" };
                launch.AddToClassList("sector-map-mission-launch");
                row.Add(launch);
            }
            else
            {
                var why = new Label(RefusalLabel(refusal));
                why.AddToClassList("sector-map-mission-refusal");
                row.Add(why);
            }

            return row;
        }

        void Launch(MissionKind kind, int sector)
        {
            gameRuntime.Missions.TryLaunch(kind, sector, gameRuntime.World.ActionRadiusCells, out _);
            RenderTarget();
        }

        /// <summary>What is already on its way to this sector, or null when nothing is. Names the kind, because two different missions to one place are two different questions.</summary>
        string MissionUnderwayTo(int sector)
        {
            if (gameRuntime.Missions == null) return null;

            foreach (MissionRuntime mission in gameRuntime.Missions.InFlight)
            {
                if (mission.TargetSector != sector) continue;
                return $"{KindLabel(mission.Kind)} en cours · {FormatDuration(mission.RemainingSeconds)}";
            }
            return null;
        }

        /// <summary>What the fleet has left, which is the one number that decides whether any of this is possible at all.</summary>
        string FleetLine()
        {
            MissionSystem missions = gameRuntime.Missions;
            if (missions == null || !missions.RobotsHaveAppeared) return string.Empty;

            return $"{missions.InFlight.Count}/{missions.MaxConcurrentMissions} en cours · {missions.TotalChargesLeft} charges";
        }

        static string KindLabel(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.Prospection: return "Prospection";
                case MissionKind.ExplorationLointaine: return "Exploration lointaine";
                default: return "Récupération";
            }
        }

        static string RefusalLabel(MissionSystem.LaunchRefusal refusal)
        {
            switch (refusal)
            {
                case MissionSystem.LaunchRefusal.RobotsHaveNotArrived: return "aucun robot";
                case MissionSystem.LaunchRefusal.AllSlotsBusy: return "emplacements occupés";
                case MissionSystem.LaunchRefusal.NoRobotAvailable: return "robots épuisés";
                case MissionSystem.LaunchRefusal.AlreadyReconnoitred: return "déjà reconnu";
                case MissionSystem.LaunchRefusal.NotYetReconnoitred: return "à reconnaître d'abord";
                case MissionSystem.LaunchRefusal.NothingToRecover: return "rien à récupérer";
                case MissionSystem.LaunchRefusal.AlreadyRecovered: return "déjà récupéré";
                default: return "impossible";
            }
        }

        /// <summary>Minutes and seconds, because a mission is minutes long and a bare count of seconds stops being readable past a hundred or so.</summary>
        static string FormatDuration(float seconds)
        {
            int whole = Mathf.RoundToInt(seconds);
            return whole < 60 ? $"{whole} s" : $"{whole / 60} min {whole % 60:00} s";
        }

        /// <summary>Same AZERTY layout the world camera uses, so the keys mean the same thing whichever is listening.</summary>
        static Vector2 PanDirection(Keyboard keyboard)
        {
            var move = Vector2.zero;
            if (keyboard.zKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.qKey.isPressed) move.x -= 1f;
            return move;
        }

        // ---- Hover ----

        void OnHoveredSectorChanged(int sector)
        {
            if (sector < 0)
            {
                ShowNothingHovered();
                return;
            }

            SectorGrid grid = gameRuntime.Sectors;

            // The one rule this panel exists to hold. Everything below the check is information a
            // robot brought back; above it, there is nothing to say but the state.
            if (grid.IsWhollyUnknown(sector, gameRuntime.Discovery))
            {
                _hoverName.text = "Secteur non reconnu";
                _hoverDetail.text = "Aucun robot n'y est allé.";
                return;
            }

            _hoverName.text = gameRuntime.SectorCatalog.NameOf(sector);
            _hoverDetail.text = $"Risque estimé : {RiskLabel(gameRuntime.SectorCatalog.RiskOf(sector))}";
        }

        void ShowNothingHovered()
        {
            _hoverName.text = string.Empty;
            _hoverDetail.text = "Molette pour zoomer, glisser pour déplacer.";
        }

        /// <summary>Qualitative, and always qualified as an estimate — §5.2 forbids ever showing a number.</summary>
        static string RiskLabel(SectorRisk risk)
        {
            switch (risk)
            {
                case SectorRisk.Low: return "faible";
                case SectorRisk.Moderate: return "modéré";
                case SectorRisk.High: return "élevé";
                default: return "critique";
            }
        }
    }
}
