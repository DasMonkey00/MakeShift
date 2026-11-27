using System;
using System.Collections.Generic;
using System.IO;
using Autohand;
using Makeshift.Audio;
using Makeshift.Fire;
using Makeshift.Settling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// The small camp in the Toon Nature look test scene: the game's fire pit, logs, flint and stone and torches,
    /// with flames from the Wallcoeur "VFX URP - Fire Package".
    /// </summary>
    public static partial class GreyboxSceneBuilder
    {
        const string FirePack = "Assets/VFXPACK_FIRE_WALLCOEUR/";
        const string FireMatDir = "Assets/Makeshift/ToonNature/Fire";

        /// <summary>Builds the camp in front of the player. <paramref name="ground"/> gives the height at a point.</summary>
        public static Campfire AddToonCamp(Vector3 start, float yaw, Func<Vector3, float> ground, out Flammable[] litTorches)
        {
            if (!AssetDatabase.IsValidFolder(FirePack.TrimEnd('/')))
                throw new FileNotFoundException("import 'VFX URP - Fire Package' first: " + FirePack);

            var turn = Quaternion.Euler(0f, yaw, 0f);
            // x to the player's right, z ahead, height above the ground at that spot.
            Vector3 At(float x, float z, float height = 0f)
            {
                var p = start + turn * new Vector3(x, 0f, z);
                p.y = ground(p) + height;
                return p;
            }

            var campfire = MakeCampfire(At(0f, ToonCampDistance));
            campfire.transform.rotation = turn;

            var pile = campfire.transform.Find("FuelPile");
            for (int i = pile.childCount - 1; i >= 0; i--) Object.DestroyImmediate(pile.GetChild(i).gameObject);
            for (int i = 0; i < 3; i++)
            {
                var holder = new GameObject("PileLog" + i).transform;
                holder.SetParent(pile, false);
                holder.localPosition = new Vector3(0f, 0.07f + 0.02f * i, 0f);
                holder.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
                Model(holder, "log", 0.4f, 0, Pivot.Center, default, default, true);
            }

            for (int i = 0; i < 5; i++)
            {
                // The ground falls away ahead: logs lie pointing down the slope so they cannot roll off.
                var log = MakeLog(At(0.75f + 0.19f * i, 1.25f + (i % 2) * 0.08f, 0.1f));
                log.transform.rotation = turn * Quaternion.Euler(0f, 90f + i * 5f - 10f, 0f);
                log.AddComponent<RestUntilTouched>();
            }

            // Flint and stone on the camp table, with a torch ready to be lit.
            var table = At(-0.95f, 1.15f);
            var tableGo = MakeTable("CampTable", table, new Vector2(0.9f, 0.65f), "camptable");
            tableGo.transform.rotation = turn * Quaternion.Euler(0f, -55f, 0f);
            var right = tableGo.transform.right;
            MakeFlintAndStone(table + right * 0.18f + Vector3.up * 0.8f, table - right * 0.05f + Vector3.up * 0.84f);
            var torch = MakeTorch(table - right * 0.26f + Vector3.up * 0.82f);
            torch.transform.rotation = tableGo.transform.rotation * Quaternion.Euler(90f, 0f, 0f);

            // Two torches on stakes that burn from the start.
            var standing = new List<Flammable>();
            foreach (var spot in new[] { new Vector2(-1.25f, 2.6f), new Vector2(1.5f, 2.7f) })
            {
                var t = MakeTorch(At(spot.x, spot.y, 1.15f));
                t.name = "StandingTorch";
                t.GetComponent<Rigidbody>().isKinematic = true;
                t.GetComponent<Grabbable>().isGrabbable = false;
                var stake = Vis(null, "TorchStake", PrimitiveType.Cylinder, At(spot.x, spot.y, 0.45f),
                    new Vector3(0.045f, 0.47f, 0.045f), Bark);
                stake.AddComponent<CapsuleCollider>();
                var flame = t.GetComponent<Flammable>();
                SetFloat(flame, "burnDuration", 100000f);
                standing.Add(flame);
            }
            litTorches = standing.ToArray();

            // Loose props come to rest on the slope instead of creeping into the fire pit.
            foreach (var body in Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude))
                if (!body.isKinematic && body.GetComponent<Grabbable>() != null && body.GetComponentInParent<AutoHandPlayer>() == null
                    && body.GetComponent<PropSettle>() == null)
                    body.gameObject.AddComponent<PropSettle>();

            UseFirePack(campfire);
            return campfire;
        }

        public const float ToonCampDistance = 1.9f;

        /// <summary>Swaps every flame in the open scene for the fire pack's effect and gives it its sound and glow.</summary>
        static void UseFirePack(Campfire campfire)
        {
            foreach (var upright in Object.FindObjectsByType<FlameUpright>(FindObjectsInactive.Include))
            {
                var root = upright.transform;
                bool isCamp = root.parent == campfire.transform;
                bool isTorch = root.parent.name.EndsWith("Torch");

                for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);

                float size = isCamp ? 0.55f : isTorch ? 0.32f : 0.3f;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    FirePack + "Prefab/" + (isTorch ? "VFX_TorchLight" : "VFX_Fire") + ".prefab");
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                fx.transform.localPosition = Vector3.zero;
                fx.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cancels the flame root's -90
                fx.transform.localScale = Vector3.one;
                fx.SetActive(true);

                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    // The pack sizes each system on its own and lets gravity lift the flames, so both need scaling.
                    ps.transform.localScale = Vector3.one * (ps.transform == fx.transform ? size : 1f);
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    var gravity = main.gravityModifier;
                    float lift = isCamp ? size * 0.55f : size * 0.6f;
                    main.gravityModifier = new ParticleSystem.MinMaxCurve(gravity.constantMin * lift, gravity.constantMax * lift);
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    renderer.sharedMaterial = UrpParticle(renderer.sharedMaterial);
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(ps);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(ps.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }

                var source = root.gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                var crackle = root.gameObject.AddComponent<FireCrackle>();
                SetFloat(crackle, "volume", isCamp ? 0.6f : 0.25f);

                if (isCamp) continue; // the campfire has its own light, which it flickers itself
                var lightGo = new GameObject("TorchGlow");
                lightGo.transform.SetParent(root, false);
                lightGo.transform.localPosition = new Vector3(0f, 0f, 0.12f); // the root's +Z is up
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = ParticleFactory.FireLightColor;
                light.range = 3.5f;
                light.intensity = 1.4f;
                light.shadows = LightShadows.None;
            }

            var fireLight = campfire.transform.Find("FireLight").GetComponent<Light>();
            fireLight.range = 6f;
        }

        /// <summary>
        /// A URP copy of an old style particle material. The old shaders draw in one eye only with the project's
        /// single pass instanced VR rendering.
        /// </summary>
        public static Material UrpParticle(Material source)
        {
            if (source == null || source.shader.name.StartsWith("Universal Render Pipeline/")) return source;
            Directory.CreateDirectory(FireMatDir);
            string path = $"{FireMatDir}/{source.name} URP.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            bool additive = source.shader.name.Contains("Add");
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", source.mainTexture);
            // A little over white on the flames, so bloom picks them up when post effects are on.
            float boost = additive ? 1.5f : 1f;
            m.SetColor("_BaseColor", new Color(boost, boost, boost, 1f));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
