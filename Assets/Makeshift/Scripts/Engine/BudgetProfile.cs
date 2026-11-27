using UnityEngine;

namespace Makeshift.Engine
{
    /// <summary>
    /// Every number the World Budget applies, in one asset. The wrist menu changes the live values; this asset holds
    /// the starting point. Defaults are tuned for the Quest 3S 90 fps budget (see CLAUDE.md), not for the PC.
    /// </summary>
    [CreateAssetMenu(menuName = "Makeshift/Budget Profile", fileName = "BudgetProfile")]
    public class BudgetProfile : ScriptableObject
    {
        [Header("Physics bubble")]
        [Tooltip("Loose rigidbodies outside the bubble that have come to rest become static until a player comes back.")]
        public bool bubbleOn = true;
        [Tooltip("Metres around the nearest player point (head or hand) where loose objects stay dynamic.")]
        public float bubbleRadius = 20f;
        [Tooltip("Most awake loose rigidbodies inside the bubble at once. Nearest objects win.")]
        public int rigidbodyCap = 40;
        [Tooltip("An object that was frozen wakes only once a player is this far inside the bubble edge (stops flicker).")]
        public float hysteresis = 3f;
        [Tooltip("Seconds between bubble decisions.")]
        public float bubbleInterval = 0.25f;

        [Header("AI tiers")]
        public bool aiTiersOn = true;
        [Tooltip("Closer than this, AI runs every frame.")]
        public float nearRange = 15f;
        [Tooltip("Between near and this, AI runs at mid rate; beyond it, at far rate.")]
        public float midRange = 35f;
        [Tooltip("Seconds between AI updates in the mid range (10 Hz).")]
        public float midPeriod = 0.1f;
        [Tooltip("Seconds between AI updates far away (2 Hz).")]
        public float farPeriod = 0.5f;

        [Header("Shadows")]
        [Tooltip("Shadow distance in metres for the main light. Quest budget: 10 to 15 m.")]
        public float shadowDistance = 12f;
        [Range(1, 4)]
        public int shadowCascades = 1;

        [Header("Quality")]
        [Tooltip("MSAA samples on the active URP asset (1, 2 or 4). Quest 3S: test 2 against 4.")]
        public int msaaSamples = 4;
        [Tooltip("Apply the shadow distance and MSAA on PC too. Off keeps the PC asset's own values at start (the PC gets higher settings from the same content); the wrist menu still changes them.")]
        public bool applyToDesktop;

        [Tooltip("Fixed foveated rendering at the start: 0 off, 1 low, 2 medium, 3 high. Higher saves more graphics time and softens the lens edges.")]
        [Range(0, 3)]
        public int foveation = 3;

        [Header("Terrain")]
        [Tooltip("Terrain heightmap pixel error. Higher = fewer terrain patches drawn.")]
        public float terrainPixelError = 5f;
        [Tooltip("Metres where terrain grass and small details stop drawing.")]
        public float terrainDetailDistance = 35f;

        /// <summary>Shadow distance presets for the wrist menu cycle (0 means shadows off).</summary>
        public static readonly float[] ShadowPresets = { 0f, 10f, 15f, 25f, 50f };
        /// <summary>Rigidbody cap presets for the wrist menu cycle (0 means the bubble is off).</summary>
        public static readonly int[] CapPresets = { 0, 20, 40, 60 };
        /// <summary>MSAA presets for the wrist menu cycle.</summary>
        public static readonly int[] MsaaPresets = { 1, 2, 4 };
    }
}
