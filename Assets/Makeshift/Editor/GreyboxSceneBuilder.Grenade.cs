using Makeshift.Combat;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Makeshift.EditorTools
{
    public static partial class GreyboxSceneBuilder
    {
        const string DemoGrenade = "Assets/AutoHand/Examples/Prefabs/Demo/Gernade.prefab";
        const string GrenadeSound = "Assets/AutoHand/Examples/Sounds/Explosion.mp3";

        /// <summary>Puts the grenade rack on a table: a copy of the Auto Hand grenade with our own fuse and blast.</summary>
        public static GameObject AddGrenadeRack(Transform table)
        {
            var old = GameObject.Find("GrenadeRack");
            if (old != null) Object.DestroyImmediate(old);
            var rack = new GameObject("GrenadeRack");
            var top = new Bounds(table.position, Vector3.zero);
            foreach (var r in table.GetComponentsInChildren<Renderer>()) top.Encapsulate(r.bounds);
            // Behind the flint and the torch, a little above the boards so it settles onto them.
            Vector3 at = table.position + table.forward * 0.17f;
            at.y = table.position.y + 0.9f;
            rack.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, table.eulerAngles.y, 0f));
            SetRef(rack.AddComponent<GrenadeRack>(), "prefab", GrenadePrefab());
            return rack;
        }

        static GameObject GrenadePrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoGrenade);
            var go = Object.Instantiate(source);
            go.name = "Grenade";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var demo = go.GetComponentInChildren<Autohand.Demo.Grenade>();
            var pinObject = demo.gameObject;
            var body = demo.grenade;
            var pin = demo.pin;
            var joint = demo.pinJoint;
            float pull = demo.pinJointStrength;
            Object.DestroyImmediate(demo);

            var grenade = pinObject.AddComponent<FellingGrenade>();
            SetRef(grenade, "grenade", body);
            SetRef(grenade, "pin", pin);
            SetRef(grenade, "pinJoint", joint);
            SetFloat(grenade, "pullStrength", pull);
            SetRef(grenade, "boom", AssetDatabase.LoadAssetAtPath<AudioClip>(GrenadeSound));
            SetRef(grenade, "blastEffect", GrenadeBlastPrefab());

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir() + "Grenade.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        const string FireDir = "Assets/Makeshift/ToonNature/Fire/";

        // The camp's fire and smoke, as one burst the size of the blast: a fireball, a ring of flame racing out to the
        // edge of the 15 m radius, a ring of dust behind it and a column of dark smoke. All gone in a few seconds.
        static GameObject GrenadeBlastPrefab()
        {
            var flame = AssetDatabase.LoadAssetAtPath<Material>(FireDir + "A_FlameAdd 1 URP.mat");
            var glow = AssetDatabase.LoadAssetAtPath<Material>(FireDir + "A_SmokeAdd URP.mat");
            var smoke = AssetDatabase.LoadAssetAtPath<Material>(FireDir + "A_SmokeAlpha_1 URP.mat");

            var go = new GameObject("GrenadeBlast");
            // Name, material, count, life, speed, size, colour, rise, flat ring.
            Burst(go, flame, 36, new Vector2(0.5f, 1.1f), new Vector2(4f, 16f), new Vector2(8f, 13f),
                new Color(1f, 0.8f, 0.45f, 1f), -0.2f, false);
            Burst(Child(go, "FlameRing"), flame, 56, new Vector2(0.75f, 0.95f), new Vector2(19f, 23f),
                new Vector2(5f, 8f), new Color(1f, 0.6f, 0.25f, 1f), 0f, true);
            Burst(Child(go, "GlowRing"), glow, 30, new Vector2(1.2f, 1.8f), new Vector2(9f, 13f), new Vector2(7f, 10f),
                new Color(0.8f, 0.45f, 0.2f, 0.7f), -0.05f, true);
            Burst(Child(go, "Smoke"), smoke, 34, new Vector2(2.5f, 4.5f), new Vector2(1f, 6f), new Vector2(7f, 12f),
                new Color(0.22f, 0.2f, 0.19f, 0.85f), -0.25f, false);
            var root = go.GetComponent<ParticleSystem>().main;
            root.stopAction = ParticleSystemStopAction.Destroy;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir() + "GrenadeBlast.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        static void Burst(GameObject go, Material material, int count, Vector2 life, Vector2 speed, Vector2 size,
            Color colour, float gravity, bool ring)
        {
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.duration = 5f;
            main.loop = false;
            main.maxParticles = count;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = colour;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = system.shape;
            shape.shapeType = ring ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = ring ? 1f : 1.2f;
            shape.radiusThickness = ring ? 0f : 1f;
            if (ring)
            {
                shape.rotation = new Vector3(90f, 0f, 0f); // lying flat, so it spreads along the ground
                shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            }
            else
            {
                var drag = system.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.limit = 1f;
                drag.dampen = 0.08f;
            }
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 3;
            sheet.numTilesY = 3;
            var grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 1.4f));
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.maxParticleSize = 2f; // big puffs must not be shrunk when close
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
