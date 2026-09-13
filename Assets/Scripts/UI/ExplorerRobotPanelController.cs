using Game.Gameplay.Exploration;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The panel a clicked explorer robot opens: what it is doing, how far out it is, and its two
    /// controls - Auto, and Retour.
    ///
    /// Same shell and routing as every other contextual inspector (see
    /// PowerplantGazPanelController), keyed off its own selection slot rather than
    /// <c>SelectedBuilding</c>: a robot is not a building, it occupies no cell, and every
    /// per-building panel keys off SelectionChanged with an <c>as</c> cast - so riding that slot
    /// would have needed every one of them to learn to ignore it.
    ///
    /// <b>Both buttons are rebuilt never, only relabelled/restyled.</b> A UI Toolkit Button whose
    /// element is replaced between the press and the release can never complete a click, which this
    /// project has already been bitten by once on the map screen.
    ///
    /// <b>Neither button owns the robot's state.</b> Both call into
    /// <c>ExplorerRobotSystem</c> (<c>SetAuto</c>/<c>Recall</c>) rather than writing
    /// <c>ExplorerRobotRuntime</c> fields directly - the same rule <c>ConstructionInputAdapter</c>'s
    /// right-click command follows, so the two can never disagree about what a robot is doing
    /// (DEVELOPMENT_RULES §1/§6).
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
        Button _autoButton;
        Button _returnButton;

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

            _autoButton = panelRoot.Q<Button>("ExplorerRobotAutoButton");
            _autoButton.clicked += ToggleAuto;
            _returnButton = panelRoot.Q<Button>("ExplorerRobotReturnButton");
            _returnButton.clicked += Recall;

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

        /// <summary>Flips Auto - the system decides what that does to the robot's movement, the panel only asks for the flip.</summary>
        void ToggleAuto()
        {
            if (_selected == null) return;

            gameRuntime.ExplorerRobots?.SetAuto(_selected, !_selected.Auto);
            gameRuntime.NotePlayerAction();
            Render();
        }

        /// <summary>Always heads home, whatever Auto currently reads.</summary>
        void Recall()
        {
            if (_selected == null) return;

            gameRuntime.ExplorerRobots?.Recall(_selected);
            gameRuntime.NotePlayerAction();
            Render();
        }

        void Update()
        {
            if (_selected == null) return;

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.ContextualPanel))
            {
                Close();
                return;
            }

            Render();
        }

        void Render()
        {
            _state.text = StateText(_selected);
            _distance.text = $"{DistanceFromCore():0} cases";

            ExplorerRobotSystem robots = gameRuntime.ExplorerRobots;
            _harvest.text = HarvestText(robots != null ? robots.HarvestStateOf(_selected) : ExplorerHarvestState.AtBase);
            _cards.text = robots != null ? $"{_selected.Cards}/{robots.MaxCards}" : _selected.Cards.ToString();

            // Restyled, never rebuilt - see the class summary.
            _autoButton.EnableInClassList("recipe-action-button-on", _selected.Auto);
            _hint.text = HintText(_selected);
        }

        float DistanceFromCore()
        {
            Vector2 core = gameRuntime.World?.CoreCenterCells ?? Vector2.zero;
            return Vector2.Distance(_selected.Position, core);
        }

        static string StateText(ExplorerRobotRuntime robot)
        {
            if (robot.State == ExplorerRobotState.Returning) return "En retour";
            if (robot.State != ExplorerRobotState.Exploring) return "Au repos";
            if (robot.Auto) return "En exploration";
            return robot.ManualTarget.HasValue ? "En route (manuel)" : "À l'arrêt (manuel)";
        }

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

        /// <summary>What the buttons will do, said once under them. A label alone reads as a state on a panel that is already showing one.</summary>
        static string HintText(ExplorerRobotRuntime robot)
        {
            if (robot.State == ExplorerRobotState.Returning) return "Rentre au Noyau.";

            const string manual = "Clic droit sur la carte pour le diriger manuellement.";

            if (robot.State != ExplorerRobotState.Exploring) // Idle
            {
                return robot.Auto ? $"Part errer et découvrir, sans destination. {manual}" : manual;
            }

            if (robot.Auto) return $"Découvre la carte en avançant. {manual}";

            return robot.ManualTarget.HasValue
                ? "En route vers sa destination. Un nouveau clic droit la change."
                : manual;
        }
    }
}
