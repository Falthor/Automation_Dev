using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static, deterministic terrain generation parameters. Gameplay-relevant (which terrain
    /// type each cell authoritatively is) - not visual/rendering settings, which belong to
    /// Presentation instead.
    /// </summary>
    [CreateAssetMenu(fileName = "TerrainGenerationSettings", menuName = "Game/Terrain/Generation Settings")]
    public sealed class TerrainGenerationSettings : ScriptableObject
    {
        [SerializeField, Min(2)] int size = 60;

        /// <summary>
        /// The world's one seed. <b>Everything derived in this project hangs off it</b> - terrain, a
        /// sector's name, risk and contents, a zone's site counts and their positions - so two runs
        /// sharing it are the same world down to the last mark on the map.
        ///
        /// Used only when <see cref="RandomiseSeedEachRun"/> is off; a run draws its own otherwise, and
        /// carries it in the save.
        /// </summary>
        [SerializeField] int seed;

        /// <summary>
        /// Whether a new run draws its own seed rather than taking the one above.
        ///
        /// <b>On, because the value above is 0 and every new game was therefore the same world</b> - the
        /// same terrain, the same sectors, and the six zones' sites at the same coordinates every time.
        /// Turn it off to reproduce a run: pin the seed and the whole world comes back, which is what
        /// makes a bug on a particular map findable at all.
        ///
        /// This is the one draw in the project that is not deterministic, and it is not a derivation: it
        /// happens once, at the start of a run, and is written to the save immediately. Everything after
        /// it goes through <c>DeterministicHash</c>.
        /// </summary>
        [SerializeField] bool randomiseSeedEachRun = true;

        [SerializeField, Min(0.01f)] float terrainScale = 45f;
        [SerializeField, Range(0f, 1f)] float proportion = 0.30f;

        public int Size => size;
        public int Seed => seed;
        public bool RandomiseSeedEachRun => randomiseSeedEachRun;
        public float TerrainScale => terrainScale;
        public float Proportion => proportion;
    }
}
