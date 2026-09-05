using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Sites;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

using SupplyLine = Game.Gameplay.Sites.ConstructionSiteRuntime.SupplyLine;

namespace Game.UI
{
    /// <summary>
    /// The panel behind a construction site's blue silhouette: the same frame, dock and header as
    /// every per-building panel, with a supply where they have a production.
    ///
    /// It exists because "delivered 10 of 15" does not answer the question a player asks of a site
    /// that is taking too long. Each ingredient is shown in the three states the reservation counter
    /// makes available - arrived, en route, missing - and it is the middle one that separates a site
    /// the robots are actively serving from one that has been forgotten because nothing in the base
    /// produces what it needs. On a delivered count alone those two look identical right up until
    /// one of them silently never finishes.
    ///
    /// Reacts to SelectionRuntime.SelectedSite, the same way ExtractorPanelController reacts to
    /// SelectedBuilding (CONTRACTS.md §7). When the site finishes it hands over to the panel of the
    /// building that has just come into existence rather than closing - see LeaveFinishedSite.
    /// </summary>
    public sealed class ConstructionSitePanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        /// <summary>The building-to-panel router, borrowed for the handover when a site finishes. Optional: without it a finished site simply closes, as before.</summary>
        [SerializeField] BuildingSelectionInput selectionRouter;

        /// <summary>
        /// The panel's accent, shared by the solid and the hatched segment on purpose: what tells
        /// them apart is the texture, not the hue. Matches the .site-bar-delivered rule in GameUI.uss
        /// - Painter2D takes a Color, so this one segment cannot read its own colour from USS.
        /// </summary>
        static readonly Color AccentColor = new Color(85f / 255f, 221f / 255f, 245f / 255f);

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly List<SupplyLine> _lines = new List<SupplyLine>();

        VisualElement _root;
        Label _title;
        VisualElement _progressFill;
        Label _stateLabel;
        Label _percentLabel;
        VisualElement _supplyList;

        ConstructionSiteRuntime _selected;

        void Start()
        {
            // Start(), not OnEnable() - see BuildingMenuController for why (GameRuntime.Awake
            // ordering across objects is not guaranteed, Start() always runs after all Awakes).
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("ConstructionSitePanelRoot");
            _title = panelRoot.Q<Label>("SiteTitle");
            _progressFill = panelRoot.Q<VisualElement>("SiteProgressFill");
            _stateLabel = panelRoot.Q<Label>("SiteStateLabel");
            _percentLabel = panelRoot.Q<Label>("SitePercentLabel");
            _supplyList = panelRoot.Q<VisualElement>("SiteSupplyList");

            panelRoot.Q<Button>("SiteCloseButton").clicked += Close;

            BuildLegend(panelRoot.Q<VisualElement>("SiteLegend"));

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SiteSelectionChanged += OnSiteSelectionChanged;
        }

        void OnDestroy()
        {
            if (gameRuntime != null) gameRuntime.Selection.SiteSelectionChanged -= OnSiteSelectionChanged;
        }

        void OnSiteSelectionChanged(ConstructionSiteRuntime site)
        {
            _selected = site;
            _root.EnableInClassList("hidden", _selected == null);
            if (_selected == null) return;

            _title.text = TitleFor(_selected);
            Render();
        }

        /// <summary>
        /// Built once, in code rather than in the UXML, because the hatched swatch has to be the same
        /// element the bars use - a legend drawn by any other means would be free to stop matching
        /// what it explains.
        /// </summary>
        static void BuildLegend(VisualElement legend)
        {
            if (legend == null) return;

            legend.Add(LegendEntry(new VisualElement(), "site-bar-delivered", "arrivé"));
            legend.Add(LegendEntry(new HatchFillElement { StripeColor = AccentColor }, "site-bar-enroute", "en route"));
            legend.Add(LegendEntry(new VisualElement(), "site-legend-missing", "manquant"));
        }

        static VisualElement LegendEntry(VisualElement swatch, string swatchClass, string text)
        {
            var entry = new VisualElement();
            entry.AddToClassList("site-legend-entry");

            swatch.AddToClassList("site-legend-swatch");
            swatch.AddToClassList(swatchClass);
            entry.Add(swatch);

            var label = new Label(text);
            label.AddToClassList("site-legend-label");
            entry.Add(label);

            return entry;
        }

        void Close() => gameRuntime.Selection.Clear();

        void Update()
        {
            if (_selected == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (!IsStillPending(_selected))
            {
                LeaveFinishedSite(_selected);
                return;
            }

            Render();
        }

        /// <summary>
        /// A site leaves the queue two ways, and they are the same event only from here. <b>Cancelled</b>,
        /// there is nothing left on that ground and closing is the whole of the right answer.
        /// <b>Finished</b>, the player is looking at a building that has just come into existence
        /// under their cursor, in the place they were already watching - and a panel vanishing there
        /// would be the single discontinuity in a sequence built entirely out of continuity:
        /// silhouette, dissolve, finished view. So the panel hands over to the building's own.
        ///
        /// IsComplete is what tells the two apart: cancelling frees the segments that were never
        /// built, so a cancelled site is by construction one whose segments did not all materialize.
        ///
        /// Not every finished site has somewhere to hand over to - a dragged run is N belts, and a
        /// belt has no panel - and that question is not answered here. It belongs to the one router
        /// that already maps a building to its panel, which answers it for a click and for this.
        /// </summary>
        void LeaveFinishedSite(ConstructionSiteRuntime site)
        {
            if (site.IsComplete && site.Segments.Count == 1 && selectionRouter != null
                && selectionRouter.TryShowPanelFor(site.Segments[0]))
            {
                // The router moved the selection on, which cleared the site slot and closed this
                // panel on the way - there is deliberately nothing left to do here.
                return;
            }

            Close();
        }

        bool IsStillPending(ConstructionSiteRuntime site)
        {
            if (gameRuntime.ConstructionSites == null) return false;

            foreach (ConstructionSiteRuntime pending in gameRuntime.ConstructionSites.Sites)
            {
                if (ReferenceEquals(pending, site)) return true;
            }
            return false;
        }

        /// <summary>A dragged run is one site of many segments, so it says so - the player placed a run, not a belt.</summary>
        static string TitleFor(ConstructionSiteRuntime site)
        {
            BuildingDefinition definition = site.PrimaryDefinition;
            string name = definition != null ? definition.DisplayName : "CHANTIER";
            return site.Segments.Count > 1
                ? $"{name.ToUpperInvariant()} ×{site.Segments.Count}"
                : name.ToUpperInvariant();
        }

        void Render()
        {
            _selected.GetSupply(_lines);

            RebuildSupplyList();

            int total = 0;
            int delivered = 0;
            bool anyMissing = false;
            foreach (SupplyLine line in _lines)
            {
                total += line.Total;
                delivered += line.Delivered;
                if (IsAlarming(line)) anyMissing = true;
            }

            float progress = total > 0 ? (float)delivered / total : 0f;
            _progressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            _percentLabel.text = $"{Mathf.RoundToInt(progress * 100f)} %";

            // Deliberately the same rule the rows are coloured by. A red ingredient under a headline
            // saying deliveries are under way would have the panel contradicting itself.
            _stateLabel.text = anyMissing ? "● MATERIAUX MANQUANTS" : "● LIVRAISON EN COURS";
            _stateLabel.RemoveFromClassList("state-producing");
            _stateLabel.RemoveFromClassList("state-blocked");
            _stateLabel.AddToClassList(anyMissing ? "state-blocked" : "state-producing");
        }

        /// <summary>
        /// Rebuilt wholesale each frame rather than diffed: the bill has one row per distinct
        /// ingredient - two or three in practice, and GetSupply guarantees a stable order - so
        /// there is nothing here that a diff would save.
        /// </summary>
        void RebuildSupplyList()
        {
            _supplyList.Clear();
            foreach (SupplyLine line in _lines)
            {
                _supplyList.Add(BuildSupplyRow(line));
            }
        }

        /// <summary>
        /// One ingredient: a label line (name left, count right) over a bar filled left to right in
        /// the order things actually arrive - solid for what is here, hatched for what is coming,
        /// bare track for what nothing has been found for.
        /// </summary>
        VisualElement BuildSupplyRow(SupplyLine line)
        {
            bool alarming = IsAlarming(line);

            var row = new VisualElement();
            row.AddToClassList("site-supply-row");

            var head = new VisualElement();
            head.AddToClassList("site-supply-head");

            var icon = new VisualElement();
            icon.AddToClassList("site-supply-icon");
            icon.style.backgroundImage = new StyleBackground(ItemSprite(line.ItemId));
            head.Add(icon);

            var name = new Label(ItemName(line.ItemId));
            name.AddToClassList("site-supply-name");
            name.EnableInClassList("site-supply-alarming", alarming);
            head.Add(name);

            var count = new Label(CountText(line));
            count.AddToClassList("site-supply-count");
            count.EnableInClassList("site-supply-alarming", alarming);
            head.Add(count);

            row.Add(head);
            row.Add(BuildSupplyBar(line));

            return row;
        }

        VisualElement BuildSupplyBar(SupplyLine line)
        {
            var track = new VisualElement();
            track.AddToClassList("site-bar-track");

            var delivered = new VisualElement();
            delivered.AddToClassList("site-bar-delivered");
            delivered.style.width = new StyleLength(Length.Percent(FillPercent(line.Delivered, line.Total)));
            track.Add(delivered);

            var enRoute = new HatchFillElement { StripeColor = AccentColor };
            enRoute.AddToClassList("site-bar-enroute");
            enRoute.style.width = new StyleLength(Length.Percent(FillPercent(line.EnRoute, line.Total)));
            track.Add(enRoute);

            // Whatever is missing is simply the track showing through: one less element per row, and
            // it cannot disagree with the other two about how much is left.
            return track;
        }

        /// <summary>
        /// "10 + 20 / 40" - I have ten, twenty are coming, I need forty. With nothing in flight the
        /// addition would be noise, so it collapses to "0 / 30".
        /// </summary>
        public static string CountText(SupplyLine line)
            => line.EnRoute > 0
                ? $"{line.Delivered} + {line.EnRoute} / {line.Total}"
                : $"{line.Delivered} / {line.Total}";

        /// <summary>
        /// Whether this ingredient is the player's problem. The useful split is not between arrived
        /// and en route - it is between "the system is handling this" and "I have to produce it" -
        /// so a bar filled end to end means do nothing, whether it is solid or hatched.
        ///
        /// Hence <b>Missing &gt; 0</b>, never Delivered &lt; Total: an ingredient with nothing
        /// delivered and everything reserved is fully handled and must not be flagged.
        /// </summary>
        public static bool IsAlarming(SupplyLine line) => line.Missing > 0;

        /// <summary>Share of the bar one count occupies, 0-100. A free building (no cost at all) fills nothing rather than dividing by zero.</summary>
        public static float FillPercent(int amount, int total)
            => total <= 0 ? 0f : Mathf.Clamp(100f * amount / total, 0f, 100f);

        Sprite ItemSprite(string itemId)
        {
            ItemDefinition item = gameRuntime.Items != null ? gameRuntime.Items.Get(itemId) : null;
            if (item != null && item.Icon != null) return item.Icon;
            return _spriteFactory.CreateSolidSquareSprite(item != null ? item.FallbackColor : Color.magenta);
        }

        string ItemName(string itemId)
        {
            ItemDefinition item = gameRuntime.Items != null ? gameRuntime.Items.Get(itemId) : null;
            return item != null && !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : itemId;
        }
    }
}
