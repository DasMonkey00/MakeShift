using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Makeshift.Rendering
{
    /// <summary>
    /// Marks a world-space description (TextMesh). Press L to show or hide every floating label at once.
    /// Hiding only turns off the renderer, so nothing else about the label changes.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class FloatingLabel : MonoBehaviour
    {
        public static bool Visible { get; private set; } = true;
        private static readonly List<FloatingLabel> All = new List<FloatingLabel>();
        private MeshRenderer meshRenderer;

        private void Awake() { meshRenderer = GetComponent<MeshRenderer>(); }
        private void OnEnable() { All.Add(this); Apply(); }
        private void OnDisable() { All.Remove(this); }
        private void Apply() { if (meshRenderer != null) meshRenderer.enabled = Visible; }

        public static void SetVisible(bool on)
        {
            Visible = on;
            foreach (var l in All) if (l != null) l.Apply();
        }

        public static void Toggle() { SetVisible(!Visible); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            Visible = true;
            var go = new GameObject("FloatingLabelKey") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<KeyHandler>();
        }

        private class KeyHandler : MonoBehaviour
        {
            private void Update()
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current;
                if (k != null && k.lKey.wasPressedThisFrame) Toggle();
#endif
            }
        }
    }
}
