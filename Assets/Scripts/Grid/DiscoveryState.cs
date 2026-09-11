namespace Game.Grid
{
    /// <summary>
    /// What the player knows about one cell: never seen, seen before, or being seen right now.
    ///
    /// <b>Only the first two are ever stored.</b> Discovery is permanent and acquired;
    /// <see cref="Observed"/> is never written into a chunk, never saved, and never derived from the
    /// world seed - it is produced only by asking <see cref="ObservationRuntime"/> where the
    /// observers are at this instant. A stored observation state would be a second source of truth,
    /// free to contradict the actual position of the things doing the observing.
    ///
    /// So <see cref="DiscoveryRuntime.GetState"/> answers with the two stored values, and
    /// <see cref="ObservationRuntime.StateOf"/> is what can answer with all three.
    ///
    /// The distinction the third state carries is <b>the static against the living</b>: terrain,
    /// vegetation and deposits stay drawn out of observation because they do not move, so showing
    /// them is still accurate - while a nest or a unit would be showing information already out of
    /// date, and freezes on its last known state or goes.
    ///
    /// Stored as its numeric value in the save's run-length encoding, so members are <b>appended,
    /// never renumbered</b>: <see cref="Remembered"/> keeps the 1 it had when it was called
    /// <c>Discovered</c>, and every existing save still reads.
    /// </summary>
    public enum DiscoveryState : byte
    {
        /// <summary>Never seen. Total black - there is nothing here to show, not even out of date.</summary>
        Unknown = 0,

        /// <summary>
        /// Seen at least once, and outside every observation radius right now. Veiled: what the
        /// player knew, not what is happening there.
        ///
        /// <b>This is the stored fact</b>, and storage means "has ever been seen" - whether it also
        /// happens to be observed this frame is not storage's business. Permanent: a discovered cell
        /// is never un-discovered, even once the Core's radius no longer covers it.
        /// </summary>
        Remembered = 1,

        /// <summary>
        /// Inside some observer's radius. Shown in full, and live.
        ///
        /// Never stored. Nothing is written when an observer arrives and nothing erased when it
        /// leaves - the cell simply stops being covered by anything in the observer list.
        /// </summary>
        Observed = 2
    }
}
