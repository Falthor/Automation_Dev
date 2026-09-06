#ifndef NANO_NOISE_INCLUDED
#define NANO_NOISE_INCLUDED

// The one grain of the whole materialisation effect, shared by every layer that draws a
// conversion front: Custom/BuildDissolve (the building), Custom/GroundCoverage (the ground) and
// Custom/BuildingGroundSlab (the concrete revealed behind it).
//
// Shared as an include rather than copied per shader because the layers are supposed to look like
// ONE process. Three copies of a noise function are three things that can be tuned apart, and the
// moment two of them differ the effect reads as several animations that happen to coincide.
//
// Custom/FogOfWar also calls NanoFrontJitter, and is NOT part of that process. It is here for the
// hash and the octaves only - a fourth hand-rolled hash is what produced the banding this file
// exists to avoid - and it runs at its own scale and weight, some thirty times coarser, because a
// fog border has to be readable across cells rather than to have sub-cell teeth. Changing the
// octave weights below therefore moves the fog's border as well as the materialisation's grain;
// changing either caller's scale or weight does not.
//
// Always sampled in WORLD space by every caller, never in UVs: the pattern stays pinned to the
// terrain, so it survives a sprite sheet frame change and two neighbouring sites share one
// continuous field instead of restarting the same pattern side by side.

// Dave Hoskins' "hash without sine" rather than a hand-rolled one, which showed periodic banding.
float NanoHash21(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float NanoValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float a = NanoHash21(i);
    float b = NanoHash21(i + float2(1.0, 0.0));
    float c = NanoHash21(i + float2(0.0, 1.0));
    float d = NanoHash21(i + float2(1.0, 1.0));

    float2 u = f * f * (3.0 - 2.0 * f);

    return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

// Three octaves, then an analytic stretch. A single octave has one scale of detail: combined with
// a reveal gradient it can only produce a soft undulation, and raising the weight amplifies those
// waves instead of breaking the edge up into teeth.
//
// The stretch is not cosmetic. A weighted sum of three noises does not span 0-1: it concentrates
// around 0.5, with a usable range of roughly 0.25 to 0.75, so without the remap a weight would
// deliver about half the irregularity it claims. Normalizing over the whole image is not available
// to a fragment shader, hence fixed constants.
float NanoFbm(float2 p)
{
    float fbm = 0.62 * NanoValueNoise(p)
              + 0.27 * NanoValueNoise(p * 2.2)
              + 0.11 * NanoValueNoise(p * 4.5);

    return saturate((fbm - 0.25) / 0.5);
}

// How far the noise displaces a front at this world position, in threshold units. Centred on zero
// (the raw noise is 0-1), so the weight widens the wobble symmetrically instead of also pushing the
// whole front one way. Callers SUBTRACT it from their signed distance to the front, which is the
// same thing as adding it to the threshold.
//
// For the two ground layers only. The building does NOT use this: it holds its reveal gradient and
// its noise in the same units at the same time, so it can blend them (base*(1-w) + noise*w), which
// is a slightly different thing - the jitter is correlated with the gradient, so its teeth taper off
// near the very start and end of the reveal. The ground only ever has a precomputed distance to the
// front, never the gradient and the progress apart, so it can only add. At the weights in use the
// two are visually the same, and sharing NanoFbm above is what actually makes the grain identical.
float NanoFrontJitter(float2 worldPos, float noiseScale, float noiseWeight)
{
    return (NanoFbm(worldPos * noiseScale) - 0.5) * noiseWeight;
}

#endif
