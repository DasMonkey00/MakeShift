using UnityEngine;

namespace Makeshift.Fire
{
    /// <summary>
    /// Burns, but is never used up as campfire fuel: a torch is lit at the fire, not fed to it.
    /// Without this the campfire snaps the thing into the pit and keeps it.
    /// </summary>
    public class NotFuel : MonoBehaviour { }
}
