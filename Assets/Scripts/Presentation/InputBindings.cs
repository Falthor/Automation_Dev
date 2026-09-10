using Game.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>
    /// The one way into the binding table: resolves an action by name, and moves the player's
    /// reassignments between the table and <c>preferences.json</c>.
    ///
    /// <b>The table is the Input System's project-wide actions asset</b>
    /// (<c>InputSystem_Actions.inputactions</c>, named from <c>ProjectSettings</c>). Using that
    /// rather than a serialized field means no scene wiring and no per-scene reference - which
    /// matters because the shortcuts menu lives in <c>MainMenu.unity</c>, where there is no
    /// GameRuntime to hang anything off.
    ///
    /// <b>A static class with no fields of its own, deliberately.</b> Domain Reload is disabled in
    /// this project (DEVELOPMENT_RULES §5), so a mutable static would keep its value across Play
    /// sessions. There is nothing to keep: the only state is inside
    /// <see cref="InputSystem.actions"/>, which is Unity's, and every method here is a read or a
    /// write of it.
    ///
    /// <b>That asset instance survives Play sessions too, and this is written to not care.</b>
    /// <see cref="ApplyStoredOverrides"/> clears every override before applying the stored ones, so
    /// running it twice gives the same result as running it once, and a session that reassigned a key
    /// without saving does not leak into the next one. Applying on top of whatever was left would
    /// have been the same defect the disabled reload creates everywhere else, just harder to see.
    /// </summary>
    public static class InputBindings
    {
        /// <summary>
        /// Loads the player's reassignments and turns the actions on, once per session, before the
        /// first scene loads.
        ///
        /// <c>BeforeSceneLoad</c> rather than a component in each scene: both Bootstrap and MainMenu
        /// need the bindings, and a component would be two things to wire and one to forget. It also
        /// guarantees the actions answer from the first <c>Start</c> that asks.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialise()
        {
            InputActionAsset table = InputSystem.actions;
            if (table == null)
            {
                Debug.LogError("No project-wide input actions asset - every shortcut will be dead. "
                    + "Check Project Settings > Input System Package > Project-wide Actions.");
                return;
            }

            ApplyStoredOverrides();
            table.Enable();
        }

        /// <summary>
        /// The action, or null when the table does not have it. Null rather than a throw because a
        /// missing action should cost one shortcut, not the frame - and
        /// <c>InputActionCatalogueTests</c> already fails the suite if the catalogue and the table
        /// ever disagree, so this path means someone edited the asset by hand.
        /// </summary>
        public static InputAction Find(string actionName)
        {
            InputActionAsset table = InputSystem.actions;
            if (table == null) return null;

            InputAction action = table.FindAction(actionName);
            if (action == null) Debug.LogError($"Input action '{actionName}' is not in the binding table.");

            return action;
        }

        // Null-tolerant reads, so a consumer keeps the shape it had when it asked the keyboard
        // directly: "the device is there and the key is down" becomes "the action is there and the
        // action is down". Consumers resolve once in Start and hold the reference; FindAction walks
        // the maps, and that has no business happening per frame.

        public static bool IsPressed(InputAction action) => action != null && action.IsPressed();

        public static bool WasPressedThisFrame(InputAction action) => action != null && action.WasPressedThisFrame();

        /// <summary>
        /// Replaces every override with what <c>preferences.json</c> holds. Idempotent - see the
        /// class summary for why that is not a nicety here.
        /// </summary>
        public static void ApplyStoredOverrides()
        {
            InputActionAsset table = InputSystem.actions;
            if (table == null) return;

            table.RemoveAllBindingOverrides();

            string stored = PreferencesService.ReadInputBindings();
            if (!string.IsNullOrEmpty(stored)) table.LoadBindingOverridesFromJson(stored);
        }

        /// <summary>
        /// Writes the current reassignments to <c>preferences.json</c>. Called after a change rather
        /// than on quit: a key the player just reassigned has to survive a crash, and it is one small
        /// file.
        /// </summary>
        public static void StoreOverrides()
        {
            InputActionAsset table = InputSystem.actions;
            if (table == null) return;

            PreferencesService.WriteInputBindings(table.SaveBindingOverridesAsJson());
        }
    }
}
