namespace Game.Gameplay.Missions
{
    /// <summary>
    /// What a mission is for. The two reconnaissances are one mission in two forms, told apart by the
    /// band their target falls in (SPEC_EXPEDITIONS.md §4); a recovery targets a point of interest
    /// already revealed, so it has no band at all.
    /// </summary>
    public enum MissionKind
    {
        /// <summary>Between the Core's current radius and the exploration threshold. Looking for a deposit.</summary>
        Prospection,

        /// <summary>Beyond the threshold. Looking for somewhere a secondary Core could stand.</summary>
        ExplorationLointaine,

        /// <summary>A point of interest a reconnaissance already found. Never a sector.</summary>
        Recuperation
    }

    /// <summary>
    /// SPEC_EXPEDITIONS.md §6, one to one.
    ///
    /// <b>Resolving is a moment, not a phase.</b> It has no duration: the outbound leg ends, the draw
    /// is applied, the return leg begins. It exists as a state so that the one frame in which the map
    /// changes is nameable.
    /// </summary>
    public enum MissionState
    {
        /// <summary>Outbound. The player sees the time remaining and nothing else.</summary>
        EnRoute,

        /// <summary>The instant the outcome is applied and the map moves.</summary>
        Resolution,

        /// <summary>Coming home. The map has already changed; the reward has not been paid yet.</summary>
        Retour,

        /// <summary>Home, outcome delivered, waiting to be read.</summary>
        Rapport,

        Close
    }

    /// <summary>Why a mission ended as it did - what §7.4's report is written from. Never a probability, always an event.</summary>
    public enum MissionOutcome
    {
        /// <summary>Everything asked for. For a reconnaissance with a probe this is the only possible map outcome.</summary>
        Reussite,

        /// <summary>The map came back, the harvest did not. The site is not consumed.</summary>
        RecolteManquee,

        /// <summary>Revealed, and nothing worth reporting was there. A prospection that found no deposit - not a failure, an answer.</summary>
        DecouverteVide
    }

    /// <summary>
    /// One mission in flight.
    ///
    /// <b>The outcome is drawn at launch and carried, not drawn on arrival.</b> Both would be
    /// reproducible in principle; storing the outcome makes surviving a save structural rather than a
    /// discipline. A draw made on arrival depends on whatever the world looks like at that moment, and
    /// a reload changes when that moment falls relative to everything else - so the guarantee would
    /// rest on nothing else having moved, which is exactly the kind of promise that quietly stops
    /// being true. Carrying the result, a reloaded mission cannot differ: there is nothing left to
    /// decide.
    ///
    /// The player learns none of it before the report. Knowing early costs nothing because nothing
    /// reads it.
    /// </summary>
    public sealed class MissionRuntime
    {
        /// <summary>Sequential, and its own draw index - so two missions of the same kind on the same sector still draw differently.</summary>
        public int Id { get; }

        public MissionKind Kind { get; }

        /// <summary>The sector aimed at. For a recovery, the sector whose point of interest is being exploited.</summary>
        public int TargetSector { get; }

        /// <summary>How long the whole round trip takes. Half outbound, half back.</summary>
        public float TotalSeconds { get; }

        public MissionState State { get; private set; }

        public float ElapsedSeconds { get; private set; }

        /// <summary>Drawn at launch, applied at resolution, told at the report.</summary>
        public MissionOutcome Outcome { get; }

        /// <summary>What the report will credit, in CU. Drawn at launch with everything else: it depends on the budget left at that moment, not on the one left when the probe lands.</summary>
        public float RewardCu { get; }

        /// <summary>Which probe is out on it. Its charge was spent at launch.</summary>
        public int ProbeIndex { get; }

        /// <summary>Kept for the day squads exist. Always 1 today - see the class summary of MissionSystem.</summary>
        public int Crew { get; }

        public MissionRuntime(int id, MissionKind kind, int targetSector, float totalSeconds,
            MissionOutcome outcome, float rewardCu, int probeIndex, int crew)
        {
            Id = id;
            Kind = kind;
            TargetSector = targetSector;
            TotalSeconds = totalSeconds > 0f ? totalSeconds : 1f;
            Outcome = outcome;
            RewardCu = rewardCu;
            ProbeIndex = probeIndex;
            Crew = crew < 1 ? 1 : crew;
            State = MissionState.EnRoute;
        }

        /// <summary>Seconds until the probe is home and the report can be read. What the top bar shows, and the only thing visible while En route.</summary>
        public float RemainingSeconds => TotalSeconds - ElapsedSeconds < 0f ? 0f : TotalSeconds - ElapsedSeconds;

        /// <summary>Half way: the outbound leg is done and the data has been transmitted.</summary>
        public bool OutboundComplete => ElapsedSeconds >= TotalSeconds * 0.5f;

        /// <summary>Advances the clock. Returns the state it is in afterwards, so the caller can act on the transition rather than poll.</summary>
        public MissionState Advance(float deltaSeconds)
        {
            if (State == MissionState.Close || State == MissionState.Rapport) return State;

            ElapsedSeconds += deltaSeconds < 0f ? 0f : deltaSeconds;

            if (State == MissionState.EnRoute && OutboundComplete) State = MissionState.Resolution;
            else if (State == MissionState.Resolution) State = MissionState.Retour;

            if (State == MissionState.Retour && ElapsedSeconds >= TotalSeconds) State = MissionState.Rapport;

            return State;
        }

        /// <summary>Marks the report read. A closed mission is dropped by the system and never saved.</summary>
        public void Close() => State = MissionState.Close;

        /// <summary>Used only when restoring: the clock and the state come from the save rather than from a launch.</summary>
        public void RestoreProgress(float elapsedSeconds, MissionState state)
        {
            ElapsedSeconds = elapsedSeconds < 0f ? 0f : elapsedSeconds;
            State = state;
        }
    }
}
