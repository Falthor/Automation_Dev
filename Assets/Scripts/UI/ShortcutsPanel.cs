using System.Collections.Generic;
using Game.Presentation;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The shortcuts screen: every reassignable action, grouped, with the key it is on.
    ///
    /// <b>A plain class rather than a MonoBehaviour.</b> It is handed the overlay element its markup
    /// already sits in (MainMenu.uxml) and owns everything inside it, so there is no component to add
    /// to the scene and no serialized reference to wire - the same shape
    /// <see cref="SectorMapElement"/> uses. MainMenuController builds one and points the RACCOURCIS
    /// button at it.
    ///
    /// <b>Every key is shown through the control's own display name.</b> A binding path names a
    /// physical position with the US layout as its reference, so <c>&lt;Keyboard&gt;/w</c> is the key
    /// an AZERTY board prints "Z" on. Printing the path, or the <c>Key</c> enum's name, would label
    /// that row "W" and send the player pressing the wrong key. <c>GetBindingDisplayString</c>
    /// resolves through the connected keyboard's layout, and it is the only correct answer here.
    ///
    /// <b>A conflict is announced and resolved, never refused.</b> Refusing a key the player has
    /// chosen leaves them guessing which of eighteen rows already holds it. So the taken key is
    /// accepted, the row that held it is named, and overwriting leaves that action <i>visibly</i>
    /// unassigned - a hole in the list they can fill, rather than a silent swap.
    ///
    /// <b>Escape cancels the capture rather than becoming the shortcut.</b> Built into the rebinding
    /// operation, which is also why nothing can ever be bound to Escape: a row opened by accident has
    /// to have a way out, and that matters more than being able to put Escape somewhere else.
    /// </summary>
    public sealed class ShortcutsPanel
    {
        /// <summary>What a key button says while it is listening.</summary>
        const string ListeningText = "…";

        /// <summary>What a row with no key at all says. Not blank: a blank cell reads as a rendering fault rather than as a choice the player made.</summary>
        const string UnassignedText = "non assigné";

        /// <summary>The per-row reset glyph. A single character, so the column stays 26px wide whatever sits beside it.</summary>
        const string ResetGlyph = "↺";

        readonly VisualElement _overlay;
        readonly VisualElement _conflictOverlay;
        readonly Label _conflictMessage;
        readonly ScrollView _list;

        /// <summary>One row per action, kept so a change repaints the two rows it touched instead of rebuilding eighteen.</summary>
        readonly Dictionary<string, Row> _rows = new Dictionary<string, Row>();

        InputActionRebindingExtensions.RebindingOperation _capture;

        // The conflict being asked about. Held in fields rather than captured in the dialog's
        // handlers, which are subscribed once: a handler added on each opening would fire for every
        // earlier conflict as well, and replacing the button instead would hit the rule that a
        // Button rebuilt between press and release can never be clicked.
        Row _pendingRow;
        Row _pendingTaken;
        string _pendingPreviousOverride;

        sealed class Row
        {
            public InputAction Action;
            public string Label;
            public Button Key;
            public Button Reset;
        }

        public ShortcutsPanel(VisualElement overlay)
        {
            _overlay = overlay;
            _list = overlay.Q<ScrollView>("ShortcutsList");

            _conflictOverlay = overlay.Q<VisualElement>("ShortcutsConflictOverlay");
            _conflictMessage = overlay.Q<Label>("ShortcutsConflictMessage");
            overlay.Q<Button>("ShortcutsConflictAcceptButton").clicked += AcceptOverwrite;
            overlay.Q<Button>("ShortcutsConflictCancelButton").clicked += RefuseOverwrite;

            overlay.Q<Button>("ShortcutsCloseButton").clicked += Hide;
            overlay.Q<Button>("ShortcutsResetAllButton").clicked += ResetEverything;

            BuildRows();
        }

        public void Show()
        {
            RefreshEveryRow();
            _overlay.RemoveFromClassList("hidden");
        }

        public void Hide()
        {
            // A capture left running would keep every action disabled for the rest of the session and
            // swallow the next key pressed anywhere.
            CancelCapture();

            _conflictOverlay.AddToClassList("hidden");
            _overlay.AddToClassList("hidden");
        }

        // ---- The list ----

        void BuildRows()
        {
            string previousSection = null;

            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                if (info.Section != previousSection)
                {
                    var heading = new Label(info.Section);
                    heading.AddToClassList("shortcuts-section");
                    if (previousSection == null) heading.AddToClassList("shortcuts-section-first");
                    _list.Add(heading);

                    previousSection = info.Section;
                }

                _list.Add(BuildRow(info));
            }
        }

        VisualElement BuildRow(InputActionInfo info)
        {
            var row = new VisualElement();
            row.AddToClassList("shortcuts-row");

            var label = new Label(info.Label);
            label.AddToClassList("shortcuts-row-label");
            row.Add(label);

            string actionName = info.Name;

            var key = new Button(() => BeginCapture(actionName)) { text = string.Empty };
            key.AddToClassList("shortcuts-key");
            row.Add(key);

            var reset = new Button(() => ResetOne(actionName)) { text = ResetGlyph };
            reset.AddToClassList("shortcuts-reset");
            reset.tooltip = "Remettre la touche par défaut";
            row.Add(reset);

            _rows[actionName] = new Row
            {
                Action = InputBindings.Find(actionName),
                Label = info.Label,
                Key = key,
                Reset = reset
            };

            return row;
        }

        void RefreshEveryRow()
        {
            foreach (KeyValuePair<string, Row> entry in _rows) Refresh(entry.Value);
        }

        static void Refresh(Row row)
        {
            if (row.Action == null)
            {
                // The catalogue offers a row the table has no action for. A test fails on that, so
                // this means a hand-edited asset - say so in the row rather than pretending to show a
                // live control.
                row.Key.text = "—";
                row.Key.SetEnabled(false);
                row.Reset.SetEnabled(false);
                return;
            }

            // Read from the effective path rather than from the display string. An unassigned
            // binding is an empty override path, and asking the display layer what an empty path
            // looks like is asking the wrong side of the question.
            bool unassigned = string.IsNullOrEmpty(row.Action.bindings[0].effectivePath);
            string display = unassigned ? UnassignedText : row.Action.GetBindingDisplayString(0);
            if (string.IsNullOrEmpty(display)) display = UnassignedText;

            row.Key.text = display;
            row.Key.EnableInClassList("shortcuts-key-unassigned", unassigned);
            row.Key.EnableInClassList("shortcuts-key-listening", false);

            // Nothing to put back when the row is already on the key the asset gives it.
            row.Reset.SetEnabled(row.Action.bindings[0].overridePath != null);
        }

        // ---- Capturing a key ----

        void BeginCapture(string actionName)
        {
            if (_capture != null) return;
            if (!_rows.TryGetValue(actionName, out Row row) || row.Action == null) return;

            row.Key.text = ListeningText;
            row.Key.AddToClassList("shortcuts-key-listening");

            // The click that started this left the button focused, and UI Toolkit activates a
            // focused Button on Space and Enter. Binding either of those would have completed the
            // capture and, on the same keypress, submitted the button that started it - reopening
            // the capture the player had just closed. Dropping the focus is the whole fix.
            row.Key.Blur();

            // What was on this binding before, so refusing a conflict puts it back exactly. Null and
            // empty are different answers: null is "no override, use the asset's key", empty is "the
            // player deliberately left this unassigned".
            _pendingPreviousOverride = row.Action.bindings[0].overridePath;

            // Both the operation and the shortcuts themselves need the actions off - see
            // InputBindings.Suspend.
            InputBindings.Suspend();

            _capture = row.Action.PerformInteractiveRebinding(0)
                // Keyboard only: the table is keyboard-only by contract (CONTRACTS.md §12b), and a
                // mouse button captured here would be a binding the rest of the game cannot honour.
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(_ => EndCapture(row, cancelled: true))
                .OnComplete(_ => EndCapture(row, cancelled: false));

            _capture.Start();
        }

        void EndCapture(Row row, bool cancelled)
        {
            DisposeCapture();
            InputBindings.Resume();

            if (cancelled)
            {
                // Nothing was applied, so nothing has to be undone.
                Refresh(row);
                return;
            }

            string takenBy = ActionSharingTheKeyWith(row.Action.name);
            if (takenBy == null || !_rows.TryGetValue(takenBy, out Row taken))
            {
                Commit(row);
                return;
            }

            _pendingRow = row;
            _pendingTaken = taken;

            _conflictMessage.text = $"« {taken.Label} » utilise déjà cette touche. "
                + $"L'écraser laissera « {taken.Label} » sans raccourci.";
            _conflictOverlay.RemoveFromClassList("hidden");
        }

        /// <summary>
        /// The name of the other action currently on <paramref name="actionName"/>'s key, or null
        /// when it has that key to itself.
        ///
        /// <b>Compares effective paths, which is what the game actually reads.</b> Two actions
        /// agreeing only on their asset defaults are still a conflict; one whose override has moved
        /// it away is not, however the asset still reads. An unbound action conflicts with nothing -
        /// several may sit on no key at once without that being a clash.
        ///
        /// Static and over the catalogue rather than over the built rows, so the rule can be asked
        /// without a screen: it is the part of this panel worth pinning, and the capture around it
        /// needs a keypress no EditMode test can make.
        /// </summary>
        public static string ActionSharingTheKeyWith(string actionName)
        {
            InputAction action = InputBindings.Find(actionName);
            string path = action?.bindings[0].effectivePath;
            if (string.IsNullOrEmpty(path)) return null;

            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                if (info.Name == actionName) continue;

                InputAction other = InputBindings.Find(info.Name);
                if (other != null && other.bindings[0].effectivePath == path) return info.Name;
            }

            return null;
        }

        void AcceptOverwrite()
        {
            _conflictOverlay.AddToClassList("hidden");
            if (_pendingRow == null) return;

            // An empty override path is how the Input System says "bound to nothing". The action
            // keeps its row and reads as a hole the player can fill.
            _pendingTaken.Action.ApplyBindingOverride(0, new InputBinding { overridePath = string.Empty });

            Commit(_pendingRow);
            Refresh(_pendingTaken);
            ForgetPendingConflict();
        }

        void RefuseOverwrite()
        {
            _conflictOverlay.AddToClassList("hidden");
            if (_pendingRow == null) return;

            // Put the row back exactly where it was. The rebinding operation already applied the new
            // key, so refusing is an undo rather than a no-op.
            if (_pendingPreviousOverride == null) _pendingRow.Action.RemoveBindingOverride(0);
            else _pendingRow.Action.ApplyBindingOverride(0, new InputBinding { overridePath = _pendingPreviousOverride });

            Refresh(_pendingRow);
            ForgetPendingConflict();
        }

        void ForgetPendingConflict()
        {
            _pendingRow = null;
            _pendingTaken = null;
            _pendingPreviousOverride = null;
        }

        void Commit(Row row)
        {
            InputBindings.StoreOverrides();
            Refresh(row);
        }

        void CancelCapture()
        {
            if (_capture == null) return;

            _capture.Cancel();
            DisposeCapture();
            InputBindings.Resume();
            RefreshEveryRow();
        }

        void DisposeCapture()
        {
            _capture?.Dispose();
            _capture = null;
        }

        // ---- Putting things back ----

        void ResetOne(string actionName)
        {
            if (!_rows.TryGetValue(actionName, out Row row) || row.Action == null) return;

            row.Action.RemoveBindingOverride(0);
            InputBindings.StoreOverrides();
            Refresh(row);
        }

        void ResetEverything()
        {
            InputBindings.ResetAllToDefaults();
            RefreshEveryRow();
        }
    }
}
