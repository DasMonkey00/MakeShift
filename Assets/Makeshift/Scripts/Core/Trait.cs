using System;

namespace Makeshift.Core
{
    /// <summary>
    /// Simple physical traits. Interactions come from traits, not recipes,
    /// so combinations nobody planned still behave sensibly.
    /// </summary>
    [Flags]
    public enum Trait
    {
        None      = 0,
        Burns     = 1 << 0, // catches fire, spreads fire
        Conducts  = 1 << 1, // carries power (wire, metal)
        Powers    = 1 << 2, // supplies power (battery, generator)
        Blows     = 1 << 3, // pushes air (leaf blower, fan, hair dryer)
        Floats    = 1 << 4, // floats in water
        Sticks    = 1 << 5, // adheres (tape, resin, magnet)
        Sharp     = 1 << 6, // cuts and pierces
        Heavy     = 1 << 7, // hits hard, weighs things down
    }
}
