using System.IO;
using Makeshift.Combat;
using Makeshift.Core;
using Makeshift.Dev;
using Makeshift.Integration;
using Makeshift.Machines;
using Makeshift.Power;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Autohand;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Makeshift/Build Greybox Drill Scene: the template VR scene plus the hand-crank drill test
    /// (rock, drill frame, sockets, junk parts, battery, plug, switch, club).
    /// Batch: -executeMethod Makeshift.EditorTools.GreyboxSceneBuilder.BuildBatch
    /// Smoke: -executeMethod Makeshift.EditorTools.GreyboxSceneBuilder.RunSmokeBatch -makeshiftSmoke
    /// </summary>
    public static partial class GreyboxSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Greybox.unity";
        const string PlaygroundPath = "Assets/Scenes/Playground.unity";
        const string OrePrefabPath = "Assets/Makeshift/Prefabs/OreChunk.prefab";

        [MenuItem("Makeshift/Build Greybox Drill Scene")]
        public static void Build() => BuildScene(false);

        [MenuItem("Makeshift/Build Playground Scene")]
        public static void BuildPlayground() => BuildScene(true);

        static void BuildScene(bool playground)
        {
            StandSceneBuilder.Build(); // physics, XR, rig, light, floor, template props
            var scene = EditorSceneManager.GetActiveScene();
            SlimPlayer();
            RemoveTemplateProps(scene);

            var orePrefab = MakeOrePrefab();

            // Station: pedestal + frame + rock
            Box("DrillPedestal", new Vector3(1.6f, 0.45f, 1.0f), new Vector3(0.6f, 0.9f, 0.5f), new Color(0.35f, 0.3f, 0.25f));
            Box("RockPedestal", new Vector3(1.6f, 0.4f, 1.7f), new Vector3(0.4f, 0.8f, 0.4f), new Color(0.3f, 0.28f, 0.25f));

            var rock = MakeBoulder(new Vector3(1.6f, 1.1f, 1.7f));
            var node = rock.AddComponent<OreNode>();
            SetRef(node, "orePrefab", orePrefab);

            var frame = new GameObject("DrillFrame");
            frame.transform.position = new Vector3(1.6f, 1.1f, 1.0f);
            var frameVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frameVis.name = "Visual";
            Object.DestroyImmediate(frameVis.GetComponent<Collider>());
            frameVis.transform.SetParent(frame.transform, false);
            frameVis.transform.localScale = new Vector3(0.5f, 0.4f, 0.4f);
            frameVis.GetComponent<Renderer>().sharedMaterial = Mat("DrillFrame", new Color(0.9f, 0.75f, 0.15f));
            var frameCol = frame.AddComponent<BoxCollider>();
            frameCol.size = new Vector3(0.5f, 0.4f, 0.4f);
            var frameRb = frame.AddComponent<Rigidbody>();
            frameRb.isKinematic = true;
            var machine = frame.AddComponent<Machine>();
            var drill = frame.AddComponent<Drill>();

            var bit = new GameObject("BitTip");
            bit.transform.SetParent(frame.transform, false);
            bit.transform.localPosition = new Vector3(0, 0, 0.3f);
            SetRef(drill, "bitTip", bit.transform);
            AddDrillVisuals(frame, drill);
            var drillBody = frame.transform.Find("Model_drill");
            if (drillBody != null)
            {
                // Body collider from the drill model only. Sockets, labels and the bit have their own colliders,
                // so they are left out here (the old whole-frame box stopped parts and the switch from being reached).
                var db = HolderBounds(drillBody);
                frameCol.center = db.center;
                frameCol.size = db.size;
            }

            // With the drill model the sockets sit beside it (bolt-on mounts); with the placeholder box they sit on top.
            bool drillModel = HasModel("drill");
            var motorSocket = Socket(frame.transform, "MotorSocket", drillModel ? new Vector3(-0.38f, -0.08f, 0) : new Vector3(-0.12f, 0.3f, 0), "motor");
            var handleSocket = Socket(frame.transform, "HandleSocket", drillModel ? new Vector3(0.38f, -0.08f, 0) : new Vector3(0.12f, 0.3f, 0), "handle");
            SetList(machine, "sockets", motorSocket, handleSocket);
            var machineSmoke = frame.AddComponent<MachineSmoke>();
            SetRef(machineSmoke, "machine", machine);
            SetRef(machineSmoke, "smoke", ParticleFactory.SmokeColumn(frame.transform, new Vector3(0f, 0.25f, -0.05f), 0f, 0.14f));

            var portGo = new GameObject("PowerPort");
            portGo.transform.SetParent(frame.transform, false);
            portGo.transform.localPosition = new Vector3(0, 0, -0.27f);
            var portCol = portGo.AddComponent<BoxCollider>();
            portCol.isTrigger = true;
            portCol.size = Vector3.one * 0.15f;
            var port = portGo.AddComponent<PowerPort>();
            SetRef(machine, "powerPort", port);
            // The yellow power socket on the back of the drill: the yellow plug goes here.
            var portVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            portVisual.name = "PowerPortVisual";
            Object.DestroyImmediate(portVisual.GetComponent<Collider>());
            portVisual.transform.SetParent(frame.transform, false);
            portVisual.transform.localPosition = new Vector3(0, 0, -0.27f);
            portVisual.transform.localScale = new Vector3(0.14f, 0.14f, 0.05f);
            portVisual.GetComponent<Renderer>().sharedMaterial = Mat("PowerPort", new Color(0.95f, 0.85f, 0.1f));

            // Switch (poke it)
            MakeSwitch(new Vector3(1.1f, 1.0f, 1.0f), machine);

            // Parts table
            Box("PartsTable", new Vector3(1.6f, 0.375f, 0.0f), new Vector3(1.0f, 0.75f, 0.6f), new Color(0.5f, 0.38f, 0.25f));
            var motor = Part(MakeMotor(new Vector3(1.3f, 0.85f, -0.1f)), "motor", 0.7f);
            var toaster = Part(MakeToaster(new Vector3(1.55f, 0.85f, -0.1f)), "toaster", 0.5f);
            var stick = Part(MakeStick(new Vector3(1.8f, 0.85f, -0.1f)), "handle", 0.6f);

            var battery = MakeBattery(new Vector3(1.3f, 0.88f, 0.15f));
            var plug = MakePlug(new Vector3(1.55f, 0.84f, 0.15f), battery.GetComponent<PowerSource>()).GetComponent<PowerPlug>();

            var club = MakeClub(new Vector3(1.8f, 0.95f, 0.15f));
            club.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // Hooks and test
            var bridge = new GameObject("MakeshiftBridge");
            bridge.AddComponent<AutoHandBridge>();

            var test = bridge.AddComponent<GreyboxSmokeTest>();
            test.machine = machine;
            test.motorSocket = motorSocket;
            test.handleSocket = handleSocket;
            test.toaster = toaster;
            test.stick = stick;
            test.plug = plug;
            test.port = port;
            test.rock = node;
            _ = motor;

            // Throwing / bonking: anything swung or thrown counts.
            // (the club, stick and battery props already carry ImpactDamage)
            foreach (var go in new[] { motor.gameObject, toaster.gameObject })
                go.AddComponent<ImpactDamage>();

            // Sprint 2 corner (left of spawn): scavenger, its den, a shiny spoon to steal, a berry bush.
            var berry = MakeBerryPrefab();
            var bushGo = MakeBush(new Vector3(-1.5f, 0f, -0.8f));
            var harvest = bushGo.AddComponent<Harvestable>();
            SetRef(harvest, "dropPrefab", berry);

            var den = new GameObject("ScavengerDen");
            den.transform.position = new Vector3(-3.5f, 0f, 2.5f);

            var creatureGo = Root("Scavenger", new Vector3(-2.2f, 0.02f, 1.6f));
            var mouthPos = BuildScavengerVisual(creatureGo);
            var crb = creatureGo.AddComponent<Rigidbody>();
            crb.isKinematic = true;
            var creatureHealth = creatureGo.AddComponent<Damageable>();
            var mouth = new GameObject("Mouth");
            mouth.transform.SetParent(creatureGo.transform, false);
            mouth.transform.localPosition = mouthPos;
            var creature = creatureGo.AddComponent<Creatures.ScavengerCreature>();
            SetRef(creature, "den", den.transform);
            SetRef(creature, "mouth", mouth.transform);

            var spoonGo = MakeSpoon(new Vector3(-1.4f, 0.05f, 1.5f));
            var spoon = spoonGo.AddComponent<Creatures.Scavengable>();

            var s2 = bridge.AddComponent<Sprint2SmokeTest>();
            s2.creature = creature;
            s2.creatureHealth = creatureHealth;
            s2.spoon = spoon;
            s2.bush = harvest;
            var suites = new System.Collections.Generic.List<SmokeSuite> { s2 };
            if (playground) AddPlayground(bridge, suites);
            test.suites = suites.ToArray();

            string path = playground ? PlaygroundPath : ScenePath;
            TemplateCommon.SaveSceneToBuild(scene, path);
            EditorSceneManager.SaveScene(scene, path);
        }

        public static void BuildBatch()
        {
            Build();
            Debug.Log("[GREYBOX] built " + ScenePath);
        }

        public static void BuildPlaygroundBatch()
        {
            BuildPlayground();
            Debug.Log("[GREYBOX] built " + PlaygroundPath);
        }

        public static void RunSmokeBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        public static void RunPlaygroundSmokeBatch()
        {
            EditorSceneManager.OpenScene(PlaygroundPath);
            EditorApplication.isPlaying = true;
        }

        // ---------- helpers ----------

        /// <summary>
        /// Narrows the player's body and head colliders. The stock sizes (0.2 and 0.15 m) stopped you so far from a
        /// table that things on it were out of reach.
        /// </summary>
        static void SlimPlayer()
        {
            var player = Object.FindAnyObjectByType<Autohand.AutoHandPlayer>(FindObjectsInactive.Include);
            if (player == null) { Debug.LogWarning("[GREYBOX] no AutoHandPlayer found; player colliders unchanged"); return; }
            SetFloat(player.GetComponent<CapsuleCollider>(), "m_Radius", 0.143f);
            SetFloat(player, "headRadius", 0.132f);
        }

        /// <summary>The template's grab-test table and shapes in front of the spawn: not part of this game.</summary>
        static void RemoveTemplateProps(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "Table" || go.name == "Cube" || go.name == "Ball" || go.name == "Handle" || go.name == "Block")
                    Object.DestroyImmediate(go);
        }

        /// <summary>
        /// A physics layer that everything touches except the player's body. Created on first use.
        /// </summary>
        static int FurnitureLayer()
        {
            const string name = "Furniture";
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0)
            {
                var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
                var layers = tags.FindProperty("layers");
                for (int i = 11; i < 31 && layer < 0; i++)
                {
                    var slot = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                    slot.stringValue = name;
                    layer = i;
                }
                tags.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
            if (layer < 0) { Debug.LogWarning("[GREYBOX] no free layer for Furniture"); return 0; }
            int body = LayerMask.NameToLayer("HandPlayer");
            if (body >= 0) Physics.IgnoreLayerCollision(body, layer, true);
            return layer;
        }

        static Material Mat(string name, Color c)
        {
            Directory.CreateDirectory("Assets/Makeshift/Materials");
            string path = $"Assets/Makeshift/Materials/{name}.mat";
            // URP Lit.
            var shader = RenderSetup.StyledShader() ?? Shader.Find("Standard");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                if (existing.shader != shader) existing.shader = shader;
                TintMaterial(existing, c);
                return existing;
            }
            var m = new Material(shader);
            TintMaterial(m, c);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void TintMaterial(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        static GameObject Box(string name, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(name, c);
            return go;
        }

        static GameObject MakeOrePrefab()
        {
            Directory.CreateDirectory("Assets/Makeshift/Prefabs");
            var ore = MakeOreNugget(Vector3.zero);
            var prefab = PrefabUtility.SaveAsPrefabAsset(ore, OrePrefabPath);
            Object.DestroyImmediate(ore);
            return prefab;
        }

        static GameObject MakeBerryPrefab()
        {
            Directory.CreateDirectory("Assets/Makeshift/Prefabs");
            var berry = MakeBerry(Vector3.zero);
            var prefab = PrefabUtility.SaveAsPrefabAsset(berry, "Assets/Makeshift/Prefabs/Berry.prefab");
            Object.DestroyImmediate(berry);
            return prefab;
        }

        static MachinePart Part(GameObject go, string id, float reliability)
        {
            var p = go.AddComponent<MachinePart>();
            SetString(p, "partId", id);
            SetFloat(p, "reliability", reliability);
            return p;
        }

        static PartSocket Socket(Transform parent, string name, Vector3 localPos, string idealId)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one * 0.2f;
            var s = go.AddComponent<PartSocket>();
            var so = new SerializedObject(s);
            var ids = so.FindProperty("idealPartIds");
            ids.arraySize = 1;
            ids.GetArrayElementAtIndex(0).stringValue = idealId;
            so.ApplyModifiedPropertiesWithoutUndo();
            return s;
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetString(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetList(Object target, string field, params Object[] values)
        {
            var so = new SerializedObject(target);
            var list = so.FindProperty(field);
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
