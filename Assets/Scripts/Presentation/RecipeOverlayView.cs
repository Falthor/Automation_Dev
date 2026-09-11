using System.Collections.Generic;
using Game.Gameplay.Buildings;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws what each machine is making, over the machine, while the player holds the view open.
    ///
    /// <b>A toggle, not a hold.</b> The key (<c>InputActionCatalogue.ShowRecipes</c>, Alt by
    /// default, reassignable like every other) turns it on and the same key turns it off. A player
    /// reading a base wants to keep reading it with both hands free.
    ///
    /// <b>An icon, not a label.</b> The output item's own sprite, which is the thing the player
    /// already recognises from the belts and the panels - and the project has no world-space text at
    /// all, so a label would mean bringing in a text stack for one overlay.
    ///
    /// <b>Only a machine with a recipe selected shows anything.</b> A Storage, a belt and an
    /// Extractor have no recipe to name; a machine with none selected yet is exactly the case worth
    /// seeing as empty rather than filled with a guess.
    ///
    /// Same shape as <see cref="ItemVisualSync"/>, deliberately: one pooled view per subject, built
    /// on first sight and dropped when its subject leaves, positioned in <c>LateUpdate</c> after the
    /// runtime has moved. Nothing here allocates per frame - the machines are walked by index over
    /// the transport registry rather than through an iterator, and the icons are only rebuilt when
    /// the recipe behind one changes.
    /// </summary>
    public sealed class RecipeOverlayView : MonoBehaviour
    {
        /// <summary>Found rather than wired when left empty, following BuildingSelectionInput's precedent: there is one GameRuntime in the scene, and a missing one only means this overlay never draws.</summary>
        [SerializeField] GameRuntime gameRuntime;

        [Tooltip("Taille de l'icône, en fraction d'une case.")]
        [SerializeField, Range(0.2f, 1.5f)] float iconScale = 0.6f;

        [Tooltip("Décalage vertical de l'icône, en cases, depuis le centre du bâtiment.")]
        [SerializeField, Range(-1f, 1f)] float verticalOffsetCells = 0f;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();

        /// <summary>One view per machine, with the recipe it was built for - so a machine switched to another recipe gets a new sprite and an unchanged one costs nothing.</summary>
        readonly Dictionary<ProductionBuildingRuntime, Icon> _icons = new Dictionary<ProductionBuildingRuntime, Icon>();
        readonly List<ProductionBuildingRuntime> _stale = new List<ProductionBuildingRuntime>();
        readonly HashSet<ProductionBuildingRuntime> _live = new HashSet<ProductionBuildingRuntime>();

        sealed class Icon
        {
            public GameObject Go;
            public SpriteRenderer Renderer;
            public string ItemId;
        }

        UnityEngine.InputSystem.InputAction _toggle;

        /// <summary>Whether the overlay is showing. Public so a test or a future menu entry can read it without a keypress.</summary>
        public bool IsShowing { get; private set; }

        void Start()
        {
            if (gameRuntime == null) gameRuntime = FindAnyObjectByType<GameRuntime>();
            _toggle = InputBindings.Find(InputActionCatalogue.ShowRecipes);
        }

        void LateUpdate()
        {
            if (InputBindings.WasPressedThisFrame(_toggle)) Toggle();

            if (!IsShowing || gameRuntime == null || gameRuntime.Grid == null || gameRuntime.Transport == null) return;

            _live.Clear();

            IReadOnlyList<BuildingRuntime> buildings = gameRuntime.Transport.NonBeltBuildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is ProductionBuildingRuntime machine)) continue;

                // A recipe's id is the item it produces - ProductionBuildingRuntime adds its output
                // under exactly that key - so the selected recipe names the icon directly, with no
                // trip through the recipe database.
                string recipeId = machine.GetSelectedRecipe();
                if (string.IsNullOrEmpty(recipeId)) continue;

                _live.Add(machine);
                Sync(machine, recipeId);
            }

            DropAllBut(_live);
        }

        void Toggle()
        {
            IsShowing = !IsShowing;
            if (!IsShowing) DropAllBut(null);
        }

        void Sync(ProductionBuildingRuntime machine, string itemId)
        {
            if (!_icons.TryGetValue(machine, out Icon icon))
            {
                var go = new GameObject("RecipeIcon");
                icon = new Icon { Go = go, Renderer = go.AddComponent<SpriteRenderer>() };
                _icons[machine] = icon;
            }

            if (icon.ItemId != itemId)
            {
                Sprite sprite = ItemSprite.For(gameRuntime.Items, _spriteFactory, itemId);
                icon.Renderer.sprite = sprite;
                icon.ItemId = itemId;

                // Imported icons carry their own pixel size, while the procedural placeholder is
                // exactly one world unit - normalise against the sprite's own bounds so every recipe
                // reads at the same size, as ItemVisualSync does for the same reason.
                float desired = gameRuntime.Grid.CellSize * iconScale;
                Vector2 native = sprite.bounds.size;
                icon.Go.transform.localScale = new Vector3(desired / native.x, desired / native.y, 1f);
            }

            Vector3 centre = gameRuntime.Grid.FootprintCenterToWorld(machine.Cell, machine.Definition.FootprintSize);
            icon.Go.transform.position = centre + new Vector3(0f, verticalOffsetCells * gameRuntime.Grid.CellSize, 0f);

            // Ranked like a building's own arrows - over the machine it belongs to, never under it -
            // and re-ranked every frame rather than registered with the ladder: the overlay comes and
            // goes with a keypress, and an entry per icon would outlive the icon.
            if (gameRuntime.DepthSort != null)
            {
                icon.Renderer.sortingOrder = gameRuntime.DepthSort.Order(
                    gameRuntime.Grid.CellToWorld(machine.Cell).y, SortingBands.SubOverlay);
            }
        }

        /// <summary>Destroys every icon whose machine is not in the given set - or every icon at all, when the overlay goes away.</summary>
        void DropAllBut(HashSet<ProductionBuildingRuntime> keep)
        {
            _stale.Clear();
            foreach (var kvp in _icons)
            {
                if (keep != null && keep.Contains(kvp.Key)) continue;
                _stale.Add(kvp.Key);
            }

            for (int i = 0; i < _stale.Count; i++)
            {
                Destroy(_icons[_stale[i]].Go);
                _icons.Remove(_stale[i]);
            }
        }
    }
}
