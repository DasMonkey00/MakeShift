using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Visual-only rope or wire between two transforms. A LineRenderer that sags under its own weight and
    /// follows both ends every frame. Used for power cables and lashed vines. No physics, no colliders.
    /// Cheap on purpose: a few segments, no shadows (Quest 3S budget).
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class RopeLine : MonoBehaviour
    {
        [SerializeField] private Transform fromEnd;
        [SerializeField] private Transform toEnd;
        [SerializeField] private Vector3 fromLocal;
        [SerializeField] private Vector3 toLocal;
        [Tooltip("How far the middle drops, as a fraction of the rope length.")]
        [SerializeField] [Range(0f, 0.4f)] private float sagFraction = 0.08f;
        [SerializeField] [Range(2, 16)] private int segments = 8;
        [SerializeField] private float width = 0.012f;
        [SerializeField] private Color color = new Color(0.35f, 0.25f, 0.12f);

        private LineRenderer line;

        /// <summary>Builds a rope under <paramref name="owner"/> from local points on two transforms.</summary>
        public static RopeLine Create(GameObject owner, Transform from, Vector3 fromLocal, Transform to, Vector3 toLocal, Color color, float width = 0.012f)
        {
            var rope = owner.AddComponent<RopeLine>();
            rope.fromEnd = from;
            rope.toEnd = to;
            rope.fromLocal = fromLocal;
            rope.toLocal = toLocal;
            rope.color = color;
            rope.width = width;
            rope.Configure();
            return rope;
        }

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            Configure();
        }

        private void Configure()
        {
            if (line == null) line = GetComponent<LineRenderer>();
            if (line == null) return;
            line.useWorldSpace = true;
            line.positionCount = segments + 1;
            line.startWidth = width;
            line.endWidth = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 2;
            if (line.sharedMaterial == null)
            {
                // URP Unlit keeps the rope cheap and avoids the Looksmith shader that is parked.
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                var mat = new Material(shader);
                mat.color = color;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                line.sharedMaterial = mat;
            }
        }

        private void LateUpdate()
        {
            if (line == null || fromEnd == null || toEnd == null) return;
            Vector3 a = fromEnd.TransformPoint(fromLocal);
            Vector3 b = toEnd.TransformPoint(toLocal);
            float length = Vector3.Distance(a, b);
            float sag = length * sagFraction;
            // A rope never gets longer than its anchors allow, so when the ends are apart the sag stays a dip, not a hump.
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                p.y -= sag * 4f * t * (1f - t);
                line.SetPosition(i, p);
            }
        }
    }
}
