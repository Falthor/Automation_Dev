using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Sites;
using Game.Presentation;
using UnityEngine;
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
    /// makes available - arrived, reserved, missing - and it is the middle one that separates a site
    /// whose material is secured from one that has been forgotten because nothing in the base
    /// produces what it needs.
    ///
    /// <b>Two questions, two places.</b> The bars answer "must I produce?" and describe stock: what
    /// is committed to this site, whoever is or is not carrying it. The service line under the title
    /// answers "is this moving?" and describes the robots. Conflating them is what made four sites
    /// placed at once all claim delivery was under way when only one was being served - the material
    /// for all four was genuinely spoken for, but only one had a robot walking toward it.
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

        /// <summary>The builder robot's own sprite, so the service line's mark is the thing the player watches crossing the map.</summary>
        [SerializeField] Sprite robotIcon;

        /// <summary>
        /// The panel's accent, shared by the solid and the hatched segment on purpose: what tells
        /// them apart is the texture, not the hue. Matches the .site-bar-delivered rule in GameUI.uss
        /// - Painter2D takes a Color, so this one segment cannot read its own colour from USS.
        /// </summary>
        static readonly Color AccentColor = new Color(85f / 255f, 221f / 255f, 245f / 255f);

        /// <summary>Matches .site-service-waiting in GameUI.uss - Painter2D takes a Color, so the drawn clock cannot read its own from USS.</summary>
        static readonly Color WaitingColor = new Color(140f / 255f, 148f / 255f, 155f / 255f);

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly List<SupplyLine> _lines = new List<SupplyLine>();

        VisualElement _root;
        Label _title;
        VisualElement _progressFill;
        Label _percentLabel;
        VisualElement _supplyList;

        Label _serviceLabel;
        VisualElement _serviceRobotIcon;
        ClockGlyphElement _serviceClock;

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
            _percentLabel = panelRoot.Q<Label>("SitePercentLabel");
            _supplyList = panelRoot.Q<VisualElement>("SiteSupplyList");

            _serviceLabel = panelRoot.Q<Label>("SiteServiceLabel");
            _serviceRobotIcon = panelRoot.Q<VisualElement>("SiteServiceRobotIcon");
            if (robotIcon != null) _serviceRobotIcon.style.backgroundImage = new StyleBackground(robotIcon);

            // The clock is drawn, not imported - see ClockGlyphElement. Inserted beside the robot
            // icon rather than declared in the UXML, which cannot instantiate a custom element
            // without a UxmlFactory this one does not need.
            _serviceClock = new ClockGlyphElement { GlyphColor = WaitingColor };
            _serviceClock.AddToClassList("site-service-glyph");
            panelRoot.Q<VisualElement>("SiteServiceRow").Insert(0, _serviceClock);

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
            legend.Add(LegendEntry(new HatchFillElement { StripeColor = AccentColor }, "site-bar-reserved", "réservé"));
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

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.ContextualPanel))
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
            string name = definition != null ? definition.DisplayName : "Chantier";
            return site.Segments.Count > 1
                ? $"{name} ×{site.Segments.Count}"
                : name;
        }

        void Render()
        {
            _selected.GetSupply(_lines);

            RebuildSupplyList();

            int total = 0;
            int delivered = 0;
            foreach (SupplyLine line in _lines)
            {
                total += line.Total;
                delivered += line.Delivered;
            }

            float progress = total > 0 ? (float)delivered / total : 0f;
            _progressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            _percentLabel.text = $"{Mathf.RoundToInt(progress * 100f)} %";

            RenderServiceLine();
        }

        /// <summary>
        /// The panel's answer to "is this moving?", kept apart from the bars' answer to "must I
        /// produce?". Two questions the player asks separately, so two places - the bars describe the
        /// stock committed to this site, and this line describes what the robots are doing about it.
        /// Four sites placed at once have identical bars and only one of them says a robot is coming.
        /// </summary>
        void RenderServiceLine()
        {
            int approaching = RobotsApproaching(
                gameRuntime.ConstructionSites != null ? gameRuntime.ConstructionSites.Robots : null,
                _selected);

            bool served = approaching > 0;

            _serviceLabel.text = ServiceText(approaching);
            _serviceLabel.EnableInClassList("site-service-active", served);
            _serviceLabel.EnableInClassList("site-service-waiting", !served);

            _serviceRobotIcon.style.display = served ? DisplayStyle.Flex : DisplayStyle.None;
            _serviceClock.style.display = served ? DisplayStyle.None : DisplayStyle.Flex;
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

            var reserved = new HatchFillElement { StripeColor = AccentColor };
            reserved.AddToClassList("site-bar-reserved");
            reserved.style.width = new StyleLength(Length.Percent(FillPercent(line.Reserved, line.Total)));
            track.Add(reserved);

            // Whatever is missing is simply the track showing through: one less element per row, and
            // it cannot disagree with the other two about how much is left.
            return track;
        }

        /// <summary>
        /// "5 / 5" - what is secured out of what is needed. The numerator counts delivered AND
        /// reserved, so it is exactly what the bar shows as filled.
        ///
        /// Deliberately not "2 + 3 / 5". Splitting the numerator puts the same breakdown in two
        /// places, and the count is the wrong one of the two: the bar already separates solid from
        /// hatched, and it does it spatially. Worse, a split numerator breaks the correspondence -
        /// "3 / 8" under a bar filled end to end reads as a contradiction, and the reader has to work
        /// out which of the two is lying.
        /// </summary>
        public static string CountText(SupplyLine line)
            => $"{line.Delivered + line.Reserved} / {line.Total}";

        /// <summary>
        /// How many robots are physically on their way to this site right now, carrying for it.
        ///
        /// Only MovingToSite counts. A robot assigned to this site but heading to a chest is not
        /// approaching it - it is walking the other way - and a reservation is not a robot at all.
        /// That confusion is exactly what this line exists to end: four sites placed at once all
        /// have their material spoken for, and only one of them is being served.
        /// </summary>
        public static int RobotsApproaching(IReadOnlyList<BuilderRobotRuntime> robots, ConstructionSiteRuntime site)
        {
            if (robots == null || site == null) return 0;

            int count = 0;
            foreach (BuilderRobotRuntime robot in robots)
            {
                if (ReferenceEquals(robot.TargetSite, site) && robot.State == BuilderRobotState.MovingToSite) count++;
            }
            return count;
        }

        /// <summary>The service line's text: is anything moving toward this site at this instant, or is it waiting its turn?</summary>
        public static string ServiceText(int robotsApproaching)
            => robotsApproaching > 0
                ? $"{robotsApproaching} robot{(robotsApproaching > 1 ? "s" : "")} en approche"
                : "en attente d'un robot";

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
