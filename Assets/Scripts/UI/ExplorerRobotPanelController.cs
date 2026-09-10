using Game.Gameplay.Exploration;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The panel a clicked explorer robot opens: what it is doing, how far out it is, and the one
    /// action there is - send it wandering, or call it home.
    ///
    /// Same shell and routing as every other contextual inspector (see
    /// PowerplantGazPanelController), keyed off its own selection slot rather than
    /// <c>SelectedBuilding</c>: a robot is not a building, it occupies no cell, and every
    /// per-building panel keys off SelectionChanged with an <c>as</c> cast - so riding that slot
    /// would have needed every one of them to learn to ignore it.
    ///
    /// <b>The action button is rebuilt never, only relabelled.</b> A UI Toolkit Button whose
    /// element is replaced between the press and the release can never complete a click, which this
    /// project has already been bitten by once on the map screen.
    /// </summary>
    public sealed class ExplorerRobotPanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        /// <summary>The datacard's art. Serialized here rather than read off the settings asset, which is the project's pattern for panel icons - see ConstructionSitePanelController's robotIcon.</summary>
        [SerializeField] Sprite cardIcon;

        VisualElement _root;
        Label _state;
        Label _distance;
        Label _harvest;
        Label _cards;
        VisualElement _cardIcon;
        Label _hint;
        Button _action;

        ExplorerRobotRuntime _selected;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("ExplorerRobotPanelRoot");
            _state = panelRoot.Q<Label>("ExplorerRobotState");
            _distance = panelRoot.Q<Label>("ExplorerRobotDistance");
            _harvest = panelRoot.Q<Label>("ExplorerRobotHarvest");
            _cards = panelRoot.Q<Label>("ExplorerRobotCards");
            _cardIcon = panelRoot.Q<VisualElement>("ExplorerRobotCardIcon");
            _hint = panelRoot.Q<Label>("ExplorerRobotHint");

            _action = panelRoot.Q<Button>("ExplorerRobotActionButton");
            _action.clicked += Toggle;

            panelRoot.Q<Button>("ExplorerRobotCloseButton").clicked += Close;

            // Set once: the art never changes, so there is nothing for Render to do with it.
            if (cardIcon != null) _cardIcon.style.backgroundImage = new StyleBackground(cardIcon);

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.ExplorerRobotSelectionChanged += OnSelectionChanged;
        }

        void OnDestroy()
        {
            if (gameRuntime != null && gameRuntime.Selection != null)
            {
                gameRuntime.Selection.ExplorerRobotSelectionChanged -= OnSelectionChanged;
            }
        }

        void OnSelectionChanged(ExplorerRobotRuntime robot)
        {
            _selected = robot;
            _root.EnableInClassList("hidden", _selected == null);
            if (_selected != null) Render();
        }

        void Close() => gameRuntime.Selection.Clear();

        /// <summary>The whole interaction. Sends an idle robot out, turns a wandering one round - the system owns which, so the panel does not restate the rule.</summary>
        void Toggle()
        {
            if (_selected == null) return;

            gameRuntime.ExplorerRobots?.Toggle(_selected);
            Render();
        }

        void Update()
        {
            if (_selected == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            Render();
        }

        void Render()
        {
            _state.text = StateText(_selected.State);
            _distance.text = $"{DistanceFromCore():0} cases";

            ExplorerRobotSystem robots = gameRuntime.ExplorerRobots;
            _harvest.text = HarvestText(robots != null ? robots.HarvestStateOf(_selected) : ExplorerHarvestState.AtBase);
            _cards.text = robots != null ? $"{_selected.Cards}/{robots.MaxCards}" : _selected.Cards.ToString();

            // Relabelled, never rebuilt - see the class summary.
            _action.text = ExplorerRobotSystem.ActionLabel(_selected.State);
            _hint.text = HintText(_selected.State);
        }

        float DistanceFromCore()
        {
            Vector2 core = gameRuntime.World?.CoreCenterCells ?? Vector2.zero;
            return Vector2.Distance(_selected.Position, core);
        }

        static string StateText(ExplorerRobotState state) => state switch
        {
            ExplorerRobotState.Exploring => "En exploration",
            ExplorerRobotState.Returning => "En retour",
            _ => "Au repos"
        };

        /// <summary>
        /// Whether the robot is still earning, in words.
        ///
        /// <b>The most important line on the panel</b>, and the reason it says why rather than yes or
        /// no: a full robot and one circling its own trail both earn nothing, but only one of them is
        /// worth recalling. No rate and no cells-per-minute - that is what the measurement log is for,
        /// and a throughput figure is not a decision.
        /// </summary>
        static string HarvestText(ExplorerHarvestState state) => state switch
        {
            ExplorerHarvestState.Harvesting => "En cours",
            ExplorerHarvestState.OverKnownGround => "Terrain déjà connu",
            ExplorerHarvestState.StockFull => "Plein",
            ExplorerHarvestState.Returning => "Rentre",
            _ => "À la base"
        };

        /// <summary>What the button will do, said once under it. A label alone reads as a state on a panel that is already showing one.</summary>
        static string HintText(ExplorerRobotState state) => state switch
        {
            ExplorerRobotState.Exploring => "Découvre la carte en avançant. Un clic le rappelle.",
            ExplorerRobotState.Returning => "Rentre au Noyau. Un clic le renvoie explorer.",
            _ => "Part errer et découvrir, sans destination."
        };
    }
}
