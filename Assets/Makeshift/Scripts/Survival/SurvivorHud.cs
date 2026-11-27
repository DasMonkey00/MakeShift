using UnityEngine;

namespace Makeshift.Survival
{
    /// <summary>
    /// Debug read-out for the playground: hunger, warmth and time of day as small text floating
    /// at the lower edge of the player's view. Replace with a proper wrist UI later.
    /// </summary>
    public class SurvivorHud : MonoBehaviour
    {
        [SerializeField] private Survivor survivor;
        [SerializeField] private DayNightCycle cycle;

        private TextMesh text;

        private void Start()
        {
            var cam = GetComponentInChildren<Camera>(true);
            if (cam == null) return;

            var go = new GameObject("HudText");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.22f, 0.7f);
            text = go.AddComponent<TextMesh>();
            go.AddComponent<MeshFilter>(); // some scene scans expect every MeshRenderer to have one
            text.characterSize = 0.012f;
            text.fontSize = 48;
            text.anchor = TextAnchor.UpperCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        private void Update()
        {
            if (text == null || survivor == null) return;
            string time = cycle != null ? (cycle.IsNight ? "night" : "day") : "";
            text.text = $"food {survivor.Hunger:0}   warmth {survivor.Warmth:0}   {time}";
        }
    }
}
