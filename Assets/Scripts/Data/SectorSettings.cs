using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// How the map is cut up, and how much derived ore it holds.
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

        [Header("Gisements dérivés")]

        /// <summary>
        /// One sector in this many carries an ore cluster. Everything else derives nothing.
        ///
        /// <b>This is the number that decides how often exploring pays.</b> It used to be four
        /// sectors in eight, and a wreck or a nest produced ore as well, so seven sectors in eight
        /// held some - and a robot materialises the 3x3 block around every sector it crosses. One
        /// sortie turned up dozens of scattered tiles, which is not a find, it is scenery.
        /// </summary>
        [SerializeField, Min(1)] int oreClusterOneSectorIn = 12;

        /// <summary>
        /// How many tiles a cluster holds just outside the Core's reach, at least and at most.
        /// Contiguous, so this is the size of a patch rather than a count of scattered cells - a
        /// cluster is meant to be worth a trip and an Extractor, and six cells spread over 256 are
        /// neither.
        /// </summary>
        [SerializeField, Min(1)] int oreClusterMinTiles = 6;

        [SerializeField, Min(1)] int oreClusterMaxTiles = 10;

        /// <summary>
        /// And how many at the far limit, with everything in between interpolated
        /// (<see cref="OreClusterProfile"/>). Distance is the only thing exploring costs, so it has
        /// to be the thing that pays; a flat size makes the far half of the map the near half with a
        /// longer walk.
        /// </summary>
        [SerializeField, Min(1)] int oreClusterFarMinTiles = 10;

        [SerializeField, Min(1)] int oreClusterFarMaxTiles = 15;

        public int ChunkSizeCells => chunkSizeCells;
        public int SectorSizeCells => sectorSizeCells;

        public int OreClusterOneSectorIn => oreClusterOneSectorIn;
        public int OreClusterMinTiles => oreClusterMinTiles;
        public int OreClusterMaxTiles => Mathf.Max(oreClusterMinTiles, oreClusterMaxTiles);
        public int OreClusterFarMinTiles => oreClusterFarMinTiles;
        public int OreClusterFarMaxTiles => Mathf.Max(oreClusterFarMinTiles, oreClusterFarMaxTiles);

        /// <summary>
        /// The cluster figures as one value, with the two radii the ramp runs between.
        ///
        /// <b>The radii are not this asset's to hold.</b> The near one is the Core's furthest reach
        /// and the far one is how far a robot wanders - both belong to the systems that own them, and
        /// a copy here could only ever disagree. They are handed in.
        /// </summary>
        public OreClusterProfile ClusterProfile(float nearRadiusCells, float farRadiusCells)
            => new OreClusterProfile(OreClusterOneSectorIn,
                OreClusterMinTiles, OreClusterMaxTiles,
                OreClusterFarMinTiles, OreClusterFarMaxTiles,
                nearRadiusCells, farRadiusCells);

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
