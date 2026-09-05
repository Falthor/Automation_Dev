using System;
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
    /// </summary>
    [Serializable]
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

        public List<DepositSaveData> Deposits = new List<DepositSaveData>();
        public List<BuildingSaveData> Buildings = new List<BuildingSaveData>();
    }

    [Serializable]
    public sealed class DepositSaveData
    {
        public string DefinitionId;
        public int OriginX;
        public int OriginY;
        public int RemainingQuantity;
    }

    [Serializable]
    public sealed class BuildingSaveData
    {
        public string DefinitionId;
        public int CellX;
        public int CellY;
        public int FacingRotation;
        public JObject State = new JObject();
    }
}
