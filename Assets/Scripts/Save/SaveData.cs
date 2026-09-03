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
        public int Version = 1;
        public string SavedAtUtc;

        public int TerrainSeed;
        public int TerrainSize;
        public float TerrainScale;
        public float TerrainProportion;

        public float ComputeReserve;

        public float ResearchRp;
        public string ResearchActiveId;
        public float ResearchProgress;
        public List<string> ResearchUnlocked = new List<string>();

        public Dictionary<string, int> GlobalStock = new Dictionary<string, int>();

        public string CoreDefinitionId;
        public int CoreCellX;
        public int CoreCellY;
        public JObject CoreState = new JObject();

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
