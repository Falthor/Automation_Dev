namespace Game.Save
{
    /// <summary>
    /// Carries the player's New Game/Load choice from MainMenu.unity across the scene load into
    /// Bootstrap.unity, where GameRuntime.Awake() consumes and clears it immediately.
    ///
    /// This is the one deliberately mutable static state introduced by the save system
    /// (DEVELOPMENT_RULES.md §5 requires flagging this explicitly): two fields, alive only for
    /// the duration of one scene transition, never read outside GameRuntime.Awake() - not a
    /// general-purpose singleton or a second source of truth for game state.
    /// </summary>
    public static class PendingGameStart
    {
        public static SaveData LoadedSave { get; private set; }

        /// <summary>
        /// Which named save this session belongs to - the folder its writes go to.
        ///
        /// It travels with the choice rather than being re-derived in Bootstrap, because only the
        /// menu knows it: a new game's name was typed there, and a loaded game's name is the entry
        /// that was picked. Bootstrap has no way back to either.
        /// </summary>
        public static string SaveName { get; private set; } = SaveService.DefaultName;

        /// <summary>Marks the next Bootstrap.unity load as a fresh game under that name.</summary>
        public static void RequestNewGame(string saveName = null)
        {
            LoadedSave = null;
            SaveName = SaveService.Sanitise(saveName);
        }

        /// <summary>Marks the next Bootstrap.unity load as a restore of that named save.</summary>
        public static void RequestLoadGame(SaveData data, string saveName)
        {
            LoadedSave = data;
            SaveName = SaveService.Sanitise(saveName);
        }
    }
}
