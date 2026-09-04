using System.Collections.Generic;
using System.Linq;
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
    /// category button. Linear introduction menu (TASK_02_REFONTE_RECHERCHE.md §8,
    /// `01-menu-recherche-intro.html`): the whole tree is read from
    /// GameRuntime.Researches (Game.Data.ResearchDatabase) - this panel never defines it.
    ///
    /// Five states, never distinguished by color alone - each also gets its own glyph:
    /// acquis (check), en cours (progress ring), disponible et payable / CU insuffisant (diamond,
    /// filled vs hollow), verrouille (lock). Clicking a row calls ResearchSystem.Enqueue, which is
    /// a safe no-op for a row that is locked/active/queued/completed (CanQueue's own guard) - so
    /// every row shares one click handler. Clicking a locked row additionally highlights its
    /// missing prerequisites instead of doing nothing.
    /// </summary>
    public sealed class ResearchPanelController : MonoBehaviour
    {
        public const string PanelName = "research";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _root;
        Label _reserveLabel;
        VisualElement _list;
        VisualElement _queueSection;
        VisualElement _queueList;

        VisualElement _detailPanel;
        Label _detailState;
        Label _detailName;
        Label _detailDescription;
        Label _detailEffect;
        VisualElement _detailProgressFill;
        Label _detailCostLabel;
        Label _detailTimeLabel;
        Label _detailAbsorptionValue;
        VisualElement _detailPrerequisites;
        Button _detailActionButton;

        /// <summary>One row per research in the tree, built once when the panel opens - never rebuilt per frame (a Button destroyed and recreated between pointer-down and pointer-up never completes its click).</summary>
        readonly List<(ResearchDefinition definition, VisualElement row, Label icon, Label name, Label status)> _rows = new List<(ResearchDefinition, VisualElement, Label, Label, Label)>();

        /// <summary>Queue buttons are rebuilt only when the queue's actual sequence changes (rare - a handful of times per session), for the same reason the main rows aren't rebuilt every frame.</summary>
        readonly List<ResearchDefinition> _lastRenderedQueue = new List<ResearchDefinition>();

        /// <summary>The research currently shown in the detail panel - defaults to the active one when the panel opens, otherwise whatever the player last clicked. Not necessarily the active research.</summary>
        ResearchDefinition _inspected;

        void Start()
        {
            // Start(), not OnEnable() - see BuildingMenuController for why (GameRuntime.Awake
            // ordering across objects is not guaranteed, Start() always runs after all Awakes).
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("ResearchPanelRoot");
            _reserveLabel = panelRoot.Q<Label>("ResearchReserveLabel");
            _list = panelRoot.Q<VisualElement>("ResearchList");
            _queueSection = panelRoot.Q<VisualElement>("ResearchQueueSection");
            _queueList = panelRoot.Q<VisualElement>("ResearchQueueList");

            _detailPanel = panelRoot.Q<VisualElement>("ResearchDetailPanel");
            _detailState = panelRoot.Q<Label>("ResearchDetailState");
            _detailName = panelRoot.Q<Label>("ResearchDetailName");
            _detailDescription = panelRoot.Q<Label>("ResearchDetailDescription");
            _detailEffect = panelRoot.Q<Label>("ResearchDetailEffect");
            _detailProgressFill = panelRoot.Q<VisualElement>("ResearchDetailProgressFill");
            _detailCostLabel = panelRoot.Q<Label>("ResearchDetailCostLabel");
            _detailTimeLabel = panelRoot.Q<Label>("ResearchDetailTimeLabel");
            _detailAbsorptionValue = panelRoot.Q<Label>("ResearchDetailAbsorptionValue");
            _detailPrerequisites = panelRoot.Q<VisualElement>("ResearchDetailPrerequisites");
            _detailActionButton = panelRoot.Q<Button>("ResearchDetailActionButton");
            _detailActionButton.clicked += OnDetailActionClicked;

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
            _inspected = gameRuntime.Research.GetActiveResearch();
            _lastRenderedQueue.Clear();
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

        /// <summary>Creates one row per research the database defines. Called when the panel opens, never per frame - see _rows.</summary>
        void BuildRows()
        {
            _list.Clear();
            _rows.Clear();

            IReadOnlyList<ResearchDefinition> all = gameRuntime.Researches != null ? gameRuntime.Researches.GetAll() : System.Array.Empty<ResearchDefinition>();
            foreach (ResearchDefinition definition in all)
            {
                if (definition == null) continue;

                ResearchDefinition captured = definition;
                var row = new VisualElement();
                row.AddToClassList("research-row");
                row.RegisterCallback<ClickEvent>(_ => OnRowClicked(captured));

                var icon = new Label();
                icon.AddToClassList("research-row-icon");
                row.Add(icon);

                var text = new VisualElement();
                text.AddToClassList("research-row-text");

                var name = new Label(definition.DisplayName);
                name.AddToClassList("research-row-name");
                text.Add(name);

                var sub = new Label(definition.Description);
                sub.AddToClassList("research-row-sub");
                text.Add(sub);

                row.Add(text);

                var status = new Label();
                status.AddToClassList("research-row-status");
                row.Add(status);

                _list.Add(row);
                _rows.Add((definition, row, icon, name, status));
            }
        }

        /// <summary>The only place that mutates ResearchSystem state from this panel's main list. Safe to call unconditionally on any row - ResearchSystem.Enqueue's own CanQueue guard makes it a no-op for a locked/active/already-queued/completed research.</summary>
        void OnRowClicked(ResearchDefinition definition)
        {
            _inspected = definition;
            gameRuntime.Research.Enqueue(definition);
        }

        void OnDetailActionClicked()
        {
            if (_inspected == null) return;

            ResearchSystem research = gameRuntime.Research;
            if (ReferenceEquals(_inspected, research.GetActiveResearch()))
            {
                research.CancelActive();
            }
            else if (research.GetQueue().Contains(_inspected))
            {
                research.Dequeue(_inspected);
            }
            else
            {
                research.Enqueue(_inspected);
            }
        }

        void Refresh()
        {
            ResearchSystem research = gameRuntime.Research;
            _reserveLabel.text = $"Reserve {Mathf.FloorToInt(gameRuntime.Compute.Reserve)} CU";

            HashSet<string> missingPrereqIds = MissingPrerequisiteIds(research, _inspected);

            foreach (var (definition, row, icon, name, status) in _rows)
            {
                RefreshRow(research, definition, row, icon, name, status, missingPrereqIds);
            }

            RefreshQueueList(research);
            RefreshDetailPanel(research);
        }

        static HashSet<string> MissingPrerequisiteIds(ResearchSystem research, ResearchDefinition inspected)
        {
            var missing = new HashSet<string>();
            if (inspected == null || research.ArePrerequisitesMet(inspected)) return missing;

            foreach (ResearchDefinition prerequisite in inspected.Prerequisites)
            {
                if (prerequisite != null && !research.IsUnlocked(prerequisite.Id)) missing.Add(prerequisite.Id);
            }
            return missing;
        }

        enum RowState { Completed, InProgress, Payable, Unaffordable, Locked }

        /// <summary>
        /// "Payable" vs "CU insuffisant" is a display-only distinction (reserve > 0 right now) -
        /// queuing/starting a research never requires CU up front (ResearchSystem.CanQueue has no
        /// reserve check), so this never blocks a click, it only tells the player what to expect.
        /// </summary>
        static RowState ResolveState(ResearchSystem research, ResearchDefinition definition, float currentReserve)
        {
            if (research.IsUnlocked(definition.Id)) return RowState.Completed;
            if (ReferenceEquals(definition, research.GetActiveResearch())) return RowState.InProgress;
            if (!research.ArePrerequisitesMet(definition)) return RowState.Locked;
            return currentReserve > 0f ? RowState.Payable : RowState.Unaffordable;
        }

        void RefreshRow(ResearchSystem research, ResearchDefinition definition, VisualElement row, Label icon, Label name, Label status, HashSet<string> missingPrereqIds)
        {
            RowState state = ResolveState(research, definition, gameRuntime.Compute.Reserve);

            row.RemoveFromClassList("research-state-completed");
            row.RemoveFromClassList("research-state-in-progress");
            row.RemoveFromClassList("research-state-payable");
            row.RemoveFromClassList("research-state-unaffordable");
            row.RemoveFromClassList("research-state-locked");
            row.EnableInClassList(StateClass(state), true);
            row.EnableInClassList("research-row-missing-prereq", missingPrereqIds.Contains(definition.Id));
            row.EnableInClassList("research-row-inspected", ReferenceEquals(definition, _inspected));

            icon.text = StateGlyph(state);

            switch (state)
            {
                case RowState.Completed:
                    status.text = "ACQUIS";
                    break;
                case RowState.InProgress:
                    status.text = $"{Mathf.FloorToInt(research.AbsorbedCu)} / {Mathf.CeilToInt(definition.CuCost)} CU";
                    break;
                default:
                    status.text = $"{Mathf.CeilToInt(definition.CuCost)} CU";
                    break;
            }
        }

        static string StateClass(RowState state) => state switch
        {
            RowState.Completed => "research-state-completed",
            RowState.InProgress => "research-state-in-progress",
            RowState.Payable => "research-state-payable",
            RowState.Unaffordable => "research-state-unaffordable",
            _ => "research-state-locked"
        };

        static string StateGlyph(RowState state) => state switch
        {
            RowState.Completed => "✓", // check
            RowState.InProgress => "◷", // progress ring
            RowState.Payable => "◆", // filled diamond
            RowState.Unaffordable => "◇", // hollow diamond
            _ => "\U0001F512" // lock
        };

        /// <summary>Rebuilds the reorderable queue list only when the queue's own sequence changed since last frame - same reasoning as _rows: these buttons must not be torn down mid-click.</summary>
        void RefreshQueueList(ResearchSystem research)
        {
            IReadOnlyList<ResearchDefinition> queue = research.GetQueue();
            bool changed = queue.Count != _lastRenderedQueue.Count;
            if (!changed)
            {
                for (int i = 0; i < queue.Count; i++)
                {
                    if (!ReferenceEquals(queue[i], _lastRenderedQueue[i])) { changed = true; break; }
                }
            }

            _queueSection.EnableInClassList("hidden", queue.Count == 0);
            if (!changed) return;

            _lastRenderedQueue.Clear();
            _lastRenderedQueue.AddRange(queue);

            _queueList.Clear();
            for (int i = 0; i < queue.Count; i++)
            {
                _queueList.Add(BuildQueueRow(research, queue[i], i, queue.Count));
            }
        }

        VisualElement BuildQueueRow(ResearchSystem research, ResearchDefinition definition, int index, int count)
        {
            var row = new VisualElement();
            row.AddToClassList("research-queue-row");

            var position = new Label((index + 1).ToString());
            position.AddToClassList("research-queue-position");
            row.Add(position);

            var name = new Label(definition.DisplayName);
            name.AddToClassList("research-queue-name");
            row.Add(name);

            var up = new Button(() => research.ReorderQueue(index, index - 1)) { text = "▲" };
            up.AddToClassList("research-queue-button");
            up.SetEnabled(index > 0);
            row.Add(up);

            var down = new Button(() => research.ReorderQueue(index, index + 1)) { text = "▼" };
            down.AddToClassList("research-queue-button");
            down.SetEnabled(index < count - 1);
            row.Add(down);

            var remove = new Button(() => research.Dequeue(definition)) { text = "✕" };
            remove.AddToClassList("research-queue-button");
            row.Add(remove);

            return row;
        }

        void RefreshDetailPanel(ResearchSystem research)
        {
            _detailPanel.EnableInClassList("hidden", _inspected == null);
            if (_inspected == null) return;

            bool isActive = ReferenceEquals(_inspected, research.GetActiveResearch());
            bool isQueued = research.GetQueue().Contains(_inspected);
            bool isCompleted = research.IsUnlocked(_inspected.Id);
            bool prerequisitesMet = research.ArePrerequisitesMet(_inspected);

            _detailState.text = isCompleted ? "ACQUIS" : isActive ? "EN COURS" : isQueued ? "EN FILE D'ATTENTE" : !prerequisitesMet ? "VERROUILLE" : "DISPONIBLE";
            _detailName.text = _inspected.DisplayName;
            _detailDescription.text = _inspected.Description;
            _detailEffect.text = _inspected.Description;

            float progress = isActive ? research.GetProgress() : isCompleted ? 1f : 0f;
            _detailProgressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            _detailCostLabel.text = isActive
                ? $"{Mathf.FloorToInt(research.AbsorbedCu)} / {Mathf.CeilToInt(_inspected.CuCost)} CU"
                : isCompleted ? "ACQUIS" : $"{Mathf.CeilToInt(_inspected.CuCost)} CU";
            _detailTimeLabel.text = isActive ? $"~{Mathf.CeilToInt(research.GetEstimatedSecondsRemaining())} s" : string.Empty;
            _detailAbsorptionValue.text = $"{Mathf.CeilToInt(_inspected.AbsorptionRatePerSecond)} CU/s";

            _detailPrerequisites.Clear();
            if (_inspected.Prerequisites.Count == 0)
            {
                var none = new Label("Aucun");
                none.AddToClassList("research-detail-prereq-none");
                _detailPrerequisites.Add(none);
            }
            else
            {
                foreach (ResearchDefinition prerequisite in _inspected.Prerequisites)
                {
                    if (prerequisite == null) continue;
                    bool met = research.IsUnlocked(prerequisite.Id);
                    var line = new Label((met ? "✓ " : "\U0001F512 ") + prerequisite.DisplayName);
                    line.AddToClassList(met ? "research-detail-prereq-met" : "research-detail-prereq-missing");
                    _detailPrerequisites.Add(line);
                }
            }

            if (isCompleted)
            {
                _detailActionButton.text = "ACQUIS";
                _detailActionButton.SetEnabled(false);
            }
            else if (isActive)
            {
                _detailActionButton.text = "ANNULER LA RECHERCHE";
                _detailActionButton.SetEnabled(true);
            }
            else if (isQueued)
            {
                _detailActionButton.text = "RETIRER DE LA FILE";
                _detailActionButton.SetEnabled(true);
            }
            else if (!prerequisitesMet)
            {
                _detailActionButton.text = "VERROUILLE";
                _detailActionButton.SetEnabled(false);
            }
            else
            {
                _detailActionButton.text = research.HasActiveResearch() ? "AJOUTER A LA FILE" : "LANCER";
                _detailActionButton.SetEnabled(true);
            }
        }
    }
}
