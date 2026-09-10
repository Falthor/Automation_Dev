using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// How the map is cut up.
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
        /// The sector: the internal unit contents and materialisation work in. 16 so that 4x4 of them
        /// tile a chunk exactly - the old 12 fell on no chunk boundary at all.
        /// </summary>
        [SerializeField, Min(1)] int sectorSizeCells = 16;

        public int ChunkSizeCells => chunkSizeCells;
        public int SectorSizeCells => sectorSizeCells;

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

        }
    }
}
