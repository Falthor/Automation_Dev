using System.Collections.Generic;

namespace Game.Presentation
{
    /// <summary>One reassignable action: its name in the binding table, and how it is named to the player.</summary>
    public readonly struct InputActionInfo
    {
        /// <summary>The action's name in <c>InputSystem_Actions.inputactions</c>. An identifier, never shown.</summary>
        public readonly string Name;

        /// <summary>What the player reads. French, like the rest of the interface.</summary>
        public readonly string Label;

        /// <summary>The heading it appears under. Actions sharing one appear consecutively, so the array's order is the display order.</summary>
        public readonly string Section;

        public InputActionInfo(string name, string label, string section)
        {
            Name = name;
            Label = label;
            Section = section;
        }
    }

    /// <summary>
    /// Every reassignable action the game has, in the order a menu should list them.
    ///
    /// <b>Two files, two different facts, and a test that they agree.</b> The
    /// <c>.inputactions</c> asset says which key an action is on; this says which actions exist and
    /// what to call them in French. The asset format has nowhere to put a label, so the names appear
    /// in both places - which is the one duplication here that could not be designed away.
    /// <c>InputActionCatalogueTests</c> asserts the two sets are exactly equal, in both directions,
    /// so an action added to one and forgotten in the other fails the suite rather than showing up as
    /// a blank row or a missing one.
    ///
    /// <b>What is deliberately absent.</b> Nothing driven by the mouse is here: the buttons and the
    /// wheel are not reassignable, so they have no row to offer. And the intro and Genesis screens
    /// answer to any key at all, which is not a binding.
    ///
    /// <b>The camera and the map share their four keys rather than owning four each.</b> They always
    /// read the same physical keys, in two separate literal copies - exactly the duplication the
    /// inventory flagged. One set of actions read by both consumers means rebinding "vers le nord"
    /// moves both, which is what a player who rebinds it means, and the menu shows four rows instead
    /// of eight identical ones.
    /// </summary>
    public static class InputActionCatalogue
    {
        // Names, as the asset spells them. Consumers use these rather than string literals so that a
        // typo is a compile error instead of an action that silently never fires.
        public const string PanNorth = "PanNorth";
        public const string PanSouth = "PanSouth";
        public const string PanEast = "PanEast";
        public const string PanWest = "PanWest";

        public const string Rotate = "Rotate";
        public const string MoveInputSide = "MoveInputSide";
        public const string DropDragAxis = "DropDragAxis";

        public const string BuildingMenu = "BuildingMenu";
        public const string Pause = "Pause";
        public const string Close = "Close";
        public const string ShowRecipes = "ShowRecipes";

        /// <summary>Toolbar slots are numbered from 1, matching the digit they sit on and the label the player reads.</summary>
        public static string Slot(int oneBased) => "Slot" + oneBased;

        public const string CameraAndMapSection = "Caméra et carte";
        public const string ConstructionSection = "Construction";
        public const string InterfaceSection = "Interface";
        public const string ToolbarSection = "Barre d'outils";

        /// <summary>
        /// The list, in display order. A <c>static readonly</c> array whose contents are never
        /// mutated after initialisation, which is what DEVELOPMENT_RULES §5 allows - exposed as a
        /// read-only list so a caller cannot make that untrue.
        /// </summary>
        public static readonly IReadOnlyList<InputActionInfo> All = Build();

        static InputActionInfo[] Build()
        {
            var all = new List<InputActionInfo>
            {
                new InputActionInfo(PanNorth, "Vers le nord", CameraAndMapSection),
                new InputActionInfo(PanSouth, "Vers le sud", CameraAndMapSection),
                new InputActionInfo(PanWest, "Vers l'ouest", CameraAndMapSection),
                new InputActionInfo(PanEast, "Vers l'est", CameraAndMapSection),

                new InputActionInfo(Rotate, "Tourner le bâtiment", ConstructionSection),
                new InputActionInfo(MoveInputSide, "Déplacer la flèche d'entrée", ConstructionSection),
                new InputActionInfo(DropDragAxis, "Relâcher l'axe du glissé", ConstructionSection),

                new InputActionInfo(BuildingMenu, "Menu des bâtiments", InterfaceSection),
                new InputActionInfo(Pause, "Pause", InterfaceSection),
                new InputActionInfo(Close, "Fermer / annuler", InterfaceSection),
                new InputActionInfo(ShowRecipes, "Afficher les recettes", InterfaceSection),
            };

            for (int slot = 1; slot <= ToolbarSlotCount; slot++)
            {
                all.Add(new InputActionInfo(Slot(slot), "Emplacement " + slot, ToolbarSection));
            }

            return all.ToArray();
        }

        /// <summary>How many toolbar slots have a shortcut. Matches BuildingMenuController.ToolbarSlotCount, and a test says so.</summary>
        public const int ToolbarSlotCount = 8;
    }
}
