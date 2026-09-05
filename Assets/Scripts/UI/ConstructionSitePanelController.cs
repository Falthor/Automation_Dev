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
            bool anyStalled = false;
            foreach (SupplyLine line in _lines)
            {
                total += line.Total;
                delivered += line.Delivered;
                if (line.IsStalled) anyStalled = true;
            }

            float progress = total > 0 ? (float)delivered / total : 0f;
            _progressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            _percentLabel.text = $"{Mathf.RoundToInt(progress * 100f)} %";

            _stateLabel.text = anyStalled ? "● MATERIAUX MANQUANTS" : "● LIVRAISON EN COURS";
            _stateLabel.RemoveFromClassList("state-producing");
            _stateLabel.RemoveFromClassList("state-blocked");
            _stateLabel.AddToClassList(anyStalled ? "state-blocked" : "state-producing");
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

        VisualElement BuildSupplyRow(SupplyLine line)
        {
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
            head.Add(name);

            var totalLabel = new Label(line.Total.ToString());
            totalLabel.AddToClassList("site-supply-total");
            head.Add(totalLabel);

            row.Add(head);

            var counts = new VisualElement();
            counts.AddToClassList("site-supply-counts");
            counts.Add(Count($"{line.Delivered} {Plural(line.Delivered, "arrivé")}", "site-count-arrived", line.Delivered));
            counts.Add(Separator());
            counts.Add(Count($"{line.EnRoute} en route", "site-count-enroute", line.EnRoute));
            counts.Add(Separator());
            counts.Add(Count($"{line.Missing} {Plural(line.Missing, "manquant")}", "site-count-missing", line.Missing));
            row.Add(counts);

            return row;
        }

        /// <summary>A zero keeps its place in the row - the columns have to line up down the list - but loses its colour, so the eye lands on the counts that carry meaning.</summary>
        static Label Count(string text, string styleClass, int value)
        {
            var label = new Label(text);
            label.AddToClassList(styleClass);
            if (value == 0) label.AddToClassList("site-count-zero");
            return label;
        }

        static Label Separator()
        {
            var label = new Label("·");
            label.AddToClassList("site-supply-separator");
            return label;
        }

        static string Plural(int amount, string word) => amount > 1 ? word + "s" : word;

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
