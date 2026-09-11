using System.Collections.Generic;
using System.Globalization;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The Data Center inspector: what it produces, where that goes, the state of its hardware,
    /// what is in reserve, and when to replace. Read-only except for its three sliders, and it talks
    /// to the building only through its public contract - never a private field.
    ///
    /// <b>Five sections, in the order each one explains the next</b> (design/maquette-panneau-
    /// datacenter.png). Production first, and the real figure rather than the nominal one, because
    /// the chain the player cannot otherwise see is exactly that: the bays tire, the factor falls,
    /// the production follows. Showing the nominal made the headline figure immobile while
    /// everything underneath it moved.
    ///
    /// <b>Stability is drawn, not written.</b> A bay's number is its current draw; what matters is
    /// the width of the band it can land in, and that band widens as wear falls. Two bays side by
    /// side are then comparable without reading a digit - a new one keeps a narrow band with its
    /// tick pinned right, a worn one has an open band and a tick that visibly jumps on every roll,
    /// which is DataCenterRuntime.StabilityInterval - two seconds. That movement is the wear, seen
    /// before it is read, and it is why the interval was shortened from five: a tick that moves
    /// once every five seconds reads as frozen rather than unsteady.
    ///
    /// <b>The bay blocks are built once and updated in place.</b> The previous panel rebuilt its
    /// rows every frame; a block is ten elements, and up to eight bays of them every frame is a
    /// per-frame allocation this project does not allow. They are rebuilt only when the number of
    /// unlocked bays changes, which is twice per run at most.
    /// </summary>
    public sealed class DataCenterPanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;

        VisualElement _root;
        VisualElement _primingSection;
        VisualElement _primingFill;
        Label _primingLabel;

        Label _productionValue;
        Label _productionBreakdown;
        Label _powerLabel;

        Label _researchAxisValue;
        Label _buildingsAxisValue;
        Slider _axisSlider;

        /// <summary>The research share, drawn inside the axis slider's own tracker so the bar is the setting rather than a picture of it - see BuildAxisBar.</summary>
        VisualElement _axisResearchFill;

        Label _bayCount;
        VisualElement _bayList;
        VisualElement _spareList;

        Slider _cpuThresholdSlider;
        Label _cpuThresholdValue;
        Slider _memoryThresholdSlider;
        Label _memoryThresholdValue;

        VisualElement _explainOverlay;

        DataCenterRuntime _selected;

        /// <summary>True while a slider callback is itself pushing a value into the runtime - avoids the render pass immediately reading it back and fighting the slider's own drag gesture.</summary>
        bool _applyingSliderChange;

        readonly List<BayView> _bays = new List<BayView>();
        readonly List<SpareView> _spares = new List<SpareView>();

        /// <summary>How many bays the blocks were built for. Rebuilt when it moves, and only then.</summary>
        int _builtBayCount = -1;

        /// <summary>One bay's elements, kept so the values can be written without rebuilding anything.</summary>
        sealed class BayView
        {
            public VisualElement Frame;
            public VisualElement Icon;
            public Label Name;
            public Label Performance;
            public VisualElement RangeTrack;
            public VisualElement RangeBand;
            public VisualElement RangeTick;
            public VisualElement DetailRow;
            public Label Range;
            public Label Stability;
            public VisualElement WearRow;
            public Label WearValue;
            public VisualElement WearTrack;
            public VisualElement WearFill;
            public Label Alert;

            /// <summary>Which item this bay takes, for the spare-stock question a bay asks about itself.</summary>
            public string ItemId;
        }

        sealed class SpareView
        {
            public VisualElement Tile;
            public VisualElement Icon;
            public Label Count;
            public string ItemId;
        }

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("DataCenterPanelRoot");
            _primingSection = panelRoot.Q<VisualElement>("DataCenterPrimingSection");
            _primingFill = panelRoot.Q<VisualElement>("DataCenterPrimingFill");
            _primingLabel = panelRoot.Q<Label>("DataCenterPrimingLabel");

            _productionValue = panelRoot.Q<Label>("DataCenterProductionValue");
            _productionBreakdown = panelRoot.Q<Label>("DataCenterProductionBreakdown");
            _powerLabel = panelRoot.Q<Label>("DataCenterPowerLabel");

            _researchAxisValue = panelRoot.Q<Label>("DataCenterResearchAxisValue");
            _buildingsAxisValue = panelRoot.Q<Label>("DataCenterBuildingsAxisValue");
            _axisSlider = panelRoot.Q<Slider>("DataCenterAxisSlider");

            _bayCount = panelRoot.Q<Label>("DataCenterBayCount");
            _bayList = panelRoot.Q<VisualElement>("DataCenterBayList");
            _spareList = panelRoot.Q<VisualElement>("DataCenterSpareList");

            _cpuThresholdSlider = panelRoot.Q<Slider>("DataCenterCpuThresholdSlider");
            _cpuThresholdValue = panelRoot.Q<Label>("DataCenterCpuThresholdValue");
            _memoryThresholdSlider = panelRoot.Q<Slider>("DataCenterMemoryThresholdSlider");
            _memoryThresholdValue = panelRoot.Q<Label>("DataCenterMemoryThresholdValue");

            _explainOverlay = panelRoot.Q<VisualElement>("DataCenterExplainOverlay");

            if (powerIcon != null) panelRoot.Q<VisualElement>("DataCenterPowerIcon").style.backgroundImage = new StyleBackground(powerIcon);

            panelRoot.Q<Button>("DataCenterCloseButton").clicked += Close;
            panelRoot.Q<Button>("DataCenterExplainButton").clicked += () => ShowExplanation(true);
            panelRoot.Q<Button>("DataCenterExplainClose").clicked += () => ShowExplanation(false);

            BuildAxisBar();

            _axisSlider.RegisterValueChangedCallback(OnAxisSliderChanged);
            _cpuThresholdSlider.RegisterValueChangedCallback(OnCpuThresholdChanged);
            _memoryThresholdSlider.RegisterValueChangedCallback(OnMemoryThresholdChanged);

            BuildSpareTiles();

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy() => gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;

        /// <summary>
        /// The research share is drawn as a child of the slider's own tracker, sized every frame.
        ///
        /// The bar in the mockup has no separate handle: the colour boundary <i>is</i> the handle,
        /// which is what makes the control say what it does - "Recherche 2,4" on the left of a
        /// boundary that sits where the research share sits. A picture of the split beside a slider
        /// would be two things to keep in step, and the reason the old row read as neither.
        /// </summary>
        void BuildAxisBar()
        {
            VisualElement tracker = _axisSlider.Q("unity-tracker");
            if (tracker == null) return;

            _axisResearchFill = new VisualElement();
            _axisResearchFill.AddToClassList("dc-axis-fill-research");
            _axisResearchFill.pickingMode = PickingMode.Ignore;
            tracker.Add(_axisResearchFill);
        }

        void BuildSpareTiles()
        {
            _spareList.Clear();
            _spares.Clear();

            AddSpareTile(DataCenterRuntime.CpuItemId);
            AddSpareTile(DataCenterRuntime.MemoryItemId);
        }

        void AddSpareTile(string itemId)
        {
            var tile = new VisualElement();
            tile.AddToClassList("dc-spare-tile");

            var icon = new VisualElement();
            icon.AddToClassList("dc-spare-icon");
            SetItemIcon(icon, itemId);
            tile.Add(icon);

            var count = new Label("0");
            count.AddToClassList("dc-spare-count");
            tile.Add(count);

            _spareList.Add(tile);
            _spares.Add(new SpareView { Tile = tile, Icon = icon, Count = count, ItemId = itemId });
        }

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as DataCenterRuntime;
            _root.EnableInClassList("hidden", _selected == null);

            if (_selected == null)
            {
                ShowExplanation(false);
                return;
            }

            Render();
        }

        void Close() => gameRuntime.Selection.Clear();

        void ShowExplanation(bool shown) => _explainOverlay.EnableInClassList("hidden", !shown);

        bool ExplanationIsOpen => !_explainOverlay.ClassListContains("hidden");

        void OnAxisSliderChanged(ChangeEvent<float> evt)
        {
            if (_selected == null) return;
            _applyingSliderChange = true;
            _selected.SetResearchAxisShare(evt.newValue / 100f);
            _applyingSliderChange = false;
        }

        void OnCpuThresholdChanged(ChangeEvent<float> evt)
        {
            if (_selected == null) return;
            _applyingSliderChange = true;
            _selected.SetCpuReplacementThreshold(evt.newValue);
            _applyingSliderChange = false;
        }

        void OnMemoryThresholdChanged(ChangeEvent<float> evt)
        {
            if (_selected == null) return;
            _applyingSliderChange = true;
            _selected.SetMemoryReplacementThreshold(evt.newValue);
            _applyingSliderChange = false;
        }

        void Update()
        {
            if (_selected == null) return;

            // The explanation goes first: Escape puts away what is in front of the player, and the
            // panel behind it is not what they are looking at.
            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.ContextualPanel))
            {
                if (ExplanationIsOpen) ShowExplanation(false);
                else Close();
                return;
            }

            Render();
        }

        void Render()
        {
            bool isPriming = _selected.IsPriming;
            _primingSection.EnableInClassList("hidden", !isPriming);
            if (isPriming)
            {
                _primingFill.style.width = new StyleLength(Length.Percent(_selected.PrimingProgress * 100f));
                _primingLabel.text = $"~{Mathf.CeilToInt(_selected.GetPrimingSecondsRemaining())} s restantes";
            }

            RenderProduction();
            RenderAxes();
            RenderBays();
            RenderSpares();
            RenderThresholds();
        }

        void RenderProduction()
        {
            float research = _selected.GetResearchAxisProduction();
            float buildings = _selected.GetBuildingsAxisProduction();
            float real = research + buildings;
            float nominal = _selected.GetNominalComputeOutput();

            _productionValue.text = One(real);

            // One factor rather than two, and it is real/nominal - so it moves with the bays' own
            // condition as well as with the axis split. GetYield() alone would not: it is a function
            // of the split and nothing else, and a figure called "rendement" that does not budge
            // while the bays tire is the defect this panel exists to fix.
            float factor = nominal > 0f ? real / nominal : 0f;
            _productionBreakdown.text = $"nominal {One(nominal)} · rendement {Two(factor)}";

            _powerLabel.text = $"consomme {UnitFormat.Kilowatts(_selected.GetTotalPowerDemand())} kW";
        }

        void RenderAxes()
        {
            _researchAxisValue.text = One(_selected.GetResearchAxisProduction());
            _buildingsAxisValue.text = One(_selected.GetBuildingsAxisProduction());

            if (_axisResearchFill != null)
            {
                _axisResearchFill.style.width = new StyleLength(Length.Percent(_selected.ResearchAxisShare * 100f));
            }

            // A slider mid-drag must not be overwritten by the runtime's own value this same frame
            // (that would fight the pointer); every other frame keeps it in sync with whatever last
            // set the runtime value (e.g. a restored save).
            if (!_applyingSliderChange) _axisSlider.SetValueWithoutNotify(_selected.ResearchAxisShare * 100f);
        }

        void RenderThresholds()
        {
            if (!_applyingSliderChange)
            {
                _cpuThresholdSlider.SetValueWithoutNotify(_selected.CpuReplacementThresholdPercent);
                _memoryThresholdSlider.SetValueWithoutNotify(_selected.MemoryReplacementThresholdPercent);
            }

            _cpuThresholdValue.text = $"{Mathf.RoundToInt(_selected.CpuReplacementThresholdPercent)} %";
            _memoryThresholdValue.text = $"{Mathf.RoundToInt(_selected.MemoryReplacementThresholdPercent)} %";
        }

        void RenderSpares()
        {
            for (int i = 0; i < _spares.Count; i++)
            {
                SpareView spare = _spares[i];
                int count = _selected.GetInputAmount(spare.ItemId);
                bool empty = count <= 0;

                spare.Count.text = count.ToString(CultureInfo.InvariantCulture);
                spare.Tile.EnableInClassList("dc-spare-tile-empty", empty);
                spare.Count.EnableInClassList("dc-spare-count-empty", empty);
                spare.Icon.EnableInClassList("dc-spare-icon-empty", empty);
            }
        }

        void RenderBays()
        {
            IReadOnlyList<ComponentInstance> cpu = _selected.CpuSlots;
            IReadOnlyList<ComponentInstance> memory = _selected.MemorySlots;
            int total = cpu.Count + memory.Count;

            if (total != _builtBayCount) RebuildBays(cpu.Count, memory.Count);

            _bayCount.text = total == 1 ? "1 emplacement" : $"{total} emplacements";

            for (int i = 0; i < cpu.Count; i++) RenderBay(_bays[i], cpu[i], _selected.CpuReplacementThresholdPercent);
            for (int i = 0; i < memory.Count; i++) RenderBay(_bays[cpu.Count + i], memory[i], _selected.MemoryReplacementThresholdPercent);
        }

        /// <summary>
        /// One block per <b>unlocked</b> bay, CPU bays then Memory bays. A bay that does not exist
        /// yet is not drawn at all - an empty row for a locked extension would read as something the
        /// player could fill.
        /// </summary>
        void RebuildBays(int cpuCount, int memoryCount)
        {
            _bayList.Clear();
            _bays.Clear();

            for (int i = 0; i < cpuCount; i++) _bays.Add(AddBay(DataCenterRuntime.CpuItemId));
            for (int i = 0; i < memoryCount; i++) _bays.Add(AddBay(DataCenterRuntime.MemoryItemId));

            _builtBayCount = cpuCount + memoryCount;
        }

        BayView AddBay(string itemId)
        {
            var frame = new VisualElement();
            frame.AddToClassList("dc-bay");

            var head = new VisualElement();
            head.AddToClassList("dc-bay-head");

            var icon = new VisualElement();
            icon.AddToClassList("dc-bay-icon");
            SetItemIcon(icon, itemId);
            head.Add(icon);

            var name = new Label();
            name.AddToClassList("dc-bay-name");
            head.Add(name);

            var performance = new Label();
            performance.AddToClassList("dc-bay-performance");
            head.Add(performance);
            frame.Add(head);

            var track = new VisualElement();
            track.AddToClassList("dc-range-track");

            var band = new VisualElement();
            band.AddToClassList("dc-range-band");
            track.Add(band);

            var tick = new VisualElement();
            tick.AddToClassList("dc-range-tick");
            track.Add(tick);
            frame.Add(track);

            var underTrack = new VisualElement();
            underTrack.AddToClassList("dc-bay-row");

            var range = new Label();
            range.AddToClassList("dc-bay-range");
            underTrack.Add(range);

            var stability = new Label();
            stability.AddToClassList("dc-bay-stability");
            underTrack.Add(stability);
            frame.Add(underTrack);

            var wearRow = new VisualElement();
            wearRow.AddToClassList("dc-bay-row");

            var wearName = new Label("Usure");
            wearName.AddToClassList("dc-bay-wear-name");
            wearRow.Add(wearName);

            var wearValue = new Label();
            wearValue.AddToClassList("dc-bay-wear-value");
            wearRow.Add(wearValue);
            frame.Add(wearRow);

            var wearTrack = new VisualElement();
            wearTrack.AddToClassList("dc-wear-track");

            var wearFill = new VisualElement();
            wearFill.AddToClassList("dc-wear-fill");
            wearTrack.Add(wearFill);
            frame.Add(wearTrack);

            var alert = new Label();
            alert.AddToClassList("dc-bay-alert-line");
            frame.Add(alert);

            _bayList.Add(frame);

            return new BayView
            {
                Frame = frame,
                Icon = icon,
                Name = name,
                Performance = performance,
                RangeTrack = track,
                RangeBand = band,
                RangeTick = tick,
                DetailRow = underTrack,
                Range = range,
                Stability = stability,
                WearRow = wearRow,
                WearValue = wearValue,
                WearTrack = wearTrack,
                WearFill = wearFill,
                Alert = alert,
                ItemId = itemId
            };
        }

        void RenderBay(BayView view, ComponentInstance slot, float thresholdPercent)
        {
            bool hasSpare = _selected.GetInputAmount(view.ItemId) > 0;

            if (slot == null)
            {
                // <b>Hidden, not emptied.</b> A vacant bay used to draw an empty range track, an
                // empty wear bar and a row of blank numbers - three rails describing a component
                // that is not there, and the tallest block in the panel for the least information
                // in it. What is left says the whole of it: which bay, that it is empty, and whether
                // anything is coming.
                view.Name.text = "Emplacement vide";
                view.Performance.text = "—";
                ShowComponentRows(view, false);

                view.Alert.text = hasSpare ? "Installation en cours" : "Aucune pièce en stock";
                view.Alert.style.display = DisplayStyle.Flex;
                view.Frame.EnableInClassList("dc-bay-alert", !hasSpare);
                view.Frame.EnableInClassList("dc-bay-vacant", true);
                return;
            }

            view.Frame.EnableInClassList("dc-bay-vacant", false);
            ShowComponentRows(view, true);

            ItemDefinition item = gameRuntime.Items?.Get(slot.ItemId);
            view.Name.text = item != null && !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : slot.ItemId;

            // Zero while it is being replaced, because that is what it produces: EffectiveCu is
            // forced to 0 for those five seconds while EffectivePerformance keeps its last roll, and
            // showing the roll would have the bay reading 0,99 through the one moment it makes
            // nothing at all.
            float performance = slot.IsReplacing ? 0f : slot.EffectivePerformance;
            float floor = slot.FluctuationFloor;

            view.Performance.text = Two(performance);
            view.Range.text = $"{Two(floor)} – 1,00";
            view.Stability.text = $"stable {Mathf.RoundToInt(slot.Stability)} %";
            view.WearValue.text = $"{Mathf.RoundToInt(slot.Wear)} %";

            view.RangeBand.style.left = new StyleLength(Length.Percent(floor * 100f));
            view.RangeBand.style.width = new StyleLength(Length.Percent((1f - floor) * 100f));
            view.RangeTick.style.left = new StyleLength(Length.Percent(performance * 100f));
            view.WearFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(slot.Wear / 100f) * 100f));

            string alert = AlertFor(slot, hasSpare);
            view.Alert.text = alert;
            view.Alert.style.display = string.IsNullOrEmpty(alert) ? DisplayStyle.None : DisplayStyle.Flex;

            bool alerting = !string.IsNullOrEmpty(alert);
            view.Frame.EnableInClassList("dc-bay-alert", alerting);
            view.Performance.EnableInClassList("dc-value-alert", alerting);
            view.WearValue.EnableInClassList("dc-value-alert", alerting);
            view.WearFill.EnableInClassList("dc-wear-fill-alert", alerting);
        }

        /// <summary>Everything that describes an installed component: shown for a bay that has one, hidden for a bay that does not.</summary>
        static void ShowComponentRows(BayView view, bool shown)
        {
            DisplayStyle display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            view.RangeTrack.style.display = display;
            view.DetailRow.style.display = display;
            view.WearRow.style.display = display;
            view.WearTrack.style.display = display;
        }

        /// <summary>
        /// A bay at its threshold or without a spare behind it says so, and both at once when both
        /// are true - which is the case that matters, and the one a player should never have to
        /// assemble by comparing two sections.
        /// </summary>
        static string AlertFor(ComponentInstance slot, bool hasSpare)
        {
            if (slot.IsReplacing)
            {
                return hasSpare ? "Au seuil · remplacement en cours" : "Au seuil · aucune pièce en stock";
            }

            return hasSpare ? string.Empty : "Aucune pièce en stock";
        }

        void SetItemIcon(VisualElement element, string itemId)
        {
            Sprite icon = gameRuntime.Items?.Get(itemId)?.Icon;
            if (icon == null) icon = computeIcon;
            if (icon != null) element.style.backgroundImage = new StyleBackground(icon);
        }

        /// <summary>
        /// French decimal comma, built by hand rather than through a culture - which would depend on
        /// the machine's locale, as every other formatted figure in this project does.
        /// </summary>
        static string One(float value) => value.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

        static string Two(float value) => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
