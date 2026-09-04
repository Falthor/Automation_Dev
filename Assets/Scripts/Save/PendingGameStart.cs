namespace Game.Save
{
    /// <summary>
    /// Carries the player's New Game/Load choice from MainMenu.unity across the scene load into
    /// Bootstrap.unity, where GameRuntime.Awake() consumes and clears it immediately.
    ///
    /// This is the one deliberately mutable static state introduced by the save system
    /// (DEVELOPMENT_RULES.md §5 requires flagging this explicitly): a single field, alive only
    /// for the duration of one scene transition, never read outside GameRuntime.Awake() - not a
    /// general-purpose singleton or a second source of truth for game state.
    /// </summary>
    public static class PendingGameStart
    {
        public static SaveData LoadedSave { get; private set; }

        /// <summary>Marks the next Bootstrap.unity load as a fresh game.</summary>
        public static void RequestNewGame() => LoadedSave = null;

        /// <summary>Marks the next Bootstrap.unity load as a restore from the given save.</summary>
        public static void RequestLoadGame(SaveData data) => LoadedSave = data;
    }
}
