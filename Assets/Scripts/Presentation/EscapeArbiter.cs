using Game.Construction;
using Game.Gameplay.Selection;
using System;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>What Escape is about to close, when it is about to close something.</summary>
    public enum EscapeClaimant
    {
        /// <summary>Nothing is open and nothing is armed - Escape does nothing.</summary>
        None = 0,

        /// <summary>A construction tool is armed and its ghost is following the cursor.</summary>
        ArmedTool = 1,

        /// <summary>A building, a construction site or an explorer robot is being inspected in the right-hand dock.</summary>
        ContextualPanel = 2,

        /// <summary>A named global panel is open - Storage, the building menu, Research, Power, the map.</summary>
        GlobalPanel = 3,

        /// <summary>
        /// The menu overlay - the in-game menu, or the shortcuts screen it hands over to.
        ///
        /// The only tier that appears twice in the stack: at the very top when one of those two is
        /// open, because closing what is in front of the player outranks everything behind it, and
        /// at the very bottom when neither is, because Escape with nothing open is how a player
        /// reaches the menu. Both are the same claimant because both are the same gesture on the
        /// same object, and one owner that toggles cannot disagree with itself about which of the
        /// two it is doing.
        /// </summary>
        MenuOverlay = 4
    }

    /// <summary>
    /// Whether there is a menu overlay to act on at all, and whether it is showing.
    ///
    /// Three states and not a bool, because "absent" and "closed" answer Escape differently:
    /// closed means Escape opens it, absent means Escape has nobody left and does nothing. Absent
    /// is the truth in a scene with no Top Bar, and in every test that does not build one.
    /// </summary>
    public enum MenuOverlayState
    {
        Absent = 0,
        Closed = 1,
        Open = 2
    }

    /// <summary>
    /// Decides who Escape belongs to on any given frame.
    ///
    /// <b>It exists because fourteen places read Escape and none of them arbitrated.</b> Each one
    /// returned early on its own state - "am I the active panel", "is anything selected", "am I
    /// open" - and the whole thing worked only as long as those states stayed mutually exclusive.
    /// Nothing guaranteed that and no test looked: two readers firing on one keypress was a bug
    /// waiting for the first state that overlapped, and one does overlap (below).
    ///
    /// <b>Exclusivity is derived, not raced.</b> There is no "already consumed" flag and no
    /// ordering between the readers, because there is nothing to consume:
    /// <see cref="Claimant"/> is a pure function of the state, so it can only ever name one tier,
    /// and it names the same one however Unity happens to order the components that ask. A flag
    /// would have made the answer depend on which <c>Update</c> ran first - the same class of
    /// defect, moved.
    ///
    /// <b>The stack is the menu overlay when it is open, then armed tool, then contextual panel,
    /// then global panel, then the menu overlay again when it is closed.</b> The first two can
    /// genuinely coexist: <see cref="SelectionRuntime"/> keeps its own slots mutually exclusive, and
    /// arms nothing, so clicking a notification opens a robot's panel while a tool is still armed.
    /// The armed tool wins there because it is the most transient state on screen - the ghost
    /// following the cursor is the thing the player most likely meant to drop.
    ///
    /// A contextual panel and a global panel cannot both be open: opening either closes the other,
    /// in <see cref="SelectionRuntime"/> itself. Their order in the stack therefore never decides
    /// anything today - it is written down so that the day one of them stops closing the other, the
    /// answer is here rather than in whichever fourteen files happen to be read first.
    ///
    /// <b>There is no fourth tier for the building menu.</b> It looks like one, and
    /// <c>BuildingMenuController.IsOpen</c> reads like independent state, but it is set from
    /// <c>GlobalPanelChanged</c> and is a mirror of <c>ActiveGlobalPanel == PanelName</c>. The menu
    /// is a global panel.
    /// </summary>
    public sealed class EscapeArbiter
    {
        readonly SelectionRuntime _selection;

        /// <summary>Resolved once, in the constructor: the arbiter is built in GameRuntime.Awake, which runs after the bindings are loaded.</summary>
        readonly InputAction _close;

        /// <summary>Optional: null means no tool can be armed, which is the truth in a scene without construction.</summary>
        readonly ConstructionService _construction;

        /// <summary>
        /// What the menu overlay is doing, asked of whoever owns it.
        ///
        /// <b>A delegate, and settable, unlike the other two facts.</b> Those come from systems this
        /// is built with in <c>GameRuntime.Awake</c>; the menu is a UI Toolkit overlay built later,
        /// in <c>TopBarController.Start</c>, out of a template the scene instantiates. Rather than
        /// delay the arbiter until the interface exists, its owner reports in when it does. Unset
        /// means Absent, which is what a scene without a Top Bar should answer.
        /// </summary>
        Func<MenuOverlayState> _menuProbe;

        int _settledFrame = -1;
        EscapeClaimant _settled;

        public EscapeArbiter(SelectionRuntime selection, ConstructionService construction)
        {
            _selection = selection;
            _construction = construction;
            _close = InputBindings.Find(InputActionCatalogue.Close);
        }

        /// <summary>
        /// The priority stack, as a pure function of the three facts. Static and separate from the
        /// live state on purpose: the ordering is the part worth pinning, and a test can pin every
        /// combination of it without building a world.
        /// </summary>
        public static EscapeClaimant ClaimantFor(bool aToolIsArmed, bool aContextualPanelIsOpen,
            bool aGlobalPanelIsOpen, MenuOverlayState menu)
        {
            if (menu == MenuOverlayState.Open) return EscapeClaimant.MenuOverlay;
            if (aToolIsArmed) return EscapeClaimant.ArmedTool;
            if (aContextualPanelIsOpen) return EscapeClaimant.ContextualPanel;
            if (aGlobalPanelIsOpen) return EscapeClaimant.GlobalPanel;
            return menu == MenuOverlayState.Closed ? EscapeClaimant.MenuOverlay : EscapeClaimant.None;
        }

        /// <summary>
        /// Tells the arbiter where to ask about the menu overlay. Called by the component that owns
        /// it, once, when it has built it - see <see cref="_menuProbe"/> for why it is not a
        /// constructor argument.
        /// </summary>
        public void SetMenuProbe(Func<MenuOverlayState> probe) => _menuProbe = probe;

        /// <summary>Who would take Escape right now, whether or not it has been pressed.</summary>
        public EscapeClaimant Claimant => ClaimantFor(
            _construction?.Selected != null,
            _selection != null && (_selection.SelectedBuilding != null
                || _selection.SelectedSite != null
                || _selection.SelectedExplorerRobot != null),
            _selection?.ActiveGlobalPanel != null,
            _menuProbe != null ? _menuProbe() : MenuOverlayState.Absent);

        /// <summary>
        /// True on the frame Escape goes down, and only for the one tier that owns it. Every other
        /// reader sees nothing.
        ///
        /// The single place the key itself is read, which is the other half of what this fixes:
        /// fourteen copies of <c>escapeKey.wasPressedThisFrame</c> were fourteen bindings to keep in
        /// step.
        /// </summary>
        public bool IsClaimedBy(EscapeClaimant claimant)
        {
            if (claimant == EscapeClaimant.None || !InputBindings.WasPressedThisFrame(_close)) return false;

            return ClaimantThisFrame(UnityEngine.Time.frameCount) == claimant;
        }

        /// <summary>
        /// The claimant as it stood when the first reader asked on this frame, held for the rest of it.
        ///
        /// <b>Derived is not enough on its own.</b> A reader acts on the key it is awarded, and acting
        /// changes the state the claimant is derived from: a panel closing itself leaves nothing open,
        /// and a reader asking later in the same frame - the Top Bar - was then told the key was the
        /// menu's. One keypress closed the window and opened the menu behind it. Settling the answer
        /// once per frame, before anybody has acted on it, is what makes one press close one thing.
        /// </summary>
        public EscapeClaimant ClaimantThisFrame(int frame)
        {
            if (frame != _settledFrame)
            {
                _settledFrame = frame;
                _settled = Claimant;
            }
            return _settled;
        }
    }
}
