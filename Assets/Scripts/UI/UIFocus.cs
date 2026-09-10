using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The single answer to "is the player typing into something right now?".
    ///
    /// <b>It exists because the test everyone writes is wrong.</b> Both callers asked
    /// <c>focusedElement is TextField</c>, which never answers true. A <see cref="TextField"/> is a
    /// <c>BaseField&lt;string&gt;</c> and it <b>delegates its focus</b>: the element that ends up
    /// focused is the text input inside it, not the field. The guard read correctly, compiled, and
    /// could not fire - the kind of defect that only shows up the day something actually has a text
    /// field to type in, as a keypress that both typed a character and triggered a shortcut.
    ///
    /// So the question is asked of the ancestry, not of the focused element alone.
    ///
    /// <b>Nothing in the project has a text field yet</b>, so this is a correctness fix rather than
    /// an observed one. What protects the shortcut capture in the options screen is a different
    /// mechanism entirely - the action maps are turned off for the duration
    /// (<c>InputBindings.Suspend</c>) - because a capture focuses nothing and a text-field guard
    /// would have been no help there either.
    /// </summary>
    public static class UIFocus
    {
        /// <summary>
        /// True while a text field of this document holds the focus. A null document, or one whose
        /// panel is not built, means "nobody is typing" - so a scene without UI keeps its shortcuts
        /// rather than losing them all.
        /// </summary>
        public static bool IsTypingInAField(UIDocument document)
        {
            VisualElement root = document?.rootVisualElement;
            var focused = root?.panel?.focusController?.focusedElement as VisualElement;
            if (focused == null) return false;

            return focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null;
        }
    }
}
