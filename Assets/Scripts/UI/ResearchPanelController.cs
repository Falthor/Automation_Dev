using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Gameplay.Research;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Global Research panel (CONTRACTS.md §11/§12): the neural network of GDD §5.4, and the only
    /// research menu the game has. It takes the whole screen, brought in front of everything else
    /// when it opens. It does not exist before the Datacenter has finished priming - see
    /// IsAvailable - and the introduction runs on the Core's directives alone.
    ///
    /// The Datacenter sits at the centre, the cores of ResearchDatabase.GetCores() around it, and each
    /// research where its asset places it - on the ring of its tier, at its angle - placed by hand and
    /// never computed here (ResearchNetworkPlacement). A synapse is drawn
    /// only from a parent already acquired; toward a parent that is available but not acquired only a
    /// short stub leaves the node, and toward one locked further back nothing is drawn at all - there
    /// is no path, so there is none on screen. An unpowered core is linked by a dim dashed line.
    ///
    /// Clicking a node only inspects it: the detail panel's own button is what queues or cancels, so
    /// committing CU is always a deliberate second click. Clicking a prerequisite in the detail panel
    /// recentres the view on it; the inspected node's whole missing chain is outlined.
    ///
    /// Nothing is rebuilt per frame. Node classes, the synapses and the detail panel are redone only
    /// when the state signature changes; per frame, only the pulse moves and the live figures of the
    /// active research are rewritten when their whole value changes.
    /// </summary>
    public sealed class ResearchPanelController : MonoBehaviour
    {
        public const string PanelName = "research";

        /// <summary>Logical pixels between two rings - the radius is the tier.</summary>
        const float RingStep = 64f;

        const float CentreSize = 58f;
        const float CoreSize = 50f;
        const float NodeSize = 30f;
        const float NameWidth = 110f;
        const float StubLength = 22f;
        const float PulseSize = 8f;
        const float PulseSeconds = 1.6f;
        const float MinZoom = 0.4f;
        const float MaxZoom = 2f;
        const float WheelStep = 1.12f;
        const int CurveSamples = 20;

        static readonly Color RingColor = new Color32(28, 32, 41, 255);
        static readonly Color LitColor = new Color32(237, 147, 177, 255);
        static readonly Color AcquiredColor = new Color32(93, 202, 165, 255);
        static readonly Color StubColor = new Color32(61, 67, 79, 255);
        static readonly Color DarkColor = new Color32(42, 48, 58, 255);

        enum NodeState { Completed, InProgress, Payable, Unaffordable, Locked, CoreOn, CoreOff }

        /// <summary>Indexed by NodeState - one class per state, never distinguished by colour alone: each also has its own glyph.</summary>
        static readonly string[] StateClasses =
        {
            "research-node-completed", "research-node-in-progress", "research-node-payable",
            "research-node-unaffordable", "research-node-locked", "research-node-core-on",
            "research-node-core-off"
        };

        sealed class Node
        {
            /// <summary>The core or research on this node.</summary>
            public ResearchDefinition Definition;
            public bool IsCore;

            /// <summary>Offset from the Datacenter, before pan and zoom.</summary>
            public Vector2 Position;

            public VisualElement Element;
            public Label Glyph;
            public Label OffCaption;
            public NodeState State;
        }

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _panelRoot;
        VisualElement _root;
        Label _reserveLabel;
        VisualElement _network;
        VisualElement _content;
        VisualElement _pulse;
        VisualElement _queueSection;
        VisualElement _queueList;

        VisualElement _detailPanel;
        Label _detailState;
        Label _detailName;
        Label _detailDescription;
        VisualElement _detailResearchBody;
        Label _detailEffect;
        VisualElement _detailProgressFill;
        Label _detailCostLabel;
        Label _detailTimeLabel;
        Label _detailAbsorptionValue;
        VisualElement _detailPrerequisites;
        Button _detailActionButton;

        readonly List<Node> _nodes = new List<Node>();
        readonly Dictionary<ResearchDefinition, int> _indexOf = new Dictionary<ResearchDefinition, int>();

        /// <summary>Queue buttons are rebuilt only when the queue's actual sequence changes: a Button destroyed and recreated between pointer-down and pointer-up never completes its click.</summary>
        readonly List<ResearchDefinition> _lastRenderedQueue = new List<ResearchDefinition>();

        /// <summary>Every prerequisite the inspected research is still missing, all the way back - outlined on the network.</summary>
        readonly HashSet<ResearchDefinition> _missingChain = new HashSet<ResearchDefinition>();
        readonly Stack<ResearchDefinition> _chainWalk = new Stack<ResearchDefinition>();

        /// <summary>What the detail panel describes - defaults to the active research when the panel opens, otherwise whatever the player last clicked.</summary>
        ResearchDefinition _inspected;

        float _outerRadius;

        /// <summary>Half the side of the square the network is drawn in, centred on the Datacenter. Sized on what it holds, so picking reaches every node however many rings there are.</summary>
        float _extent = 400f;

        int _maxTier;
        bool _dirty = true;
        int _signature;
        int _shownReserve = int.MinValue;
        int _shownAbsorbed = int.MinValue;
        int _shownSeconds = int.MinValue;
        int _pulseNode = -1;
        int _pulseParent = -1;

        Vector2 _pan;
        float _zoom = 1f;
        bool _fitPending;
        bool _dragging;
        int _dragPointer = -1;

        /// <summary>
        /// Whether the research menu exists yet: once any core is powered. The Datacenter powers the
        /// Research and Buildings cores when its priming is done (DataCenterRuntime.ResearchCoreId),
        /// so before that there is no Top Bar card, no Bottom Nav icon and nothing to open.
        /// </summary>
        public static bool IsAvailable(ResearchDatabase database, ResearchSystem research)
        {
            if (database == null || research == null) return false;

            IReadOnlyList<ResearchDefinition> cores = database.GetCores();
            for (int i = 0; i < cores.Count; i++)
            {
                if (cores[i] != null && research.IsUnlocked(cores[i].Id)) return true;
            }
            return false;
        }

        void Start()
        {
            // Start(), not OnEnable() - see BuildingMenuController for why (GameRuntime.Awake
            // ordering across objects is not guaranteed, Start() always runs after all Awakes).
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;
            _panelRoot = panelRoot;

            _root = panelRoot.Q<VisualElement>("ResearchPanelRoot");
            _reserveLabel = panelRoot.Q<Label>("ResearchReserveLabel");
            _network = panelRoot.Q<VisualElement>("ResearchNetwork");
            _content = panelRoot.Q<VisualElement>("ResearchNetworkContent");
            _queueSection = panelRoot.Q<VisualElement>("ResearchQueueSection");
            _queueList = panelRoot.Q<VisualElement>("ResearchQueueList");

            _detailPanel = panelRoot.Q<VisualElement>("ResearchDetailPanel");
            _detailState = panelRoot.Q<Label>("ResearchDetailState");
            _detailName = panelRoot.Q<Label>("ResearchDetailName");
            _detailDescription = panelRoot.Q<Label>("ResearchDetailDescription");
            _detailResearchBody = panelRoot.Q<VisualElement>("ResearchDetailResearchBody");
            _detailEffect = panelRoot.Q<Label>("ResearchDetailEffect");
            _detailProgressFill = panelRoot.Q<VisualElement>("ResearchDetailProgressFill");
            _detailCostLabel = panelRoot.Q<Label>("ResearchDetailCostLabel");
            _detailTimeLabel = panelRoot.Q<Label>("ResearchDetailTimeLabel");
            _detailAbsorptionValue = panelRoot.Q<Label>("ResearchDetailAbsorptionValue");
            _detailPrerequisites = panelRoot.Q<VisualElement>("ResearchDetailPrerequisites");
            _detailActionButton = panelRoot.Q<Button>("ResearchDetailActionButton");
            _detailActionButton.clicked += OnDetailActionClicked;

            panelRoot.Q<Button>("ResearchCloseButton").clicked += Hide;
            panelRoot.Q<Button>("ResearchRecenterButton").clicked += FitView;

            _content.generateVisualContent += DrawSynapses;

            _network.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _network.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _network.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _network.RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);
            _network.RegisterCallback<WheelEvent>(OnWheel);
            _network.RegisterCallback<GeometryChangedEvent>(_ => { if (_fitPending) FitView(); });

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
            if (panelName != PanelName)
            {
                _dragging = false;
                return;
            }

            // Over the Top Bar, the Bottom Nav and every other panel: the whole screen is the network's.
            _panelRoot.BringToFront();

            if (_nodes.Count == 0) BuildNetwork();
            _inspected = gameRuntime.Research.GetActiveResearch();
            _lastRenderedQueue.Clear();
            _queueList.Clear();
            _dirty = true;
            FitView();
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

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.GlobalPanel))
            {
                Hide();
                return;
            }

            Refresh();
            MovePulse();
        }

        // --- Building the network (once) ---

        /// <summary>Creates every node from the database, each where its asset places it (ResearchNetworkPlacement). The tree is static data, so this runs the first time the panel opens and never again.</summary>
        void BuildNetwork()
        {
            _content.Clear();
            _nodes.Clear();
            _indexOf.Clear();

            ResearchDatabase database = gameRuntime.Researches;
            IReadOnlyList<ResearchDefinition> cores = database != null ? database.GetCores() : System.Array.Empty<ResearchDefinition>();
            IReadOnlyList<ResearchDefinition> researches = database != null ? database.GetAll() : System.Array.Empty<ResearchDefinition>();

            // How far the drawing reaches: the furthest node, its half-size and the name under it.
            _maxTier = 1;
            _outerRadius = CentreSize;
            for (int i = 0; i < cores.Count + researches.Count; i++)
            {
                bool isCore = i < cores.Count;
                ResearchDefinition definition = isCore ? cores[i] : researches[i - cores.Count];
                if (definition == null || _indexOf.ContainsKey(definition)) continue;

                var node = new Node { Definition = definition, IsCore = isCore, Position = ScreenPosition(definition) };
                _indexOf[definition] = _nodes.Count;
                _nodes.Add(node);
                _maxTier = Mathf.Max(_maxTier, definition.Tier);
                _outerRadius = Mathf.Max(_outerRadius, node.Position.magnitude + CoreSize * 0.5f + 30f);
            }

            // The square the network lives in, centred on the network area. Its own centre is the
            // Datacenter, which is also the default transform-origin - so zoom scales about it.
            _extent = _outerRadius + 60f;
            _content.style.width = 2f * _extent;
            _content.style.height = 2f * _extent;
            _content.style.marginLeft = -_extent;
            _content.style.marginTop = -_extent;

            // The Datacenter: not a research and not clickable - the network grows out of it.
            AddElement(Vector2.zero, CentreSize, "research-node-centre", "Datacenter MK1", nameInside: true, clickable: false, out _);

            foreach (Node node in _nodes)
            {
                if (node.IsCore)
                {
                    node.Element = AddElement(node.Position, CoreSize, "research-node-core", node.Definition.DisplayName, nameInside: true, clickable: true, out _);
                    node.OffCaption = Caption("non alimente", CoreSize, "research-node-caption");
                    node.Element.Add(node.OffCaption);
                }
                else
                {
                    node.Element = AddElement(node.Position, NodeSize, "research-node-research", node.Definition.DisplayName, nameInside: false, clickable: true, out node.Glyph);
                }

                ResearchDefinition captured = node.Definition;
                node.Element.RegisterCallback<ClickEvent>(_ => _inspected = captured);
            }

            _pulse = new VisualElement();
            _pulse.AddToClassList("research-pulse");
            _pulse.pickingMode = PickingMode.Ignore;
            _pulse.style.width = PulseSize;
            _pulse.style.height = PulseSize;
            _content.Add(_pulse);
        }

        /// <summary>A node's offset from the Datacenter on screen: its stored polar position, y flipped - the panel is y down.</summary>
        static Vector2 ScreenPosition(ResearchDefinition definition)
        {
            Vector2 offset = ResearchNetworkPlacement.Offset(definition.Tier, definition.Angle, RingStep);
            return new Vector2(offset.x, -offset.y);
        }

        VisualElement AddElement(Vector2 position, float size, string kindClass, string name, bool nameInside, bool clickable, out Label glyph)
        {
            var element = new VisualElement();
            element.AddToClassList("research-node");
            element.AddToClassList(kindClass);
            element.style.left = _extent + position.x - size * 0.5f;
            element.style.top = _extent + position.y - size * 0.5f;
            element.style.width = size;
            element.style.height = size;
            float radius = size * 0.5f;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
            element.pickingMode = clickable ? PickingMode.Position : PickingMode.Ignore;

            glyph = null;
            if (nameInside)
            {
                var inside = new Label(name);
                inside.AddToClassList("research-node-name-inside");
                inside.pickingMode = PickingMode.Ignore;
                element.Add(inside);
            }
            else
            {
                glyph = new Label();
                glyph.AddToClassList("research-node-glyph");
                glyph.pickingMode = PickingMode.Ignore;
                element.Add(glyph);
                if (!string.IsNullOrEmpty(name)) element.Add(Caption(name, size, "research-node-name"));
            }

            _content.Add(element);
            return element;
        }

        /// <summary>A label centred under a node of the given size - its name, or a core's "non alimente".</summary>
        static Label Caption(string text, float size, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            label.pickingMode = PickingMode.Ignore;
            label.style.left = (size - NameWidth) * 0.5f;
            label.style.top = size + 3f;
            label.style.width = NameWidth;
            return label;
        }

        // --- Per-frame refresh ---

        void Refresh()
        {
            ResearchSystem research = gameRuntime.Research;
            float reserve = gameRuntime.Compute.Reserve;

            int shownReserve = Mathf.FloorToInt(reserve);
            if (shownReserve != _shownReserve)
            {
                _shownReserve = shownReserve;
                _reserveLabel.text = $"Reserve {shownReserve} CU";
            }

            int signature = 17;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Node node = _nodes[i];
                node.State = ResolveState(research, node, reserve);
                signature = signature * 31 + (int)node.State;
            }
            signature = signature * 31 + (_inspected != null && _indexOf.TryGetValue(_inspected, out int inspectedIndex) ? inspectedIndex + 1 : 0);
            signature = signature * 31 + research.GetQueue().Count;
            signature = signature * 31 + (research.HasActiveResearch() ? 1 : 0);

            if (_dirty || signature != _signature)
            {
                _dirty = false;
                _signature = signature;
                RebuildMissingChain(research);
                ApplyNodeStates();
                FindPulseEdge(research);
                _content.MarkDirtyRepaint();
                RebuildDetail(research);
            }

            RefreshQueueList(research);
            RefreshLiveDetail(research);
        }

        /// <summary>
        /// "Payable" vs "CU insuffisant" is display only (reserve above zero right now): queuing never
        /// requires CU up front, so it never blocks a click, it only tells the player what to expect.
        /// </summary>
        static NodeState ResolveState(ResearchSystem research, Node node, float reserve)
        {
            if (node.IsCore) return research.IsUnlocked(node.Definition.Id) ? NodeState.CoreOn : NodeState.CoreOff;
            if (research.IsUnlocked(node.Definition.Id)) return NodeState.Completed;
            if (ReferenceEquals(node.Definition, research.GetActiveResearch())) return NodeState.InProgress;
            if (!research.ArePrerequisitesMet(node.Definition)) return NodeState.Locked;
            return reserve > 0f ? NodeState.Payable : NodeState.Unaffordable;
        }

        static string Glyph(NodeState state) => state switch
        {
            NodeState.Completed => "✓",
            NodeState.InProgress => "◷",
            NodeState.Payable => "◆",
            NodeState.Unaffordable => "◇",
            _ => "\U0001F512"
        };

        void ApplyNodeStates()
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                Node node = _nodes[i];
                for (int c = 0; c < StateClasses.Length; c++) node.Element.EnableInClassList(StateClasses[c], c == (int)node.State);

                bool inspected = node.Definition != null && ReferenceEquals(node.Definition, _inspected);
                node.Element.EnableInClassList("research-node-inspected", inspected);
                node.Element.EnableInClassList("research-node-missing-prereq", node.Definition != null && _missingChain.Contains(node.Definition));

                if (node.Glyph != null) node.Glyph.text = Glyph(node.State);
                if (node.OffCaption != null) node.OffCaption.EnableInClassList("hidden", node.State != NodeState.CoreOff);
            }
        }

        void RebuildMissingChain(ResearchSystem research)
        {
            _missingChain.Clear();
            if (_inspected == null || research.ArePrerequisitesMet(_inspected)) return;

            _chainWalk.Clear();
            _chainWalk.Push(_inspected);
            while (_chainWalk.Count > 0)
            {
                IReadOnlyList<ResearchDefinition> prerequisites = _chainWalk.Pop().Prerequisites;
                for (int i = 0; i < prerequisites.Count; i++)
                {
                    ResearchDefinition prerequisite = prerequisites[i];
                    if (prerequisite == null || research.IsUnlocked(prerequisite.Id) || !_missingChain.Add(prerequisite)) continue;
                    _chainWalk.Push(prerequisite);
                }
            }
        }

        /// <summary>The synapse the active research is drawing through: from its first acquired parent on the network. None when nothing is active, or the active one is not on the network.</summary>
        void FindPulseEdge(ResearchSystem research)
        {
            _pulseNode = -1;
            _pulseParent = -1;

            ResearchDefinition active = research.GetActiveResearch();
            if (active != null && _indexOf.TryGetValue(active, out int index))
            {
                IReadOnlyList<ResearchDefinition> prerequisites = active.Prerequisites;
                for (int i = 0; i < prerequisites.Count; i++)
                {
                    ResearchDefinition prerequisite = prerequisites[i];
                    if (prerequisite == null || !research.IsUnlocked(prerequisite.Id) || !_indexOf.TryGetValue(prerequisite, out int parent)) continue;
                    _pulseNode = index;
                    _pulseParent = parent;
                    break;
                }
            }

            _pulse?.EnableInClassList("hidden", _pulseNode < 0);
        }

        /// <summary>The one thing that moves every frame: a point of light travelling the synapse of the research in progress.</summary>
        void MovePulse()
        {
            if (_pulseNode < 0) return;

            Vector2 from = _nodes[_pulseParent].Position;
            Vector2 to = _nodes[_pulseNode].Position;
            Vector2 point = Bezier(from, Control(from, to), to, Mathf.Repeat(Time.time / PulseSeconds, 1f));
            _pulse.style.left = _extent + point.x - PulseSize * 0.5f;
            _pulse.style.top = _extent + point.y - PulseSize * 0.5f;
        }

        // --- Synapses ---

        void DrawSynapses(MeshGenerationContext context)
        {
            if (_nodes.Count == 0) return;

            ResearchSystem research = gameRuntime.Research;
            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            var origin = new Vector2(_extent, _extent);

            painter.strokeColor = RingColor;
            painter.lineWidth = 1f;
            for (int tier = 1; tier <= _maxTier; tier++) Circle(painter, origin, tier * RingStep);

            for (int i = 0; i < _nodes.Count; i++)
            {
                Node node = _nodes[i];
                Vector2 to = origin + node.Position;

                if (node.IsCore)
                {
                    if (node.State == NodeState.CoreOn) Curve(painter, origin, to, LitColor, 2f, dashed: false);
                    else Curve(painter, origin, to, DarkColor, 1.5f, dashed: true);
                    continue;
                }

                IReadOnlyList<ResearchDefinition> prerequisites = node.Definition.Prerequisites;
                for (int p = 0; p < prerequisites.Count; p++)
                {
                    ResearchDefinition prerequisite = prerequisites[p];
                    if (prerequisite == null || !_indexOf.TryGetValue(prerequisite, out int parentIndex)) continue;

                    Vector2 from = origin + _nodes[parentIndex].Position;
                    if (research.IsUnlocked(prerequisite.Id))
                    {
                        Curve(painter, from, to, node.State == NodeState.Completed ? AcquiredColor : LitColor, 2f, dashed: false);
                    }
                    else if (!_nodes[parentIndex].IsCore && research.ArePrerequisitesMet(prerequisite))
                    {
                        Stub(painter, to, from);
                    }
                    // A parent locked further back, or an unpowered core: no path, so nothing drawn.
                }
            }
        }

        static void Curve(Painter2D painter, Vector2 from, Vector2 to, Color color, float width, bool dashed)
        {
            painter.strokeColor = color;
            painter.lineWidth = width;
            Vector2 control = Control(from, to);

            painter.BeginPath();
            if (!dashed)
            {
                painter.MoveTo(from);
                painter.QuadraticCurveTo(control, to);
            }
            else
            {
                for (int s = 0; s < CurveSamples; s += 2)
                {
                    painter.MoveTo(Bezier(from, control, to, s / (float)CurveSamples));
                    painter.LineTo(Bezier(from, control, to, (s + 1) / (float)CurveSamples));
                }
            }
            painter.Stroke();
        }

        /// <summary>A short floating stub leaving the node toward a parent that is available but not acquired yet.</summary>
        static void Stub(Painter2D painter, Vector2 node, Vector2 towards)
        {
            Vector2 direction = (towards - node).normalized;
            painter.strokeColor = StubColor;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.MoveTo(node + direction * (NodeSize * 0.5f));
            painter.LineTo(node + direction * (NodeSize * 0.5f + StubLength));
            painter.Stroke();
        }

        static void Circle(Painter2D painter, Vector2 centre, float radius)
        {
            const int segments = 96;
            painter.BeginPath();
            painter.MoveTo(centre + new Vector2(radius, 0f));
            for (int s = 1; s <= segments; s++)
            {
                float angle = s * (2f * Mathf.PI / segments);
                painter.LineTo(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            painter.Stroke();
        }

        /// <summary>A gentle, always same-side bend - the synapses are curves, not spokes.</summary>
        static Vector2 Control(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            return (from + to) * 0.5f + new Vector2(-delta.y, delta.x) * 0.12f;
        }

        static Vector2 Bezier(Vector2 from, Vector2 control, Vector2 to, float t)
        {
            float u = 1f - t;
            return u * u * from + 2f * u * t * control + t * t * to;
        }

        // --- Pan and zoom ---

        void OnPointerDown(PointerDownEvent evt)
        {
            // Only the background drags: a node, the queue and the recentre button keep their clicks.
            if (evt.button != 0 || (evt.target != _network && evt.target != _content)) return;

            _dragging = true;
            _dragPointer = evt.pointerId;
            _network.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging || evt.pointerId != _dragPointer) return;

            _pan += new Vector2(evt.deltaPosition.x, evt.deltaPosition.y);
            ApplyView();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging || evt.pointerId != _dragPointer) return;

            _dragging = false;
            _network.ReleasePointer(evt.pointerId);
        }

        /// <summary>Zooms about the cursor: the point under it stays under it.</summary>
        void OnWheel(WheelEvent evt)
        {
            float zoom = Mathf.Clamp(evt.delta.y > 0f ? _zoom / WheelStep : _zoom * WheelStep, MinZoom, MaxZoom);
            Vector2 fromCentre = evt.localMousePosition - _network.contentRect.center;
            Vector2 underCursor = (fromCentre - _pan) / _zoom;

            _zoom = zoom;
            _pan = fromCentre - underCursor * _zoom;
            ApplyView();
            evt.StopPropagation();
        }

        /// <summary>The whole network in view, centred on the Datacenter. Deferred until the area has a size - it has none the frame the panel opens.</summary>
        void FitView()
        {
            Rect area = _network.contentRect;
            if (float.IsNaN(area.width) || area.width < 1f || area.height < 1f)
            {
                _fitPending = true;
                return;
            }

            _fitPending = false;
            _zoom = Mathf.Clamp(Mathf.Min(area.width, area.height) / (2f * _outerRadius), MinZoom, 1f);
            _pan = Vector2.zero;
            ApplyView();
        }

        void FocusOn(ResearchDefinition definition)
        {
            if (definition == null || !_indexOf.TryGetValue(definition, out int index)) return;

            _pan = -_nodes[index].Position * _zoom;
            ApplyView();
        }

        void ApplyView()
        {
            _content.style.translate = new Translate(_pan.x, _pan.y);
            _content.style.scale = new Scale(new Vector3(_zoom, _zoom, 1f));
        }

        // --- Detail panel ---

        void OnDetailActionClicked()
        {
            if (_inspected == null || (_indexOf.TryGetValue(_inspected, out int index) && _nodes[index].IsCore)) return;

            ResearchSystem research = gameRuntime.Research;
            bool changed;
            if (ReferenceEquals(_inspected, research.GetActiveResearch()))
            {
                research.CancelActive();
                changed = true;
            }
            else if (research.GetQueue().Contains(_inspected)) changed = research.Dequeue(_inspected);
            else changed = research.Enqueue(_inspected);

            if (changed) gameRuntime.NotePlayerAction();
        }

        /// <summary>Everything in the detail panel that only changes with the state - redone on a signature change, never per frame.</summary>
        void RebuildDetail(ResearchSystem research)
        {
            _shownAbsorbed = int.MinValue;
            _shownSeconds = int.MinValue;
            _detailPanel.EnableInClassList("hidden", _inspected == null);
            if (_inspected == null) return;

            bool isCore = _indexOf.TryGetValue(_inspected, out int index) && _nodes[index].IsCore;
            _detailName.text = _inspected.DisplayName;
            _detailDescription.text = _inspected.Description;
            _detailResearchBody.EnableInClassList("hidden", isCore);

            if (isCore)
            {
                _detailState.text = research.IsUnlocked(_inspected.Id) ? "ALIMENTE" : "NON ALIMENTE";
                return;
            }

            bool isActive = ReferenceEquals(_inspected, research.GetActiveResearch());
            bool isQueued = research.GetQueue().Contains(_inspected);
            bool isCompleted = research.IsUnlocked(_inspected.Id);
            bool prerequisitesMet = research.ArePrerequisitesMet(_inspected);

            _detailState.text = isCompleted ? "ACQUIS" : isActive ? "EN COURS" : isQueued ? "EN FILE D'ATTENTE" : !prerequisitesMet ? "VERROUILLE" : "DISPONIBLE";
            _detailEffect.text = _inspected.Description;

            float progress = isActive ? research.GetProgress() : isCompleted ? 1f : 0f;
            _detailProgressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            if (!isActive)
            {
                _detailCostLabel.text = isCompleted ? "ACQUIS" : $"{Mathf.CeilToInt(_inspected.CuCost)} CU";
                _detailTimeLabel.text = string.Empty;
            }
            _detailAbsorptionValue.text = $"{Mathf.CeilToInt(_inspected.AbsorptionRatePerSecond)} CU/s";

            _detailPrerequisites.Clear();
            IReadOnlyList<ResearchDefinition> prerequisites = _inspected.Prerequisites;
            if (prerequisites.Count == 0)
            {
                var none = new Label("Aucun");
                none.AddToClassList("research-detail-prereq-none");
                _detailPrerequisites.Add(none);
            }
            for (int i = 0; i < prerequisites.Count; i++)
            {
                ResearchDefinition prerequisite = prerequisites[i];
                if (prerequisite == null) continue;

                bool met = research.IsUnlocked(prerequisite.Id);
                var line = new Label((met ? "✓ " : "\U0001F512 ") + prerequisite.DisplayName);
                line.AddToClassList(met ? "research-detail-prereq-met" : "research-detail-prereq-missing");
                line.AddToClassList("research-detail-prereq-link");
                line.RegisterCallback<ClickEvent>(_ => FocusOn(prerequisite));
                _detailPrerequisites.Add(line);
            }

            if (isCompleted) SetAction("ACQUIS", false);
            else if (isActive) SetAction("ANNULER LA RECHERCHE", true);
            else if (isQueued) SetAction("RETIRER DE LA FILE", true);
            else if (!prerequisitesMet) SetAction("VERROUILLE", false);
            else SetAction(research.HasActiveResearch() ? "AJOUTER A LA FILE" : "LANCER", true);
        }

        void SetAction(string text, bool enabled)
        {
            _detailActionButton.text = text;
            _detailActionButton.SetEnabled(enabled);
        }

        /// <summary>The only detail figures that move while nothing changes state: the active research's progress. Rewritten when their whole value changes, not per frame.</summary>
        void RefreshLiveDetail(ResearchSystem research)
        {
            if (_inspected == null || !ReferenceEquals(_inspected, research.GetActiveResearch())) return;

            int absorbed = Mathf.FloorToInt(research.AbsorbedCu);
            if (absorbed != _shownAbsorbed)
            {
                _shownAbsorbed = absorbed;
                _detailCostLabel.text = $"{absorbed} / {Mathf.CeilToInt(_inspected.CuCost)} CU";
                _detailProgressFill.style.width = new StyleLength(Length.Percent(research.GetProgress() * 100f));
            }

            int seconds = Mathf.CeilToInt(research.GetEstimatedSecondsRemaining());
            if (seconds != _shownSeconds)
            {
                _shownSeconds = seconds;
                _detailTimeLabel.text = $"~{seconds} s";
            }
        }

        // --- Queue ---

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

            var up = new Button(() => { if (research.ReorderQueue(index, index - 1)) gameRuntime.NotePlayerAction(); }) { text = "▲" };
            up.AddToClassList("research-queue-button");
            up.SetEnabled(index > 0);
            row.Add(up);

            var down = new Button(() => { if (research.ReorderQueue(index, index + 1)) gameRuntime.NotePlayerAction(); }) { text = "▼" };
            down.AddToClassList("research-queue-button");
            down.SetEnabled(index < count - 1);
            row.Add(down);

            var remove = new Button(() => { if (research.Dequeue(definition)) gameRuntime.NotePlayerAction(); }) { text = "✕" };
            remove.AddToClassList("research-queue-button");
            row.Add(remove);

            return row;
        }
    }
}
