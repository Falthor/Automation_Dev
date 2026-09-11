using System.Collections.Generic;
using Game.Presentation;
using Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// The shortcuts screen: its shape, and the rule that decides a conflict.
    ///
    /// <b>What cannot be tested here, and why that is not a hole being papered over.</b> The capture
    /// itself needs a keypress, and an EditMode test has no keyboard to press - so
    /// "Escape cancels instead of binding" and "the amber row is the one listening" are not
    /// assertable, and neither is the conflict dialog, which only exists once a key has been
    /// captured. What is assertable is everything either side of that keypress: the list is built
    /// from the catalogue, the rule that finds the clash is a named function over the real table, and
    /// an unbound action reads as one.
    ///
    /// <b>The table is put back exactly as it was found.</b> Domain Reload is disabled, so the
    /// actions asset instance is shared with the editor session - a test that left an override behind
    /// would change what the next Play session reads.
    /// </summary>
    public class ShortcutsPanelTests
    {
        const string OverlayName = "ShortcutsOverlay";

        /// <summary>
        /// The two screens that instantiate the shortcuts template. Both are exercised, because the
        /// markup being shared is the whole point: one list opened from two places rather than two
        /// that can drift.
        /// </summary>
        const string MainMenuUxml = "Assets/UI/MainMenu.uxml";
        const string TopBarUxml = "Assets/UI/TopBar.uxml";

        string _overridesAsFound;

        [SetUp]
        public void RememberTheTable()
        {
            _overridesAsFound = InputSystem.actions?.SaveBindingOverridesAsJson();
        }

        [TearDown]
        public void PutTheTableBack()
        {
            InputActionAsset table = InputSystem.actions;
            if (table == null) return;

            table.RemoveAllBindingOverrides();
            if (!string.IsNullOrEmpty(_overridesAsFound)) table.LoadBindingOverridesFromJson(_overridesAsFound);

            // Show() suspends every action and only Hide() puts them back, so a test that opened the
            // screen and did not close it would leave the editor session with dead shortcuts.
            table.Enable();
        }

        static VisualElement BuildOverlay(out ShortcutsPanel panel, string hostUxml = MainMenuUxml)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(hostUxml);
            Assert.IsNotNull(tree, hostUxml + " is missing.");

            VisualElement root = tree.CloneTree();
            VisualElement overlay = root.Q<VisualElement>(OverlayName);
            Assert.IsNotNull(overlay, $"'{OverlayName}' is not in {hostUxml} - the Shortcuts template is not instantiated there");

            panel = new ShortcutsPanel(overlay);
            return overlay;
        }

        static ScrollView ListOf(VisualElement overlay) => overlay.Q<ScrollView>("ShortcutsList");

        /// <summary>The key button of the row whose label reads <paramref name="label"/>.</summary>
        static Button KeyButtonFor(VisualElement overlay, string label)
        {
            foreach (VisualElement child in ListOf(overlay).Children())
            {
                var rowLabel = child.Q<Label>(className: "shortcuts-row-label");
                if (rowLabel != null && rowLabel.text == label) return child.Q<Button>(className: "shortcuts-key");
            }

            Assert.Fail($"no row labelled '{label}'");
            return null;
        }

        // ---- The shape of the list ----

        [TestCase(MainMenuUxml)]
        [TestCase(TopBarUxml)]
        public void EveryCatalogueAction_GetsARow(string hostUxml)
        {
            VisualElement overlay = BuildOverlay(out _, hostUxml);

            var labelled = new List<string>();
            foreach (VisualElement child in ListOf(overlay).Children())
            {
                var rowLabel = child.Q<Label>(className: "shortcuts-row-label");
                if (rowLabel != null) labelled.Add(rowLabel.text);
            }

            Assert.AreEqual(InputActionCatalogue.All.Count, labelled.Count,
                $"in {hostUxml}: one row per action, no more and no fewer");

            for (int i = 0; i < InputActionCatalogue.All.Count; i++)
            {
                Assert.AreEqual(InputActionCatalogue.All[i].Label, labelled[i], $"row {i} is out of catalogue order");
            }
        }

        /// <summary>
        /// One heading per section, and each printed once. The catalogue guarantees the sections are
        /// contiguous; this checks the list actually uses that rather than emitting a heading per row.
        /// </summary>
        [Test]
        public void EachSectionGetsExactlyOneHeading()
        {
            VisualElement overlay = BuildOverlay(out _);

            var headings = new List<string>();
            foreach (VisualElement child in ListOf(overlay).Children())
            {
                if (child is Label heading && heading.ClassListContains("shortcuts-section")) headings.Add(heading.text);
            }

            var expected = new List<string>();
            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                if (!expected.Contains(info.Section)) expected.Add(info.Section);
            }

            CollectionAssert.AreEqual(expected, headings);
        }

        [Test]
        public void EveryRowCarriesAKeyAndAResetButton()
        {
            VisualElement overlay = BuildOverlay(out _);

            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                Assert.IsNotNull(KeyButtonFor(overlay, info.Label), info.Label + " has no key button");
            }

            int resets = 0;
            foreach (VisualElement child in ListOf(overlay).Children())
            {
                if (child.Q<Button>(className: "shortcuts-reset") != null) resets++;
            }

            Assert.AreEqual(InputActionCatalogue.All.Count, resets, "one reset button per row");
        }

        // ---- What a row shows ----

        /// <summary>
        /// <b>The key is named the way the player's keyboard names it, never the way the path does.</b>
        /// The exact string cannot be asserted - it depends on the layout of whoever runs the suite,
        /// which is the entire reason this must not print the path. So what is pinned is that it is
        /// not the path and not the action's internal name.
        /// </summary>
        [Test]
        public void AKeyIsShownByItsDisplayName_NotByItsPath()
        {
            VisualElement overlay = BuildOverlay(out ShortcutsPanel panel);
            panel.Show();

            InputAction north = InputBindings.Find(InputActionCatalogue.PanNorth);
            Assert.IsNotNull(north);

            string shown = KeyButtonFor(overlay, "Vers le nord").text;

            Assert.IsNotEmpty(shown);
            Assert.AreNotEqual(north.bindings[0].effectivePath, shown, "the row is printing the binding path");
            Assert.IsFalse(shown.Contains("<Keyboard>"), "the row is printing a control path");
            Assert.AreNotEqual(InputActionCatalogue.PanNorth, shown, "the row is printing the action's internal name");
        }

        /// <summary>
        /// An action left with no key at all - what overwriting a shortcut does to the row that held
        /// it. It has to read as a hole rather than as a blank cell.
        /// </summary>
        [Test]
        public void AnUnboundAction_ReadsAsUnassigned()
        {
            VisualElement overlay = BuildOverlay(out ShortcutsPanel panel);

            InputAction rotate = InputBindings.Find(InputActionCatalogue.Rotate);
            rotate.ApplyBindingOverride(0, new InputBinding { overridePath = string.Empty });

            panel.Show();

            Assert.AreEqual("non assigné", KeyButtonFor(overlay, "Tourner le bâtiment").text);
        }

        /// <summary>
        /// <b>Every action is off for as long as the screen is up.</b> Opened in game it sits over a
        /// running world: left live, B would open the building menu behind it and Space would pause
        /// underneath. Closing is the only thing that gives them back, which is also why Escape does
        /// not close this screen - Escape is an action, and it is off with the rest.
        /// </summary>
        [Test]
        public void WhileTheScreenIsUp_NoActionAnswers()
        {
            BuildOverlay(out ShortcutsPanel panel);
            InputActionMap first = InputSystem.actions.actionMaps[0];

            Assert.IsTrue(first.enabled, "the table starts enabled");

            panel.Show();
            Assert.IsFalse(first.enabled, "opening the screen has to silence the shortcuts under it");

            panel.Hide();
            Assert.IsTrue(first.enabled, "closing it has to give them back");
        }

        // ---- The conflict rule ----

        [Test]
        public void WithTheDefaults_NothingClashes()
        {
            foreach (InputActionInfo info in InputActionCatalogue.All)
            {
                Assert.IsNull(ShortcutsPanel.ActionSharingTheKeyWith(info.Name),
                    $"'{info.Name}' shares its default key with another action");
            }
        }

        [Test]
        public void TwoActionsOnOneKey_NameEachOther()
        {
            InputAction rotate = InputBindings.Find(InputActionCatalogue.Rotate);
            InputAction menu = InputBindings.Find(InputActionCatalogue.BuildingMenu);

            rotate.ApplyBindingOverride(0, new InputBinding { overridePath = menu.bindings[0].effectivePath });

            Assert.AreEqual(InputActionCatalogue.BuildingMenu, ShortcutsPanel.ActionSharingTheKeyWith(InputActionCatalogue.Rotate));
            Assert.AreEqual(InputActionCatalogue.Rotate, ShortcutsPanel.ActionSharingTheKeyWith(InputActionCatalogue.BuildingMenu));
        }

        /// <summary>
        /// The rule reads the effective key, not the asset's. An override that moves an action off a
        /// shared default has resolved the clash, and a rule reading the asset would keep reporting
        /// one.
        /// </summary>
        [Test]
        public void AnOverrideThatMovesAnActionAway_ResolvesTheClash()
        {
            InputAction rotate = InputBindings.Find(InputActionCatalogue.Rotate);
            InputAction menu = InputBindings.Find(InputActionCatalogue.BuildingMenu);

            rotate.ApplyBindingOverride(0, new InputBinding { overridePath = menu.bindings[0].effectivePath });
            Assert.IsNotNull(ShortcutsPanel.ActionSharingTheKeyWith(InputActionCatalogue.Rotate));

            rotate.ApplyBindingOverride(0, new InputBinding { overridePath = "<Keyboard>/f" });

            Assert.IsNull(ShortcutsPanel.ActionSharingTheKeyWith(InputActionCatalogue.Rotate));
            Assert.IsNull(ShortcutsPanel.ActionSharingTheKeyWith(InputActionCatalogue.BuildingMenu));
        }

        /// <summary>
        /// Several actions may sit on no key at once without that being a clash - which it would be
        /// if the rule compared empty paths like any other.
        /// </summary>
        [Test]
        public void UnboundActionsDoNotClashWithEachOther()
        {
            InputBindings.Find(InputActionCatalogue.Rotate)
                .ApplyBindingOverride(0, new InputBinding { overridePath = string.Empty });
            InputBindings.Find(InputActionCatalogue.MoveInputSide)
                .ApplyBindingOverride(0, new InputBinding { overridePath = string.Empty });

            Assert.IsNull(ShortcutsPanel.ActionSharingTheKeyWith(InputActionCatalogue.Rotate));
            Assert.IsNull(ShortcutsPanel.ActionSharingTheKeyWith(InputActionCatalogue.MoveInputSide));
        }
    }
}
