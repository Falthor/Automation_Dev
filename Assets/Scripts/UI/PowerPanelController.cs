using System.Collections.Generic;
using System.Globalization;
using Game.Data;
using Game.Gameplay.Power;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Global Power panel (CONTRACTS.md §9/§12), opened from the Top Bar's Power card. Two tabs:
    ///
    /// <b>Courbes</b> - demand/production/balance, the saturation bar and the 5-minute history
    /// graph (sampled every 5s). This was the whole panel before; it became a tab rather than
    /// moving.
    ///
    /// <b>Priorité</b> - the order in which building types receive power, and the one thing that
    /// makes the order decidable: a line drawn where the running total of demand crosses
    /// production. Above it, served; below it, stopped. The player drags a handle and the line
    /// moves as they drag, so nothing has to be calculated.
    ///
    /// <b>Every type is listed, built or not</b> (the player's call): a type with no instances
    /// shows no draw and no state, and still holds the place it was given - which is what stops the
    /// list reshuffling itself the day one is built. The rows come from
    /// <c>GameRuntime.PowerPriority</c>, which is seeded from the building catalogue, so a type
    /// added to the game appears here without this file being touched.
    ///
    /// <b>The partial row is measured, not divided.</b> "1 sur 2" is how many instances actually
    /// drew (<c>PowerSystem.ServedInstancesOf</c>); dividing an allocation by a per-instance figure
    /// would be wrong for a group whose instances differ, and the Data Center is one.
    /// </summary>
    public sealed class PowerPanelController : MonoBehaviour
    {
        public const string PanelName = "power";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        const float SampleInterval = 5f;
        static readonly Color GraphColorA = new Color(0.333f, 0.867f, 0.961f, 1f);
        static readonly Color GraphColorB = new Color(0.91f, 0.66f, 0.24f, 1f);

        static readonly Color DeficitColor = new Color(0.949f, 0.325f, 0.325f, 1f);
        static readonly Color NormalColor = new Color(0.843f, 0.878f, 0.898f, 1f);

        VisualElement _root;
        Label _consumption;
        Label _production;
        Label _balance;
        VisualElement _barFill;
        HistoryGraphElement _graph;
        float _sampleTimer;

        Button _curvesTab;
        Button _priorityTab;
        VisualElement _curvesBody;
        VisualElement _priorityBody;

        Label _headerProduction;
        Label _headerDemand;
        Label _headerDeficit;
        VisualElement _list;
        VisualElement _cutLine;

        /// <summary>One row per building type, built once and updated in place - and kept alive across a drag, which is why the cut line is drawn over the list rather than inserted into it.</summary>
        readonly List<GroupRow> _rows = new List<GroupRow>();

        /// <summary>Instances per definition id, refilled every frame into this same dictionary rather than a new one.</summary>
        readonly Dictionary<string, int> _instanceCounts = new Dictionary<string, int>();

        /// <summary>What the rows were built for. Rebuilt when the set of types changes, which is never during a run.</summary>
        int _builtRowCount = -1;

        /// <summary>The row being dragged, or null. While it is set, Refresh leaves the order alone - the pointer owns it.</summary>
        GroupRow _dragging;
        float _dragStartY;
        int _dragStartIndex;

        /// <summary>Row height in pixels, and the whole of the drag's arithmetic: an offset of one row moves the group by one place. Matches .pr-row's height in GameUI.uss, and a test would not catch a disagreement - the layout would.</summary>
        const float RowHeight = 22f;

        sealed class GroupRow
        {
            public VisualElement Root;
            public VisualElement Handle;
            public Label Name;
            public Label Count;
            public Label Draw;
            public Label State;
            public string TypeId;
        }

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("PowerPanelRoot");
            _consumption = panelRoot.Q<Label>("PowerConsumptionLabel");
            _production = panelRoot.Q<Label>("PowerProductionLabel");
            _balance = panelRoot.Q<Label>("PowerBalanceLabel");
            _barFill = panelRoot.Q<VisualElement>("PowerBarFill");
            panelRoot.Q<Button>("PowerCloseButton").clicked += Hide;

            _curvesTab = panelRoot.Q<Button>("PowerCurvesTab");
            _priorityTab = panelRoot.Q<Button>("PowerPriorityTab");
            _curvesBody = panelRoot.Q<VisualElement>("PowerCurvesBody");
            _priorityBody = panelRoot.Q<VisualElement>("PowerPriorityBody");
            _curvesTab.clicked += () => ShowPriority(false);
            _priorityTab.clicked += () => ShowPriority(true);

            _headerProduction = panelRoot.Q<Label>("PowerPriorityProduction");
            _headerDemand = panelRoot.Q<Label>("PowerPriorityDemand");
            _headerDeficit = panelRoot.Q<Label>("PowerPriorityDeficit");
            _list = panelRoot.Q<VisualElement>("PowerPriorityList");
            _cutLine = panelRoot.Q<VisualElement>("PowerPriorityCutLine");

            _graph = new HistoryGraphElement(GraphColorA, GraphColorB);
            panelRoot.Q<VisualElement>("PowerGraphContainer").Add(_graph);
            Sample();

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName) => _root.EnableInClassList("hidden", panelName != PanelName);

        void Hide()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            gameRuntime.Selection.CloseGlobalPanel();
        }

        void Update()
        {
            // Sampling runs regardless of whether the panel is open, matching the source
            // project's power_panel.gd - the history keeps accumulating in the background.
            _sampleTimer += Time.deltaTime;
            if (_sampleTimer >= SampleInterval)
            {
                _sampleTimer = 0f;
                Sample();
            }

            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.GlobalPanel))
            {
                Hide();
                return;
            }

            Refresh();
            if (PriorityIsShowing) RefreshPriority();
        }

        void Sample() => _graph.AddSample(gameRuntime.Power.SettledSupply, gameRuntime.Power.SettledDemand);

        void ShowPriority(bool priority)
        {
            _curvesBody.EnableInClassList("hidden", priority);
            _priorityBody.EnableInClassList("hidden", !priority);
            _curvesTab.EnableInClassList("tab-button-active", !priority);
            _priorityTab.EnableInClassList("tab-button-active", priority);
        }

        bool PriorityIsShowing => !_priorityBody.ClassListContains("hidden");

        // ---- The priority tab ----

        void RefreshPriority()
        {
            var power = gameRuntime.Power;
            PowerPriorityOrder order = gameRuntime.PowerPriority;
            if (order == null) return;

            float supply = power.SettledSupply;
            float demand = power.SettledDemand;
            float deficit = supply - demand;

            _headerProduction.text = Whole(supply);
            _headerDemand.text = Whole(demand);
            _headerDeficit.text = deficit < 0f ? Whole(deficit) : "+" + Whole(deficit);

            _headerDemand.EnableInClassList("pr-value-warn", demand > supply);
            _headerDeficit.EnableInClassList("pr-value-warn", deficit < 0f);

            if (_dragging == null) SyncRows(order);

            gameRuntime.Transport?.CountByDefinition(_instanceCounts);

            int cutIndex = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                GroupRow row = _rows[i];
                float groupDemand = power.DemandOf(row.TypeId);
                int asking = power.AskingInstancesOf(row.TypeId);
                int servedInstances = power.ServedInstancesOf(row.TypeId);

                int instances = _instanceCounts.TryGetValue(row.TypeId, out int count) ? count : 0;
                row.Count.text = instances > 0 ? "×" + instances.ToString(CultureInfo.InvariantCulture) : "–";
                row.Draw.text = groupDemand > 0f ? Whole(groupDemand) + " kW" : "–";

                bool stopped = groupDemand > 0f && servedInstances == 0;
                bool partial = servedInstances > 0 && servedInstances < asking;

                if (groupDemand <= 0f) row.State.text = string.Empty;
                else if (stopped) row.State.text = "à l'arrêt";
                else if (partial) row.State.text = $"{servedInstances} sur {asking}";
                else row.State.text = "alimenté";

                row.State.EnableInClassList("pr-state-stopped", stopped);
                row.State.EnableInClassList("pr-state-partial", partial);
                row.State.EnableInClassList("pr-state-served", !stopped && !partial && groupDemand > 0f);
                row.Root.EnableInClassList("pr-row-stopped", stopped);

                // The line goes above the first group that asked for power and got none of it. A
                // partially served group stays above it: it is where the production ran out, not
                // where it had already run out.
                if (cutIndex < 0 && stopped) cutIndex = i;
            }

            bool showLine = cutIndex >= 0;
            _cutLine.EnableInClassList("hidden", !showLine);
            if (showLine) _cutLine.style.top = new StyleLength(cutIndex * RowHeight - 1f);
        }

        /// <summary>
        /// Brings the rows in line with the order - built on the first pass, and reordered in place
        /// afterwards rather than rebuilt, because a rebuild during a drag would destroy the element
        /// holding the pointer.
        /// </summary>
        void SyncRows(PowerPriorityOrder order)
        {
            IReadOnlyList<string> ids = order.Order;

            if (ids.Count != _builtRowCount)
            {
                BuildRows(ids);
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (_rows[i].TypeId == ids[i]) continue;
                BuildRows(ids);
                return;
            }
        }

        void BuildRows(IReadOnlyList<string> ids)
        {
            for (int i = 0; i < _rows.Count; i++) _rows[i].Root.RemoveFromHierarchy();
            _rows.Clear();

            for (int i = 0; i < ids.Count; i++)
            {
                GroupRow row = BuildRow(ids[i]);
                if (row != null) _rows.Add(row);
            }

            _builtRowCount = ids.Count;
        }

        GroupRow BuildRow(string typeId)
        {
            BuildingDefinition definition = gameRuntime.FindBuildingDefinitionById(typeId);
            if (definition == null) return null;

            var root = new VisualElement();
            root.AddToClassList("pr-row");

            var handle = new VisualElement();
            handle.AddToClassList("pr-handle");
            root.Add(handle);

            var name = new Label(string.IsNullOrEmpty(definition.DisplayName) ? typeId : definition.DisplayName);
            name.AddToClassList("pr-row-name");
            root.Add(name);

            var count = new Label();
            count.AddToClassList("pr-row-count");
            root.Add(count);

            var draw = new Label();
            draw.AddToClassList("pr-row-draw");
            root.Add(draw);

            var state = new Label();
            state.AddToClassList("pr-row-state");
            root.Add(state);

            _list.Add(root);

            var built = new GroupRow
            {
                Root = root, Handle = handle, Name = name,
                Count = count, Draw = draw, State = state, TypeId = typeId
            };

            handle.RegisterCallback<PointerDownEvent>(evt => BeginDrag(built, evt));
            handle.RegisterCallback<PointerMoveEvent>(evt => Drag(built, evt));
            handle.RegisterCallback<PointerUpEvent>(evt => EndDrag(built, evt));

            return built;
        }

        void BeginDrag(GroupRow row, PointerDownEvent evt)
        {
            _dragging = row;
            _dragStartY = evt.position.y;
            _dragStartIndex = _rows.IndexOf(row);
            row.Root.AddToClassList("pr-row-dragging");
            row.Handle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        /// <summary>
        /// Moves the group as the pointer travels: one row of offset is one place. The model is
        /// updated live rather than on release, which is what makes the cut line move while the
        /// player drags - the whole reason this screen is worth having.
        /// </summary>
        void Drag(GroupRow row, PointerMoveEvent evt)
        {
            if (_dragging != row) return;

            int target = _dragStartIndex + Mathf.RoundToInt((evt.position.y - _dragStartY) / RowHeight);
            target = Mathf.Clamp(target, 0, _rows.Count - 1);

            int current = _rows.IndexOf(row);
            if (target == current) return;

            _rows.RemoveAt(current);
            _rows.Insert(target, row);

            // The element moves with it, and stays the same element - the pointer is captured on its
            // handle, and a rebuilt row would drop the capture mid-gesture.
            row.Root.RemoveFromHierarchy();
            _list.Insert(target + 1, row.Root);   // +1: the cut line is the list's first child

            gameRuntime.PowerPriority.MoveTo(row.TypeId, target);
            evt.StopPropagation();
        }

        void EndDrag(GroupRow row, PointerUpEvent evt)
        {
            if (_dragging != row) return;

            row.Root.RemoveFromClassList("pr-row-dragging");
            row.Handle.ReleasePointer(evt.pointerId);
            _dragging = null;
            _builtRowCount = _rows.Count;
            evt.StopPropagation();
        }

        static string Whole(float value) => Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);

        void Refresh()
        {
            var power = gameRuntime.Power;
            float supply = power.SettledSupply;
            float demand = power.SettledDemand;
            bool deficit = demand > supply;
            float balance = supply - demand;
            string sign = balance >= 0f ? "+" : "";

            _consumption.text = $"Demande : {Mathf.RoundToInt(demand)} kW";
            _production.text = $"Production : {Mathf.RoundToInt(supply)} kW";
            _balance.text = $"Bilan : {sign}{Mathf.RoundToInt(balance)} kW";
            _balance.style.color = deficit ? DeficitColor : NormalColor;

            float ratio = supply > 0f ? Mathf.Clamp01(demand / supply) : 0f;
            _barFill.style.width = new StyleLength(Length.Percent(ratio * 100f));
        }
    }
}
