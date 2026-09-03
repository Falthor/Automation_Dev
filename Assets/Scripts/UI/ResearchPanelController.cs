using System.Collections.Generic;
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
    /// it. Lists every offered research with its RP cost and unlock state; clicking an available
    /// one starts it through ResearchSystem.Start (CONTRACTS.md §11) - the only command this
    /// panel issues.
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

        /// <summary>
        /// One built row per offered research, kept alive for as long as the panel is open. The
        /// rows are NOT rebuilt every frame: a Button destroyed and recreated between the pointer
        /// going down and coming back up never completes its click, which is exactly why clicking
        /// a research did nothing. Only their labels/state classes refresh per frame.
        /// </summary>
        readonly List<(ResearchDefinition definition, Button row, Label status)> _rows = new List<(ResearchDefinition, Button, Label)>();

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
            if (panelName != PanelName) return;

            BuildRows();
            Refresh();
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

        /// <summary>Creates one row per offered research. Called when the panel opens, never per frame - see _rows.</summary>
        void BuildRows()
        {
            _list.Clear();
            _rows.Clear();
            if (researches == null) return;

            ResearchSystem research = gameRuntime.Research;
            foreach (ResearchDefinition definition in researches)
            {
                if (definition == null) continue;

                // A Button, not a plain VisualElement - clicking an available row starts that
                // research immediately (research.Start deducts the cost and rejects everything
                // the availability styling already rules out, so this can never double-spend).
                ResearchDefinition captured = definition;
                var row = new Button(() => research.Start(captured));
                row.AddToClassList("research-row");

                var icon = new VisualElement();
                icon.AddToClassList("research-row-icon");
                row.Add(icon);

                var text = new VisualElement();
                text.AddToClassList("research-row-text");

                var name = new Label(definition.DisplayName);
                name.AddToClassList("research-row-name");
                text.Add(name);

                var status = new Label();
                status.AddToClassList("research-row-status");
                text.Add(status);

                row.Add(text);
                _list.Add(row);
                _rows.Add((definition, row, status));
            }
        }

        /// <summary>Per-frame update of the existing rows only: RP total, each row's state class, enabled-ness and status line.</summary>
        void Refresh()
        {
            ResearchSystem research = gameRuntime.Research;
            _rpLabel.text = $"RP: {Mathf.FloorToInt(research.Rp)}";

            foreach ((ResearchDefinition definition, Button row, Label status) in _rows)
            {
                bool completed = research.IsUnlocked(definition.Id);
                bool inProgress = !completed && research.GetActiveResearch() == definition;
                bool locked = !completed && !inProgress
                    && (research.HasActiveResearch() || !research.ArePrerequisitesMet(definition) || research.Rp < definition.Cost);
                bool available = !completed && !inProgress && !locked;

                row.SetEnabled(available);
                row.EnableInClassList("research-state-completed", completed);
                row.EnableInClassList("research-state-in-progress", inProgress);
                row.EnableInClassList("research-state-locked", locked);
                row.EnableInClassList("research-state-available", available);
                status.text = StatusText(research, definition, completed, inProgress);
            }
        }

        static string StatusText(ResearchSystem research, ResearchDefinition definition, bool completed, bool inProgress)
        {
            if (completed) return "TERMINEE";
            if (inProgress) return $"EN COURS ({Mathf.FloorToInt(research.GetProgress() * 100f)}%)";

            // A missing prerequisite is named rather than just greying the row out: the cost
            // alone would leave the player wondering why an affordable research does nothing.
            if (!research.ArePrerequisitesMet(definition)) return $"REQUIERT : {definition.RequiresResearch.DisplayName}";

            return $"{Mathf.CeilToInt(definition.Cost)} RP";
        }
    }
}
