using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// How the map is cut up, and how dangerous each part of it reads.
    ///
    /// <b>The one place these numbers exist.</b> They used to be constants in the code, which is the
    /// third path the large-map directive forbids: the generator reads its values from a settings
    /// object, the new-game screen edits that object, and a developer tuning balance edits its
    /// defaults. A constant in a generator is none of those, and it is the thing that makes changing
    /// one number require changing a second one somewhere else.
    ///
    /// The map's own size is deliberately NOT here - it lives on TerrainGenerationSettings, which is
    /// what actually builds the world. Copying it would be exactly the duplication this asset exists
    /// to prevent; the sector grid is handed the map size and the sector size separately.
    /// </summary>
    [CreateAssetMenu(fileName = "SectorSettings", menuName = "Game/World/Sector Settings")]
    public sealed class SectorSettings : ScriptableObject
    {
        [Header("Découpage")]

        /// <summary>
        /// The chunk: the one division everything region-shaped aligns on - terrain generation,
        /// discovery storage, the map image, and the ore-density block. Two competing divisions would
        /// produce permanent misalignments.
        /// </summary>
        [SerializeField, Min(1)] int chunkSizeCells = 64;

        /// <summary>
        /// The sector: what a mission is aimed at. 16 so that 4x4 of them tile a chunk exactly - the
        /// old 12 fell on no chunk boundary at all. Its inscribed disc has a radius of 8 and reveals
        /// 201 cells.
        /// </summary>
        [SerializeField, Min(1)] int sectorSizeCells = 16;

        /// <summary>
        /// How wide a <b>named region</b> should be, in cells. Sectors inside one share a region name
        /// and differ by a coordinate suffix, so this is really "how much ground carries one name".
        ///
        /// An intent, not the answer: a map big enough that this would need more regions than there
        /// are names gets wider ones instead (SectorCatalog.MaxRegionsPerAxis). At the shipped 10 000
        /// it is the cap that decides, giving 27 regions of 371 cells across.
        /// </summary>
        [SerializeField, Min(1)] int preferredRegionSizeCells = 384;

        [Header("Risque, en cases depuis le Noyau")]

        /// <summary>
        /// Up to here, a sector reads as the player's own ground. Near the Core's initial radius,
        /// because that is where the player starts - but it is a <b>balance</b> value, not a derived
        /// one: deriving it from the radius would couple danger to how far a Core happens to reach,
        /// which is a different idea entirely.
        /// </summary>
        [SerializeField, Min(0f)] float lowRiskWithinCells = 40f;

        /// <summary>The middle band: ground a robot reaches routinely.</summary>
        [SerializeField, Min(0f)] float moderateRiskWithinCells = 250f;

        /// <summary>As far out as a secondary Core's own territory reaches. Past this, everything is Critical.</summary>
        [SerializeField, Min(0f)] float highRiskWithinCells = 330f;

        public int ChunkSizeCells => chunkSizeCells;
        public int SectorSizeCells => sectorSizeCells;
        public int PreferredRegionSizeCells => preferredRegionSizeCells;

        public float LowRiskWithinCells => lowRiskWithinCells;
        public float ModerateRiskWithinCells => moderateRiskWithinCells;
        public float HighRiskWithinCells => highRiskWithinCells;

        /// <summary>How many sectors tile a chunk along one axis. 4 at the defaults.</summary>
        public int SectorsPerChunkAxis => Mathf.Max(1, chunkSizeCells / Mathf.Max(1, sectorSizeCells));

        /// <summary>
        /// Whether sectors tile chunks exactly. False means every rule that works per chunk and every
        /// rule that works per sector disagree about where their boundaries are - which is the one
        /// thing choosing a single division was meant to prevent.
        /// </summary>
        public bool SectorsTileChunksExactly => sectorSizeCells > 0 && chunkSizeCells % sectorSizeCells == 0;

        /// <summary>
        /// Warns on an inconsistent setting - but only as a convenience, and it is not the guard.
        /// Unity runs this when it imports or reloads the asset, <b>not</b> while a value is being
        /// typed into the Inspector, so an inconsistent value can sit in the project for a whole
        /// session unseen. SectorSettingsTests is what actually catches it, on every suite run and
        /// whatever put the value there.
        /// </summary>
        void OnValidate()
        {
            if (!SectorsTileChunksExactly)
            {
                Debug.LogWarning($"{name}: a sector of {sectorSizeCells} does not divide a chunk of {chunkSizeCells}. "
                    + "Sector and chunk boundaries will not line up.", this);
            }

            if (lowRiskWithinCells > moderateRiskWithinCells || moderateRiskWithinCells > highRiskWithinCells)
            {
                Debug.LogWarning($"{name}: the risk thresholds are out of order - each has to be at least the previous one.", this);
            }
        }
    }
}
