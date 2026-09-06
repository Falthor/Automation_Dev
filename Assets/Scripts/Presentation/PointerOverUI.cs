using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    /// <summary>
    /// The single answer to "is the cursor on the UI or on the world?".
    ///
    /// It exists because there are two different questions that look alike, and conflating them
    /// costs input the player expects to have. <b>"A panel is open"</b> is the right gate for a
    /// click, which must not fall through a panel onto the world behind it. It is the wrong gate
    /// for the scroll wheel, which only conflicts with the UI when the cursor is actually over a
    /// scrollable element - gating the camera on it takes zooming away everywhere else on screen,
    /// with a panel open or a building selected.
    /// </summary>
    public static class PointerOverUI
    {
        /// <summary>
        /// True when <paramref name="screenPosition"/> lands on a pickable UI element of this
        /// document. A null document, or one whose panel is not built yet, means "not over UI" -
        /// so a scene without UI is fully interactive rather than fully inert.
        ///
        /// The Y flip is not optional. Mouse positions arrive with the origin at the screen's
        /// bottom-left while a UI Toolkit panel's coordinates start at its top-left, and
        /// ScreenToPanel does not flip that axis itself: passing the raw position picks a
        /// vertically mirrored point, which reports "no UI here" for positions that do hit a panel.
        /// </summary>
        public static bool At(UIDocument document, Vector2 screenPosition)
        {
            if (document == null) return false;

            IPanel panel = document.rootVisualElement?.panel;
            if (panel == null) return false;

            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));

            return panel.Pick(panelPosition) != null;
        }
    }
}
