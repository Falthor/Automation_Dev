using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// What grows on the ground, how thickly, and how far from the camera it exists as an object.
    ///
    /// <b>A fourth settings asset, deliberately.</b> The rule the large-map directive is after is that
    /// no value exists in two places - one value, one owner, no copy. Four disjoint assets create no
    /// second path; a single catch-all one would be worse, because nobody opens it, everything gets
    /// added to it, and settings with different owners end up mixed.
    ///
    /// <b>Density is per chunk, never a total.</b> A global count of objects cannot follow a change of
    /// map size: 500 rocks is a scatter on a 300-cell map and invisible on a 10 000-cell one. Per
    /// chunk, the same number means the same thing at any size, and a test pins that.
    /// </summary>
    [CreateAssetMenu(fileName = "DecorSettings", menuName = "Game/World/Decor Settings")]
    public sealed class DecorSettings : ScriptableObject
    {
        /// <summary>
        /// One kind of thing that grows. The band weights are what makes a world read as a place
        /// rather than as noise - rocks in the rocky ground - and they index the ground material's own
        /// texture slots, so slot 0 here is slot 0 there.
        /// </summary>
        [System.Serializable]
        public sealed class Kind
        {
            [SerializeField] string id = "kind";
            [SerializeField] Sprite[] sprites = System.Array.Empty<Sprite>();

            /// <summary>Whether its art rises above its base. Raised decor goes in the sorted band and is ranked by DepthSortLadder; flat decor takes a fixed ground-band order.</summary>
            [SerializeField] bool raised;

            /// <summary>Relative likelihood in each ground band. All zero means the kind never appears.</summary>
            [SerializeField] float[] bandWeights = { 1f, 1f, 1f };

            [SerializeField] Vector2 scaleRange = new Vector2(0.8f, 1.3f);

            public string Id => id;
            public Sprite[] Sprites => sprites;
            public bool Raised => raised;
            public float[] BandWeights => bandWeights;
            public Vector2 ScaleRange => scaleRange;
        }

        [Header("Densité")]

        /// <summary>
        /// Average number of decor items in one chunk. At the shipped chunk of 64 cells that is one
        /// item per <c>4096 / this</c> cells, and it means the same thing on a map of any size.
        /// </summary>
        [SerializeField, Min(0f)] float itemsPerChunk = 40f;

        /// <summary>
        /// How close to a ground-band boundary a spot may be and still grow something. The CPU biome
        /// port agrees with the shader everywhere except within a rounding of a boundary; skipping a
        /// thin strip there means the rare disagreement lands on a cell where nothing grows anyway.
        /// Zero disables the skip.
        /// </summary>
        [SerializeField, Min(0f)] float bandEdgeExclusion = 0.004f;

        [Header("Fenêtre")]

        /// <summary>
        /// How far past the widest possible view decor is instantiated, in cells. Bigger means fewer
        /// window moves and more live objects; the window follows the camera the way the fog's does.
        /// </summary>
        [SerializeField, Min(0)] int windowMarginCells = 48;

        /// <summary>How many spare objects to keep rather than destroy. A pan at full zoom-out crosses many chunks, and instantiating on every frame is what produces stutter.</summary>
        [SerializeField, Min(0)] int poolSize = 512;

        [Header("Espèces")]
        [SerializeField] Kind[] kinds = System.Array.Empty<Kind>();

        public float ItemsPerChunk => itemsPerChunk;
        public float BandEdgeExclusion => bandEdgeExclusion;
        public int WindowMarginCells => windowMarginCells;
        public int PoolSize => poolSize;
        public Kind[] Kinds => kinds;

        /// <summary>
        /// The band weights of every kind, as a plain array - what DecorRuntime is built from.
        /// Game.Grid must not depend on Game.Data, so the derivation is handed numbers rather than
        /// this asset, exactly as TerrainRuntime is.
        /// </summary>
        public float[][] BandWeightsPerKind()
        {
            var weights = new float[kinds.Length][];
            for (int i = 0; i < kinds.Length; i++) weights[i] = kinds[i].BandWeights;
            return weights;
        }

        void OnValidate()
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                float[] w = kinds[i].BandWeights;
                float total = 0f;
                if (w != null)
                {
                    for (int b = 0; b < w.Length; b++) total += Mathf.Max(0f, w[b]);
                }

                if (total <= 0f)
                {
                    Debug.LogWarning($"{name}: kind '{kinds[i].Id}' has no positive band weight, so it can never appear.", this);
                }
            }
        }
    }
}
