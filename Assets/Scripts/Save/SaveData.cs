using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Game.Save
{
    /// <summary>
    /// Root save file contents (CONTRACTS.md §14). Plain data only - no Unity/gameplay types, so
    /// Game.Save has no dependency on Game.Presentation/Game.Gameplay; the Presentation layer
    /// (GameRuntime) is the only place that knows how to turn this into/from live runtime state.
    /// Per-building free-form state is stored as JObject rather than a typed DTO per building
    /// type, since each BuildingRuntime subclass already owns its own CaptureState()/RestoreState()
    /// pair - the save layer never interprets that blob, only stores and returns it verbatim.
    ///
    /// <b>Deliberately not [Serializable].</b> The save is written and read by Newtonsoft alone
    /// (SaveService), which does not consult that attribute, and nothing here ever passes through
    /// JsonUtility or an inspector field. Carrying it anyway claimed these types were Unity-
    /// serializable, which made the analyzer rightly point at the three JObject fields and the
    /// nullable int - none of which Unity can serialize - for four warnings about a serializer that
    /// is not involved. The format is pinned by SaveFormatTests.
    /// </summary>
    public sealed class SaveData
    {
        /// <summary>
        /// Bumped whenever a change to this format (or to what a per-building CaptureState blob
        /// is expected to contain) would make an older save meaningfully different to interpret -
        /// TASK_03_DATACENTER.md's decision: SaveService.Load refuses a save whose Version
        /// doesn't match this exactly, rather than attempting to load it with defaults filled in.
        /// A per-building blob missing an individual key still falls back gracefully (CONTRACTS.md
        /// §14) - Version is a coarser, all-or-nothing gate for changes too structural for that,
        /// like this task's Data Center Capture/Restore reshaping and Research's RP-to-CU switch.
        /// </summary>
        public const int CurrentVersion = 3;

        public int Version = CurrentVersion;
        public string SavedAtUtc;

        public int TerrainSeed;
        public int TerrainSize;
        public float TerrainScale;
        public float TerrainProportion;

        /// <summary>
        /// Which cells the player has discovered, run-length encoded in row-major order by
        /// <c>Game.Grid.DiscoveryRuntime.CaptureState()</c>.
        ///
        /// Player progress, not a rebuildable cache: what has been explored beyond the Core's reach
        /// cannot be derived from anything else in the file. A plain string rather than a JObject
        /// because Game.Grid owns the state and has no JSON dependency; run-length rather than one
        /// entry per cell because the file is written indented and the map is 90 000 cells.
        ///
        /// Additive, with a per-field fallback (absent restores as a wholly undiscovered map, which
        /// the Core's radius immediately writes its own disc back into), so it does not bump
        /// <see cref="CurrentVersion"/> - see the Core's action radius and BuildingCap before it.
        /// </summary>
        public string Discovered;

        /// <summary>
        /// The cells whose decor the player has cleared, as a comma-separated list of indices
        /// (DecorRuntime.CaptureState).
        ///
        /// <b>Only the removals.</b> What grows is a pure function of the world seed and re-derives
        /// itself at load, so storing it would be storing what the seed already says. What the seed
        /// cannot say is that a rock was cleared to make room for a building - and without this, that
        /// rock grows back the moment the camera leaves and returns, which is the whole trap the
        /// delta set exists for.
        ///
        /// A plain string for the same reason as Discovered: Game.Grid references only Game.Core and
        /// Game.Data, and a JObject would give it a JSON dependency it has no other use for. No
        /// Version bump - an additive field with a per-field fallback, like Discovered and BuildingCap
        /// before it. A save predating it restores as a world nobody has cleared anything in, which is
        /// exactly what it recorded.
        /// </summary>
        public string DecorRemoved;

        /// <summary>
        /// The expedition system: missions in flight with their clock and their already-drawn outcome,
        /// the charges left on each probe, and the sites already recovered
        /// (<c>MissionSystem.CaptureState</c>).
        ///
        /// <b>The outcome is stored, not a seed.</b> A mission saved in flight has to come back
        /// identical - neither redrawn nor lost - and carrying the result is what makes that
        /// structural: there is nothing left to decide at landing, so nothing a reload can decide
        /// differently.
        ///
        /// Reports waiting to be read are deliberately absent: an unread one is delivered again on the
        /// next landing rather than lost. No <c>Version</c> bump - an additive field with a per-field
        /// fallback, which restores as a game whose probes have not yet arrived.
        /// </summary>
        public JObject Missions;

        /// <summary>
        /// The expedition zones: which one the player chose, the radius the six were laid out from, and
        /// the sites whose state has moved off what the derivation gives
        /// (<c>ExpeditionZoneSystem.CaptureState</c>).
        ///
        /// <b>Almost nothing, deliberately.</b> A zone's bounds, its sites and their positions are pure
        /// functions of the world seed, so they come back on their own - what cannot be re-derived is
        /// the choice, the ground the layout started from, and which sites the player has turned up or
        /// spent. The inner radius travels because the layout was frozen against it: re-reading the
        /// Core's radius at load would re-cut the zones under a run already half mapped.
        ///
        /// No <c>Version</c> bump - an additive field with a per-field fallback, which restores as a run
        /// with the six zones still on offer and nothing touched.
        /// </summary>
        public JObject ExpeditionZones;

        public float ComputeReserve;

        public string ResearchActiveId;
        public float ResearchProgress;
        public List<string> ResearchQueue = new List<string>();
        public List<string> ResearchUnlocked = new List<string>();

        /// <summary>
        /// Construction sites (their remaining bill of materials and their reservations) and both
        /// builder robots (position, state, cargo), plus any repatriation still in flight -
        /// TASK_05_ROBOT_CONSTRUCTEUR.md §8. An opaque blob owned by
        /// Game.Gameplay.Sites.ConstructionSiteSystem's own Capture/Restore pair, like every
        /// per-building blob. Absent (a save from before that task) restores as two idle robots
        /// with no site, without throwing.
        ///
        /// There is deliberately no GlobalStock field any more: it holds nothing to serialize -
        /// it is recomputed at load from the real containers (CONTRACTS.md §15).
        /// </summary>
        public JObject ConstructionSites;

        /// <summary>Which Core directive is current (CoreDirectiveSystem.CaptureState). Absent in an older save, which restores as "the first one", the same state a new game starts in.</summary>
        public JObject CoreDirectives;

        public string CoreDefinitionId;
        public int CoreCellX;
        public int CoreCellY;
        public JObject CoreState = new JObject();

        /// <summary>
        /// Current building slot cap (TASK_04_PLAFOND_RAYON.md §3/§6) - nullable so an absent key
        /// (a save from before this task) is distinguishable from an explicit value and falls back
        /// to ConstructionService.DefaultBuildingCap, never to 0. The Core's own current action
        /// radius is not a separate field here - it already round-trips through CoreState via
        /// CoreRuntime.CaptureState/RestoreState, alongside cuTimer and inventory contents.
        /// </summary>
        public int? BuildingCap;

        /// <summary>
        /// Seconds of simulated time this run has been played (Game.Gameplay.Session.PlayClock).
        /// Nullable so a save from before the clock existed restores as zero rather than as a run
        /// that has never been played being indistinguishable from one whose key is missing - the
        /// same convention BuildingCap uses.
        /// </summary>
        public float? PlayTimeSeconds;

        public List<DepositSaveData> Deposits = new List<DepositSaveData>();
        public List<BuildingSaveData> Buildings = new List<BuildingSaveData>();
    }

    /// <summary>
    /// Where a deposit is and what it is - all of it. A deposit never runs out
    /// (ALIGNEMENT_PROJET.md §8), so it has no mutable state to carry: no quantity is written, and
    /// the RemainingQuantity an older save still holds is simply ignored, which is exactly right
    /// now that the answer is "infinite" whatever the number said.
    /// </summary>
    public sealed class DepositSaveData
    {
        public string DefinitionId;
        public int OriginX;
        public int OriginY;
    }

    public sealed class BuildingSaveData
    {
        public string DefinitionId;
        public int CellX;
        public int CellY;
        public int FacingRotation;
        public JObject State = new JObject();
    }
}
