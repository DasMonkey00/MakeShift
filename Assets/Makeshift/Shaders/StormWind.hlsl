// Storm wind for the Toon Fantasy Nature foliage shaders (grass and vegetation). The StormController sets
// these two globals; both are zero in fair weather, which leaves the pack's wind as it was.
#ifndef MAKESHIFT_STORM_WIND
#define MAKESHIFT_STORM_WIND

float _MkWindOffset; // extra time, so the sway runs faster without jumping
float _MkWindBoost;  // extra strength
float _MkStormDark;  // 0 = fair weather, 1 = full storm dark
float _MkWindOff;    // 1 = no sway

float MkWindTime()
{
    return _TimeParameters.x + _MkWindOffset;
}

// Strengthens the pack's sway for a storm. The sway is a per-vertex offset in any direction, so only its
// sideways part (in the world) may grow: scaling all of it stretches blades to twice their height.
float3 MkWindShape(float3 sway)
{
    // The wrist menu's wind switch. This stills the foliage; the pack's noise is still sampled.
    if (_MkWindOff > 0.5) return float3(0.0, 0.0, 0.0);
    float storm = saturate(_MkWindBoost);
    if (storm <= 0.0) return sway;

    float3 world = mul((float3x3)GetObjectToWorldMatrix(), sway);
    float calm = length(world.xz);
    // At most this far sideways in metres, unless the fair weather sway already went further.
    float pushed = max(calm, min(calm * (1.0 + 1.5 * _MkWindBoost), 0.3));
    world.xz *= pushed / max(calm, 0.0001);
    // No extra lift, and the further a tip is pushed the lower it dips, as a bent stem does.
    world.y = lerp(world.y, min(world.y, 0.0), storm) - (pushed - calm) * 0.35;
    return mul((float3x3)GetWorldToObjectMatrix(), world);
}

// The grass highlights are added unlit, so they have to be turned down by hand when the sky goes dark.
float MkHighlightGain()
{
    return 1.0 - 0.92 * _MkStormDark;
}

#endif
