using Game.Data;
using Game.Gameplay.Research;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Global Research panel (CONTRACTS.md §11/§12), opened from the Bottom Nav "Research"
    /// category button - not tied to any single Laboratory instance, since every built
    /// Laboratory contributes to whichever research is active regardless of which panel opened
    /// it. For now: just the offered research list with its RP cost and unlock state (matches
    /// the source project's research_panel.gd at this stage) - starting a research and richer
    /// progress display can follow once the list itself is confirmed.
    /// </summary>
    public sealed class ResearchPanelController : MonoBehaviour
    {
        public const string PanelName = "research";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] ResearchDefinition[] researches;

        VisualElement _root;
        VisualElement _list;
        Label _rpLabel;

        void Start()
        {
            // Start(), not OnEnable() - see BuildingMenuController for why (GameRuntime.Awake
            // ordering across objects is not guaranteed, Start() always runs after all Awakes).
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("ResearchPanelRoot");
            _list = panelRoot.Q<VisualElement>("ResearchList");
            _rpLabel = panelRoot.Q<Label>("ResearchRpLabel");
            panelRoot.Q<Button>("ResearchCloseButton").clicked += Hide;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName)
        {
            _root.EnableInClassList("hidden", panelName != PanelName);
            if (panelName == PanelName) Refresh();
        }

        void Hide()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            gameRuntime.Selection.CloseGlobalPanel();
        }

        void Update()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            Refresh();
        }

        void Refresh()
        {
            ResearchSystem research = gameRuntime.Research;
            _rpLabel.text = $"RP: {Mathf.FloorToInt(research.Rp)}";

            _list.Clear();
            if (researches == null) return;

            foreach (ResearchDefinition definition in researches)
            {
                if (definition != null) _list.Add(BuildRow(research, definition));
            }
        }

        VisualElement BuildRow(ResearchSystem research, ResearchDefinition definition)
        {
            bool completed = research.IsUnlocked(definition.Id);
            bool inProgress = !completed && research.HasActiveResearch() && research.GetActiveResearch() == definition;
            bool locked = !completed && !inProgress && (research.HasActiveResearch() || research.Rp < definition.Cost);
            bool available = !completed && !inProgress && !locked;

            // A Button, not a plain VisualElement - clicking an available row starts that
            // research immediately (research.Start deducts the cost and rejects everything
            // this same availability check already rules out, so this can never double-spend).
            var row = new Button(() => research.Start(definition));
            row.SetEnabled(available);
            row.AddToClassList("research-row");
            row.AddToClassList(StateClass(completed, inProgress, locked));

            var icon = new VisualElement();
            icon.AddToClassList("research-row-icon");
            row.Add(icon);

            var text = new VisualElement();
            text.AddToClassList("research-row-text");

            var name = new Label(definition.DisplayName);
            name.AddToClassList("research-row-name");
            text.Add(name);

            var status = new Label(StatusText(research, definition, completed, inProgress));
            status.AddToClassList("research-row-status");
            text.Add(status);

            row.Add(text);
            return row;
        }

        static string StateClass(bool completed, bool inProgress, bool locked)
        {
            if (completed) return "research-state-completed";
            if (inProgress) return "research-state-in-progress";
            if (locked) return "research-state-locked";
            return "research-state-available";
        }

        static string StatusText(ResearchSystem research, ResearchDefinition definition, bool completed, bool inProgress)
        {
            if (completed) return "TERMINEE";
            if (inProgress) return $"EN COURS ({Mathf.FloorToInt(research.GetProgress() * 100f)}%)";
            return $"{Mathf.CeilToInt(definition.Cost)} RP";
        }
    }
}
