using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// A few little bits that fly off and vanish: bites of food, a squashed berry. Plain cubes with their own
    /// simple fall (no physics), so they cost next to nothing and never bump into anything.
    /// </summary>
    public class Crumbs : MonoBehaviour
    {
        private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        private Vector3 velocity;
        private float life = 0.8f;

        public static void Burst(Vector3 position, Color color, int count = 8, float speed = 1f)
        {
            if (!materials.TryGetValue(color, out var mat) || mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                mat = new Material(shader) { color = color };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                materials[color] = mat;
            }
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Crumb";
                var col = go.GetComponent<Collider>();
                col.enabled = false;
                Destroy(col);
                go.transform.SetPositionAndRotation(position, Random.rotation);
                go.transform.localScale = Vector3.one * Random.Range(0.008f, 0.018f);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.AddComponent<Crumbs>().velocity = (Random.insideUnitSphere + Vector3.up * 0.7f) * speed;
            }
        }

        private void Update()
        {
            velocity += Physics.gravity * (0.5f * Time.deltaTime);
            transform.position += velocity * Time.deltaTime;
            life -= Time.deltaTime;
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
