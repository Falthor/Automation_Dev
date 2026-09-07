using UnityEngine;

namespace Game.Grid
{
    /// <summary>
    /// Which ground texture the shader picks at a world position, computed on the CPU.
    ///
    /// A port of Custom/ShadedGroundTiled's base-layer classification, kept in step with it by hand.
    /// It exists so that anything scattered on the ground - decor, and later anything else that wants
    /// to know what it is standing on - can ask without a GPU readback.
    ///
    /// <b>In float, deliberately, and this is the whole subtlety.</b> An earlier CPU port was
    /// abandoned because it diverged from the shader; the natural reading of that failure is
    /// "float32 was not precise enough, use double". <b>Measured, that reading is wrong</b>: a double
    /// port disagrees with the GPU on 43 % of samples - near chance - while this float one disagrees
    /// on 1 in 3 000.
    ///
    /// The reason is that the hash is <b>chaotic, not merely imprecise</b>. Its last step takes the
    /// fraction of a value of magnitude ~3 000, so a relative error of 1e-7 in the input is already
    /// an absolute error of 1e-4 there, and the fraction turns that into an O(1) difference. At one
    /// lattice point, double gives 0.962 where float gives 0.210. Being <i>more</i> precise than the
    /// GPU therefore makes the answer <i>more</i> wrong, not less: what matters is not accuracy but
    /// <b>agreeing with the shader</b>, so the port matches its width and its order of operations.
    ///
    /// The residual 1 in 3 000 are cells sitting on a band boundary, where a last-bit difference
    /// tips the comparison. The consequence is one scattered rock taking the other biome's tint on a
    /// transition that is itself noisy - invisible, and not the failure that made the earlier port
    /// unusable, which flipped the classification of the ground itself.
    ///
    /// Do not "clean this up" into double, and do not reorder the arithmetic. Both would silently
    /// unmoor it from the shader. DecorBiomeAgreementTests measures the rate against the real
    /// material rather than assuming it.
    /// </summary>
    public sealed class BiomeField
    {
        readonly Vector2 _variationOrigin;
        readonly float _cellSize;
        readonly float _seed;
        readonly float[] _weights;
        readonly int _bandCount;

        /// <summary>
        /// Takes the shader's own parameters as plain values - Game.Grid must not depend on
        /// Game.Data or on Presentation. GameRuntime unpacks them from the ground material, the same
        /// way it unpacks TerrainGenerationSettings for TerrainRuntime.
        /// </summary>
        public BiomeField(Vector2 variationOrigin, float biomeCellSize, float biomeSeed, float[] weights, int bandCount)
        {
            _variationOrigin = variationOrigin;
            _cellSize = Mathf.Max(biomeCellSize, 0.0001f);
            _seed = biomeSeed;
            _bandCount = Mathf.Clamp(bandCount, 1, 3);

            _weights = new float[3];
            for (int i = 0; i < 3; i++)
            {
                _weights[i] = weights != null && i < weights.Length ? Mathf.Max(weights[i], 0.0001f) : 0.0001f;
            }
        }

        /// <summary>The band index the shader would show at this world position - 0, 1 or 2, indexing the same texture slots the ground material uses.</summary>
        public int BandAt(Vector2 worldPos) => PickBand(FieldAt(worldPos));

        /// <summary>The raw field value, 0 to 1. Exposed so a caller can tell how close to a band boundary a position is - decor can then skip the cells where the CPU and the GPU are most likely to disagree.</summary>
        public float FieldAt(Vector2 worldPos)
        {
            float px = (worldPos.x - _variationOrigin.x) / _cellSize + _seed;
            float py = (worldPos.y - _variationOrigin.y) / _cellSize + (-_seed * 1.37f);

            return ValueNoise(px, py);
        }

        /// <summary>How far this position sits from the nearest band boundary, in field units. Near zero means the shader and this port are one rounding apart from disagreeing.</summary>
        public float DistanceToBandEdge(Vector2 worldPos)
        {
            float field = FieldAt(worldPos);

            float total = 0f;
            for (int k = 0; k < _bandCount; k++) total += _weights[k];

            float nearest = Mathf.Min(field, 1f - field);
            float cumulative = 0f;
            for (int j = 0; j < _bandCount - 1; j++)
            {
                cumulative += _weights[j] / total;
                nearest = Mathf.Min(nearest, Mathf.Abs(field - cumulative));
            }

            return nearest;
        }

        /// <summary>The shader's PickBand: the 0-1 field is cut into bands sized by the weights, and this says which one a value lands in.</summary>
        int PickBand(float fieldValue)
        {
            float total = 0f;
            for (int k = 0; k < _bandCount; k++) total += _weights[k];

            float cumulative = 0f;
            for (int j = 0; j < _bandCount; j++)
            {
                float next = cumulative + _weights[j] / total;
                if (fieldValue < next || j == _bandCount - 1) return j;
                cumulative = next;
            }

            return _bandCount - 1;
        }

        // ---- The shader's ValueNoise2D and Hash21, transcribed ----
        //
        // A transcription rather than a re-derivation: same constants, same order, same width, so
        // that reading the two side by side shows they are one function. Every one of those three is
        // load-bearing - see the class summary for what changing the width alone costs.

        static float Frac(float v) => v - Mathf.Floor(v);

        static float Hash21(float px, float py)
        {
            float p3x = Frac(px * 0.1031f);
            float p3y = Frac(py * 0.1031f);
            float p3z = Frac(px * 0.1031f);

            float dot = p3x * (p3y + 33.33f) + p3y * (p3z + 33.33f) + p3z * (p3x + 33.33f);
            p3x += dot;
            p3y += dot;
            p3z += dot;

            return Frac((p3x + p3y) * p3z);
        }

        static float ValueNoise(float px, float py)
        {
            float ix = Mathf.Floor(px);
            float iy = Mathf.Floor(py);
            float fx = px - ix;
            float fy = py - iy;

            float a = Hash21(ix, iy);
            float b = Hash21(ix + 1f, iy);
            float c = Hash21(ix, iy + 1f);
            float d = Hash21(ix + 1f, iy + 1f);

            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);

            return a + (b - a) * ux + (c - a) * uy * (1f - ux) + (d - b) * ux * uy;
        }
    }
}
