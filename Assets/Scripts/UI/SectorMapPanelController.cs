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

        bool _bound;

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

            _map = new SectorMapElement();
            _map.HoveredSectorChanged += OnHoveredSectorChanged;
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

            // Opening always finds the player, rather than wherever they last dragged to. A map that
            // opens somewhere unexpected costs a moment of "where am I" every single time.
            if (visible && Bind()) _map.CentreOnCore();
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

            // The image rebuilds itself only when discovery moved, so this is a version comparison on
            // a still frame - see SectorMapImage.
            gameRuntime.SectorMap.Refresh();
            _map.SetCoreRadius(gameRuntime.World.ActionRadiusCells);
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
