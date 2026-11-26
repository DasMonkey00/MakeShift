using System;
using PaleFernStudio.Looksmith;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Makeshift.Rendering
{
    public enum RenderStyle { None = 0, StylizedRealism = 1, CelShading = 2, Foggy = 3 }

    /// <summary>
    /// Picks the world's render style (Looksmith Look) at runtime.
    ///
    /// Cost rule: the banded lighting lives in the material shaders and costs about the same as normal
    /// URP lighting. The expensive parts are full-screen: edge lines (extra depth + normals pass),
    /// grain/vignette finishing, and the post-processing stack (bloom, fringe, tonemap). So there are two modes:
    ///   lean  (default on Quest): lighting and haze only. No lines, no finish, no post stack.
    ///   full  (default on PC):    the Look exactly as authored.
    /// None turns Looksmith off entirely (no extra passes at all).
    ///
    /// Keys 1-4 pick a style and 5 flips lean/full, in the editor or on a PC build.
    /// </summary>
    public class RenderStyleSwitcher : MonoBehaviour
    {
        [SerializeField] private Volume volume;
        [SerializeField] private VolumeProfile stylizedRealism;
        [SerializeField] private VolumeProfile celShading;
        [SerializeField] private VolumeProfile foggy;
        [SerializeField] private RenderStyle startStyle = RenderStyle.None;
        [Tooltip("Show on-screen text and listen for number keys.")]
        [SerializeField] private bool hotkeys = true;
        [SerializeField] private TextMesh statusText;

        public RenderStyle Current { get; private set; }
        /// <summary>True = the Look as authored (lines, finish, post). False = lean (Quest-safe).</summary>
        public bool FullEffects { get; private set; }

        public event Action<RenderStyle, bool> Changed;

        private LooksmithLookVolume lookVolume;
        private VolumeProfile runtimeProfile;

        private void Awake()
        {
            FullEffects = Application.platform != RuntimePlatform.Android;
            if (volume != null) lookVolume = volume.GetComponent<LooksmithLookVolume>();
        }

        private void Start() { Apply(startStyle); }

        private void OnDestroy()
        {
            if (runtimeProfile != null) Destroy(runtimeProfile);
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (!hotkeys || Keyboard.current == null) return;
            if (Keyboard.current.digit1Key.wasPressedThisFrame) Select(RenderStyle.None);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) Select(RenderStyle.StylizedRealism);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) Select(RenderStyle.CelShading);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) Select(RenderStyle.Foggy);
            if (Keyboard.current.digit5Key.wasPressedThisFrame) ToggleFullEffects();
#endif
        }

#if UNITY_EDITOR
        /// <summary>Edit mode only: gives the scene's Look Volume a profile so Looksmith's tools see a look.</summary>
        public bool EditorAssignProfile()
        {
            if (volume == null || volume.sharedProfile != null || stylizedRealism == null) return false;
            volume.sharedProfile = stylizedRealism;
            return true;
        }
#endif

        public void Select(RenderStyle style) { Apply(style); }
        public void SelectIndex(int style) { Apply((RenderStyle)Mathf.Clamp(style, 0, 3)); }

        public void ToggleFullEffects()
        {
            FullEffects = !FullEffects;
            Apply(Current);
        }

        public void SetFullEffects(bool full)
        {
            FullEffects = full;
            Apply(Current);
        }

        public static string NameOf(RenderStyle s)
        {
            switch (s)
            {
                case RenderStyle.StylizedRealism: return "Stylized realism";
                case RenderStyle.CelShading: return "Cel shading";
                case RenderStyle.Foggy: return "Foggy";
                default: return "None";
            }
        }

        private VolumeProfile ProfileFor(RenderStyle s)
        {
            switch (s)
            {
                case RenderStyle.StylizedRealism: return stylizedRealism;
                case RenderStyle.CelShading: return celShading;
                case RenderStyle.Foggy: return foggy;
                default: return null;
            }
        }

        private void Apply(RenderStyle style)
        {
            Current = style;
            var source = ProfileFor(style);
            bool on = source != null && volume != null;

            if (volume != null) volume.enabled = on;
            if (lookVolume != null) lookVolume.enabled = on;

            if (on)
            {
                if (runtimeProfile != null) { Destroy(runtimeProfile); runtimeProfile = null; }
                if (FullEffects)
                {
                    volume.sharedProfile = source;
                }
                else
                {
                    runtimeProfile = BuildLeanCopy(source); // our own copy: the authored asset is never touched
                    volume.sharedProfile = runtimeProfile;
                }
            }

            // The Look's own bloom/tonemap/grain only run if the camera post stack is on.
            foreach (var cam in Camera.allCameras)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = on && FullEffects;
            }

            if (statusText != null)
                statusText.text = $"Style: {NameOf(style)}\nEffects: {(FullEffects ? "FULL (lines, finish, post)" : "LEAN (Quest-safe)")}";
            Changed?.Invoke(style, FullEffects);
        }

        /// <summary>Copy of a Look with every full-screen cost removed: keeps lighting, colour and haze.</summary>
        private static VolumeProfile BuildLeanCopy(VolumeProfile source)
        {
            var copy = ScriptableObject.CreateInstance<VolumeProfile>();
            copy.name = source.name + " (lean)";
            if (!source.TryGet(out LooksmithLook src)) return copy;

            var dst = copy.Add<LooksmithLook>(false);
            dst.active = src.active;
            for (int i = 0; i < src.parameters.Count; i++)
            {
                dst.parameters[i].SetValue(src.parameters[i]);
                dst.parameters[i].overrideState = src.parameters[i].overrideState;
            }

            Zero(dst.lineWeight);
            Zero(dst.outlineWidth);
            Zero(dst.grain);
            Zero(dst.vignette);
            Zero(dst.paper);
            Zero(dst.chromaticFringe);
            dst.gradingLut.overrideState = true;
            dst.gradingLut.value = null;
            return copy;
        }

        private static void Zero(ClampedFloatParameter p) { p.overrideState = true; p.value = 0f; }
    }
}
