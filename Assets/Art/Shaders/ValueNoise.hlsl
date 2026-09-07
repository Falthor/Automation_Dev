#ifndef VALUE_NOISE_INCLUDED
#define VALUE_NOISE_INCLUDED

// A 2D value noise primitive, and nothing built on top of it. Every shader that needs noise is
// expected to include this and compose its own layers.
//
// The split is deliberate. What is genuinely common between two effects that both jitter an edge is
// the *source of randomness* - hashing a position into a well-distributed, tileless value. How many
// octaves that randomness is stacked into, at what weights, and how the result is remapped are not
// common at all: they are what gives one effect sub-cell teeth and another a wobble spanning several
// cells. Sharing the composition too would mean tuning one effect silently moves the other.
//
// Always sampled in WORLD space by every caller, never in UVs: the pattern stays pinned to the
// terrain, so it survives a sprite sheet frame change and two neighbouring users share one
// continuous field instead of restarting the same pattern side by side.

// Dave Hoskins' "hash without sine" rather than a hand-rolled one. A cheaper hand-rolled hash tried
// during development showed clear periodic banding at some frequency/position combinations - a
// regular ladder/grid pattern instead of true randomness. Prefer a well-tested hash over inventing
// a new one.
//
// This function is the whole of the protection against that, which is why it is the part worth
// sharing and worth never reimplementing per shader: it once existed as three verbatim copies, and
// a fix to the defect it guards against would have corrected one of them.
//
// Callers should also keep their seeds SMALL. The hash multiplies its input inside a frac() and
// float32 carries ~7 significant digits: a large seed swamps the position-dependent bits and
// collapses the field to a near-constant. TerrainView draws Random.Range(0, 10000) for that reason.
float Hash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Smoothstep-interpolated value noise over the unit lattice. Output is 0-1, and a weighted sum of
// several of these does NOT span 0-1 - it concentrates around 0.5. Callers stacking octaves have to
// remap the result themselves, with constants that follow from their own weights.
float ValueNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));

    float2 u = f * f * (3.0 - 2.0 * f);

    return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

#endif
