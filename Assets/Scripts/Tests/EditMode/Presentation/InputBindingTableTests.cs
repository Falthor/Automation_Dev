using System.Collections.Generic;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// The binding table and the catalogue, read against each other and against the real asset.
    ///
    /// <b>This is what makes "one action, one binding, no copy" mechanical.</b> The action names
    /// necessarily appear twice - the <c>.inputactions</c> asset says which key each is on, and
    /// <see cref="InputActionCatalogue"/> says which actions exist and what to call them in French,
    /// because the asset format has nowhere to put a label. That duplication could not be designed
    /// away, so it is pinned instead: an action added to one and forgotten in the other fails here
    /// rather than showing up in play as a blank row or a dead shortcut.
    ///
    /// The defaults are pinned as literals for the same reason the sector contents are: they are the
    /// keys the code used to read directly, and a change to them changes what every existing player
    /// presses.
    /// </summary>
    public class InputBindingTableTests
    {
        static InputActionAsset Table()
        {
            InputActionAsset table = InputSystem.actions;
            Assert.IsNotNull(table, "No project-wide input actions asset. Project Settings > Input System Package > Project-wide Actions.");
            return table;
        }

        // ---- The two lists agree ----

        [Test]
        public void EveryCatalogueAction_IsInTheTable()
        {
            InputActionAsset table = Table();

            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                Assert.IsNotNull(table.FindAction(info.Name),
                    $"the catalogue offers '{info.Name}' ({info.Label}) and the table has no such action - the menu would show a row that binds nothing");
            }
        }

        [Test]
        public void EveryTableAction_IsInTheCatalogue()
        {
            var catalogued = new HashSet<string>();
            foreach (InputActionInfo info in InputActionCatalogue.All) catalogued.Add(info.Name);

            foreach (InputActionMap map in Table().actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    Assert.IsTrue(catalogued.Contains(action.name),
                        $"'{map.name}/{action.name}' is in the table and not in the catalogue - it would be reassignable by nothing and invisible in the menu");
                }
            }
        }

        [Test]
        public void NoActionIsListedTwice()
        {
            var seen = new HashSet<string>();

            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                Assert.IsTrue(seen.Add(info.Name), $"'{info.Name}' appears twice in the catalogue");
            }
        }

        /// <summary>
        /// Every action carries a French label and a section. A blank one is not a compile error and
        /// would reach the menu as an empty row.
        /// </summary>
        [Test]
        public void EveryActionHasALabelAndASection()
        {
            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                Assert.IsNotEmpty(info.Label ?? string.Empty, $"'{info.Name}' has no label");
                Assert.IsNotEmpty(info.Section ?? string.Empty, $"'{info.Name}' has no section");
            }
        }

        /// <summary>
        /// Sections are contiguous, because the array's order is the display order and a menu that
        /// grouped by scanning would print one heading twice.
        /// </summary>
        [Test]
        public void ActionsSharingASection_AreConsecutive()
        {
            var closed = new HashSet<string>();
            string current = null;

            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                if (info.Section == current) continue;

                Assert.IsFalse(closed.Contains(info.Section),
                    $"section '{info.Section}' is interrupted and resumed - the menu would print its heading twice");

                if (current != null) closed.Add(current);
                current = info.Section;
            }
        }

        // ---- The defaults, as literals ----

        /// <summary>
        /// <b>wKey/aKey are not a typo.</b> Binding paths name a physical position with the US layout
        /// as their reference, exactly as the <c>Key</c> enum does, so <c>&lt;Keyboard&gt;/w</c> is
        /// the key an AZERTY board prints "Z" on. That is the whole point of the fix these bindings
        /// carry: the code used to read <c>zKey</c> under a comment claiming AZERTY, and bound north
        /// to the key marked W.
        /// </summary>
        [TestCase(InputActionCatalogue.PanNorth, "<Keyboard>/w")]
        [TestCase(InputActionCatalogue.PanSouth, "<Keyboard>/s")]
        [TestCase(InputActionCatalogue.PanEast, "<Keyboard>/d")]
        [TestCase(InputActionCatalogue.PanWest, "<Keyboard>/a")]
        [TestCase(InputActionCatalogue.Rotate, "<Keyboard>/r")]
        [TestCase(InputActionCatalogue.MoveInputSide, "<Keyboard>/t")]
        [TestCase(InputActionCatalogue.DropDragAxis, "<Keyboard>/ctrl")]
        [TestCase(InputActionCatalogue.BuildingMenu, "<Keyboard>/b")]
        [TestCase(InputActionCatalogue.Pause, "<Keyboard>/space")]
        [TestCase(InputActionCatalogue.Close, "<Keyboard>/escape")]
        public void TheDefaultBindingsAreWhatTheCodeUsedToRead(string actionName, string expectedPath)
        {
            InputAction action = Table().FindAction(actionName);
            Assert.IsNotNull(action, actionName);

            Assert.AreEqual(1, action.bindings.Count, $"'{actionName}' should have exactly one binding");
            Assert.AreEqual(expectedPath, action.bindings[0].path);
        }

        [Test]
        public void TheEightToolbarSlots_AreOnTheirOwnDigits()
        {
            InputActionAsset table = Table();

            for (int slot = 1; slot <= InputActionCatalogue.ToolbarSlotCount; slot++)
            {
                InputAction action = table.FindAction(InputActionCatalogue.Slot(slot));
                Assert.IsNotNull(action, "slot " + slot);
                Assert.AreEqual("<Keyboard>/" + slot, action.bindings[0].path);
            }
        }

        /// <summary>
        /// The grid overlay is on F1. Pinned separately from the block above, whose subject is the
        /// keys the code used to read directly - this one was never read anywhere, so it belongs to
        /// no such history. What it does share is the reason: a default key is what every existing
        /// player presses, so moving it is a decision rather than an edit.
        /// </summary>
        [Test]
        public void TheGridOverlayIsOnF1()
        {
            InputAction action = Table().FindAction(InputActionCatalogue.ShowGrid);
            Assert.IsNotNull(action, InputActionCatalogue.ShowGrid);

            Assert.AreEqual(1, action.bindings.Count, "the grid shortcut should have exactly one binding");
            Assert.AreEqual("<Keyboard>/f1", action.bindings[0].path);
        }

        /// <summary>
        /// The connection arrows are on F2, next to the grid on F1 - the two overlays that answer
        /// "what is where" sit together. Pinned for the same reason F1 is: a default key is what
        /// every existing player presses.
        /// </summary>
        [Test]
        public void TheConnectionArrowsAreOnF2()
        {
            InputAction action = Table().FindAction(InputActionCatalogue.ShowConnections);
            Assert.IsNotNull(action, InputActionCatalogue.ShowConnections);

            Assert.AreEqual(1, action.bindings.Count, "the arrow shortcut should have exactly one binding");
            Assert.AreEqual("<Keyboard>/f2", action.bindings[0].path);
        }

        /// <summary>
        /// The toolbar has as many shortcuts as it has slots. Two constants in two assemblies, and a
        /// mismatch would leave a slot no key can reach.
        /// </summary>
        [Test]
        public void TheShortcutCountMatchesTheToolbar()
        {
            Assert.AreEqual(Game.UI.BuildingMenuController.ToolbarSlotCount, InputActionCatalogue.ToolbarSlotCount);
        }

        // ---- Nothing that is not reassignable ----

        /// <summary>
        /// The table holds keyboard bindings and nothing else. The mouse buttons, the wheel and the
        /// map's pointer drag are deliberately not reassignable - putting one here would offer it in
        /// the menu, and the click/drag arbitration is a contract between three components that a
        /// reassignable button could break into a configuration where clicking selects nothing.
        /// </summary>
        [Test]
        public void TheTableIsKeyboardOnly()
        {
            foreach (InputActionMap map in Table().actionMaps)
            {
                foreach (InputBinding binding in map.bindings)
                {
                    Assert.IsTrue(binding.path.StartsWith("<Keyboard>/"),
                        $"'{map.name}/{binding.action}' is bound to {binding.path}, which is not a keyboard control");
                }
            }
        }
    }
}
