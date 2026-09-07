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
// This file is the materialisation's own composition and has no business being included by anything
// else - the octave weights below ARE the effect's identity, and a shader outside the effect
// including them would be silently tuned by any change to them. The randomness underneath is a
// different matter: that lives in ValueNoise.hlsl, and anything at all may share it.

#include "ValueNoise.hlsl"

// Three octaves, then an analytic stretch. A single octave has one scale of detail: combined with
// a reveal gradient it can only produce a soft undulation, and raising the weight amplifies those
// waves instead of breaking the edge up into teeth.
//
// The stretch is not cosmetic. A weighted sum of three noises does not span 0-1: it concentrates
// around 0.5, with a usable range of roughly 0.25 to 0.75, so without the remap a weight would
// deliver about half the irregularity it claims. Normalizing over the whole image is not available
// to a fragment shader, hence fixed constants - which follow from the weights just above them, and
// have to be revisited with them.
float NanoFbm(float2 p)
{
    float fbm = 0.62 * ValueNoise2D(p)
              + 0.27 * ValueNoise2D(p * 2.2)
              + 0.11 * ValueNoise2D(p * 4.5);

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
