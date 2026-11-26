using System.Collections.Generic;
using System.IO;
using Autohand;
using Makeshift.Combat;
using Makeshift.Core;
using Makeshift.Creatures;
using Makeshift.Crafting;
using Makeshift.Dev;
using Makeshift.Fire;
using Makeshift.Integration;
using Makeshift.Movement;
using Makeshift.Survival;
using Makeshift.UI;
using UnityEditor;
using UnityEngine;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// The Playground: the greybox drill station plus a zone for every Phase 1 building block.
    ///   west  (-8, 0, 0)   survival camp: campfire pit, logs, torch, raw meat, flint and stone, water trough
    ///   east  ( 8, 0, 0)   crafting bench: flint, stone, sticks, vines (wrap things together)
    ///   north ( 0, 0, 10)  combat range: training dummies
    ///   south ( 0, 0,-10)  traversal: climbing wall up to a platform with a glider umbrella
    /// Every test area has a note you can pick up and read on its table.
    /// </summary>
    public static partial class GreyboxSceneBuilder
    {
        static void AddPlayground(GameObject host, List<SmokeSuite> suites)
        {
            var floor = GameObject.Find("Floor");
            if (floor != null) floor.transform.localScale = new Vector3(60f, 1f, 60f);
            // Same grip as props, so things that land on the floor stop instead of rolling forever.
            if (floor != null && floor.GetComponent<Collider>() is Collider floorCol) floorCol.sharedMaterial = PropPhysics();
            var player = GameObject.Find("XRPlayer");
            var sunGo = GameObject.Find("Sun");

            // ---------- World clock and raids ----------
            var world = new GameObject("World");
            var cycle = world.AddComponent<DayNightCycle>();
            if (sunGo != null) SetRef(cycle, "sun", sunGo.GetComponent<Light>());
            var raids = world.AddComponent<NightRaids>();
            var campCenter = new GameObject("CampCenter");
            campCenter.transform.position = new Vector3(-8f, 0f, 0f);
            var meat = MakeMeatPrefab();
            var raider = MakeRaiderPrefab(meat);
            SetRef(raids, "cycle", cycle);
            SetRef(raids, "raiderPrefab", raider.GetComponent<ScavengerCreature>());
            SetRef(raids, "center", campCenter.transform);

            // ---------- Player survival ----------
            Survivor survivor = null;
            if (player != null)
            {
                var hp = player.AddComponent<Damageable>();
                SetFloat(hp, "maxHealth", 100f);
                SetFloat(hp, "knockbackPerDamage", 0f);
                survivor = player.AddComponent<Survivor>();
                var hud = player.AddComponent<SurvivorHud>();
                SetRef(hud, "survivor", survivor);
                SetRef(hud, "cycle", cycle);

                var cam = player.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    var mouth = new GameObject("Mouth");
                    mouth.transform.SetParent(cam.transform, false);
                    mouth.transform.localPosition = new Vector3(0f, -0.1f, 0.08f);
                    var sc = mouth.AddComponent<SphereCollider>();
                    sc.radius = 0.1f;
                    sc.isTrigger = true;
                    mouth.AddComponent<Mouth>();
                }
            }

            // ---------- Spawn: welcome and drill notes ----------
            // (to the left of the template's grab-test table, which already sits 0.9 m in front of the spawn)
            MakeTable("InfoStand", new Vector3(-1.3f, 0f, 0.8f), new Vector2(0.9f, 0.55f), "camptable");
            MakePaper("Note_Welcome", new Vector3(-1.55f, 0.77f, 0.8f), 0f,
                "WELCOME TO THE PLAYGROUND",
                "",
                "Wrist menu: look at the back of",
                "your LEFT wrist, then touch the",
                "round button with your RIGHT",
                "index finger.",
                "Jump: A button (right hand).",
                "Zones: WEST camp, EAST craft,",
                "NORTH combat, SOUTH climb.",
                "Notes like this sit on tables.");
            MakePaper("Note_Drill", new Vector3(-1.05f, 0.77f, 0.8f), 0f,
                "HAND DRILL (it works, badly)",
                "",
                "1 Put the ORANGE DRILL on the",
                "  MOTOR pad (blue).",
                "2 Put the STICK on the HANDLE",
                "  pad (orange).",
                "3 Pick up the YELLOW PLUG (the",
                "  cable end by the battery) and",
                "  touch it to the YELLOW SOCKET",
                "  on the back of the drill.",
                "4 Poke the RED SWITCH on the post",
                "  to start it.",
                "5 The bit points at the rock.",
                "Jams or smokes? Bonk it.");

            // ---------- West: survival camp ----------
            Label("SURVIVAL CAMP", new Vector3(-8f, 2.4f, 0f), 270f);

            var campfire = MakeCampfire(new Vector3(-8f, 0f, 0f));
            MakeBucket(new Vector3(-8f, 0.5f, -2.2f));

            for (int i = 0; i < 4; i++)
            {
                var log = MakeLog(new Vector3(-6.8f, 0.08f, -0.6f + i * 0.35f));
                log.transform.rotation = Quaternion.Euler(0f, 90f + i * 7f, 0f);
            }
            var torch = MakeTorch(new Vector3(-9.2f, 0.05f, 1.4f));
            torch.transform.rotation = Quaternion.Euler(0f, 20f, 90f);

            MakeTable("CampTable", new Vector3(-9.45f, 0f, -1.5f), new Vector2(0.9f, 0.65f), "camptable");
            MakeTable("CampTable2", new Vector3(-8.55f, 0f, -1.5f), new Vector2(0.9f, 0.65f), "camptable");
            MakePaper("Note_Survival", new Vector3(-9.45f, 0.77f, -1.5f), 270f,
                "SURVIVAL CAMP",
                "",
                "1 Put a log in the stone ring.",
                "2 Hit FLINT against STONE",
                "  over the logs for sparks.",
                "3 Hold meat by the fire:",
                "  raw, cooked, then burnt.",
                "4 Hold food to your mouth.",
                "5 Water puts the fire out.",
                "Night: raiders steal meat.",
                "Bonk them, then butcher them.");
            MakeFlintAndStone(new Vector3(-8.8f, 0.78f, -1.3f), new Vector3(-8.8f, 0.8f, -1.7f));
            for (int i = 0; i < 2; i++)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(meat);
                m.transform.position = new Vector3(-8.45f, 0.8f, -1.62f + i * 0.24f);
            }

            MakeTrough(new Vector3(-10.5f, 0f, 0.5f));

            // ---------- East: crafting bench ----------
            Label("CRAFTING", new Vector3(8f, 2.4f, 0f), 90f);
            MakeTable("CraftTable", new Vector3(8f, 0f, 0f), new Vector2(0.7f, 2.0f), "crafttable", 0.8f);
            MakeGlueTub(new Vector3(7.85f, 1.0f, -0.45f)); // dropped onto the table top, on the side you stand
            MakePaper("Note_Crafting", new Vector3(8.05f, 0.82f, -0.8f), 90f,
                "CRAFTING (no recipes)",
                "",
                "Dip a STICK in the glue, then",
                "touch a STONE to it. It stays.",
                "One coat per stick.",
                "",
                "VINE: hold it against two",
                "glued things that touch and",
                "wiggle it to lash them.",
                "",
                "Hit the dummy to test them.");
            MakeFlintAndStone(new Vector3(7.85f, 0.83f, -0.22f), new Vector3(7.85f, 0.86f, -0.05f));
            for (int i = 0; i < 2; i++) MakeStick(new Vector3(7.95f, 0.84f, 0.18f + i * 0.2f));
            for (int i = 0; i < 3; i++) MakeVine(new Vector3(7.95f, 0.84f, 0.58f + i * 0.15f));
            MakeDummy(new Vector3(8f, 0.65f, 4f));

            // ---------- North: combat range ----------
            Label("COMBAT RANGE", new Vector3(0f, 2.4f, 10f), 0f);
            for (int i = -1; i <= 1; i++) MakeDummy(new Vector3(i * 2f, 0.65f, 10f));
            MakeTable("CombatTable", new Vector3(-3.5f, 0f, 8f), new Vector2(0.9f, 0.6f), "camptable");
            MakePaper("Note_Combat", new Vector3(-3.72f, 0.77f, 8f), 0f,
                "COMBAT RANGE",
                "",
                "Swing, throw or drop anything",
                "on a dummy. Damage comes from",
                "how hard and how heavy.",
                "Sharp and heavy things hurt",
                "more. A club and a stone are",
                "on this table.");
            MakeClub(new Vector3(-3.4f, 0.85f, 8f)).transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            MakeStone(new Vector3(-3.2f, 0.85f, 8f));

            // ---------- South: traversal ----------
            Label("CLIMB UP, GLIDE DOWN", new Vector3(0f, 5.5f, -9f), 180f);
            var wallBox = Box("ClimbWall", new Vector3(0f, 2f, -10f), new Vector3(4f, 4f, 0.3f), new Color(0.62f, 0.56f, 0.5f));
            var wallVisual = new GameObject("ClimbWallModel");
            wallVisual.transform.position = new Vector3(0f, 2f, -10f);
            bool wallModel = false;
            foreach (var c in new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) })
                wallModel |= ModelFit(wallVisual.transform, "climbwall", new Vector3(2f, 2f, 0.3f), default, c) != null;
            if (wallModel) wallBox.GetComponent<Renderer>().enabled = false; // the collider stays
            Box("ClimbPlatform", new Vector3(0f, 3.85f, -12.5f), new Vector3(4f, 0.3f, 5f), new Color(0.4f, 0.36f, 0.3f));
            // Ledge over the wall top: hold a hand here to pull up onto the platform.
            var ledge = new GameObject("LedgeZone");
            // Sits above the top row of holds and behind their faces: overlapping them got in the way of grabbing.
            ledge.transform.position = new Vector3(0f, 4.15f, -10.18f);
            ledge.layer = LayerMask.NameToLayer("Ignore Raycast");
            var ledgeCol = ledge.AddComponent<BoxCollider>();
            ledgeCol.isTrigger = true;
            ledgeCol.size = new Vector3(4f, 0.85f, 0.66f);
            ledge.AddComponent<Makeshift.Movement.LedgeGrab>();
            var holds = new List<Climbable>();
            for (int row = 0; row < 8; row++)
            {
                for (int col = -1; col <= 1; col++)
                {
                    float x = col * 1.1f + (row % 2 == 0 ? 0.25f : -0.25f);
                    var hold = MakeHold($"Hold_{row}_{col}", new Vector3(x, 0.5f + row * 0.45f, -9.8f), ((row * 3 + col) % 5) * 6f - 12f);
                    holds.Add(hold.AddComponent<Climbable>());
                }
            }
            var umbrella = MakeUmbrella(new Vector3(0f, 4.4f, -12f));
            var glider = umbrella.GetComponent<Glider>();

            MakeTable("ClimbTable", new Vector3(3f, 0f, -8f), new Vector2(0.8f, 0.5f), "camptable");
            MakePaper("Note_Climb", new Vector3(3f, 0.77f, -8f), 180f,
                "CLIMBING + GLIDING",
                "",
                "Grab a hold (grip). Holds are",
                "fixed to the wall. Pull your",
                "hand down to climb, and reach",
                "up with the other hand. Reach a",
                "hand over the top edge and hold",
                "it there: you pull yourself up.",
                "On the platform: pick up the",
                "umbrella, squeeze the trigger",
                "to open it, then jump off.");

            // ---------- Test suites ----------
            var survival = host.AddComponent<SurvivalSmokeTest>();
            survival.campfire = campfire;
            survival.cycle = cycle;
            survival.raids = raids;
            suites.Add(survival);

            var crafting = host.AddComponent<CraftingSmokeTest>();
            crafting.benchSpot = new Vector3(8f, 0f, -6f);
            crafting.climbHolds = holds.ToArray();
            crafting.umbrella = glider;
            suites.Add(crafting);

            suites.Add(host.AddComponent<AuthoritySmokeTest>());

            RestyleSceneMaterials();
            AddEnvironment(world, cycle, sunGo, floor);

            if (player != null) AddWristMenu(player, world, cycle, raids, survivor, campfire, sunGo);
        }

        /// <summary>A flint and a stone to strike together, with sparks.</summary>
        static void MakeFlintAndStone(Vector3 flintPos, Vector3 stonePos)
        {
            var flint = MakeFlint(flintPos, null);
            var sparks = ParticleFactory.Burst(flint.transform, Vector3.zero, true);
            SetRef(flint.GetComponent<SparkStriker>(), "sparks", sparks);
            MakeStone(stonePos);
        }

        /// <summary>Jump button and the left-wrist test menu.</summary>
        static void AddWristMenu(GameObject player, GameObject world, DayNightCycle cycle, NightRaids raids,
            Survivor survivor, Campfire campfire, GameObject sunGo)
        {
            var ahp = player.GetComponentInChildren<AutoHandPlayer>(true);
            if (ahp == null)
            {
                Debug.LogWarning("[PLAYGROUND] no AutoHandPlayer found; skipping jump button and wrist menu");
                return;
            }

            var jump = player.AddComponent<PlayerJump>();
            SetRef(jump, "player", ahp);
            player.AddComponent<Makeshift.Rendering.HandShadowsOff>();

            var menu = player.AddComponent<WristMenu>();
            SetRef(menu, "player", ahp);
            SetRef(menu, "leftHand", ahp.handLeft);
            SetRef(menu, "rightHand", ahp.handRight);
            SetRef(menu, "head", player.GetComponentInChildren<Camera>(true));
            SetRef(menu, "cycle", cycle);
            SetRef(menu, "timeOfDay", world.GetComponent<Makeshift.Rendering.TimeOfDaySky>());
            SetRef(menu, "survivor", survivor);
            SetRef(menu, "raids", raids);
            SetRef(menu, "campfire", campfire);
            if (sunGo != null) SetRef(menu, "sun", sunGo.GetComponent<Light>());

            Directory.CreateDirectory("Assets/Makeshift/Materials");
            const string uiPath = "Assets/Makeshift/Materials/WristUI.mat";
            var ui = AssetDatabase.LoadAssetAtPath<Material>(uiPath);
            if (ui == null)
            {
                ui = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(ui, uiPath);
            }
            TintMaterial(ui, Color.white);
            SetRef(menu, "surfaceMaterial", ui);

            var spawn = new GameObject("TP_Spawn");
            spawn.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            var camp = TeleportPoint("TP_Camp", new Vector3(-5.5f, 0f, 0f), 270f);
            var craft = TeleportPoint("TP_Craft", new Vector3(5.5f, 0f, 0f), 90f);
            var combat = TeleportPoint("TP_Combat", new Vector3(0f, 0f, 6.5f), 0f);
            var climb = TeleportPoint("TP_Climb", new Vector3(0f, 0f, -7f), 180f);
            SetList(menu, "teleportPoints", spawn.transform, camp, craft, combat, climb);
        }

        static Transform TeleportPoint(string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go.transform;
        }

        const string LooksmithDir = "Assets/PaleFernStudio/Looksmith/";

        /// <summary>Ground texture, flat grass clumps, and a daytime sky with clouds driven by the world clock.</summary>
        static void AddEnvironment(GameObject world, DayNightCycle cycle, GameObject sunGo, GameObject floor)
        {
            // ---------- Ground ----------
            var groundTex = AssetDatabase.LoadAssetAtPath<Texture2D>(LooksmithDir + "Samples/Demo/Terrain/Terrain_Grass.png");
            if (floor != null && groundTex != null)
            {
                var fm = floor.GetComponent<Renderer>().sharedMaterial;
                if (fm != null && fm.HasProperty("_BaseMap"))
                {
                    fm.SetTexture("_BaseMap", groundTex);
                    fm.SetTextureScale("_BaseMap", new Vector2(30f, 30f)); // the 60 m floor: one tile every 2 m
                    TintMaterial(fm, new Color(0.9f, 0.9f, 0.9f));
                    if (fm.HasProperty("_Smoothness")) fm.SetFloat("_Smoothness", 0f);
                }
            }

            // ---------- Grass ----------
            var grassPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LooksmithDir + "Samples/Demo/Prefabs/Grass1.prefab");
            if (grassPrefab != null)
            {
                var mf = grassPrefab.GetComponentInChildren<MeshFilter>();
                var mr = grassPrefab.GetComponentInChildren<MeshRenderer>();
                string meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (AssetImporter.GetAtPath(meshPath) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true; // needed to merge clumps into chunk meshes at start
                    mi.SaveAndReimport();
                }
                var field = new GameObject("GrassField").AddComponent<Makeshift.Rendering.GrassField>();
                SetRef(field, "clump", AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) ?? mf.sharedMesh);
                SetRef(field, "material", mr.sharedMaterial);
            }
            else Debug.LogWarning("[PLAYGROUND] Grass1 prefab not found; skipping grass");

            // ---------- Sky ----------
            var skyMat = AssetDatabase.LoadAssetAtPath<Material>(LooksmithDir + "Sky/Looksmith Sky.mat");
            var night = AssetDatabase.LoadAssetAtPath<PaleFernStudio.Looksmith.LooksmithSkyPreset>(LooksmithDir + "Sky/Presets/Cozy Night Sky.asset");
            var golden = AssetDatabase.LoadAssetAtPath<PaleFernStudio.Looksmith.LooksmithSkyPreset>(LooksmithDir + "Sky/Presets/Golden Hour Sky.asset");
            var day = AssetDatabase.LoadAssetAtPath<PaleFernStudio.Looksmith.LooksmithSkyPreset>(LooksmithDir + "Sky/Presets/Morning Sky.asset");
            if (skyMat == null || night == null || golden == null || day == null)
            {
                Debug.LogWarning("[PLAYGROUND] Looksmith sky assets not found; skipping the sky");
                return;
            }
            var skyGo = new GameObject("Sky");
            var sky = skyGo.AddComponent<PaleFernStudio.Looksmith.LooksmithSky>();
            sky.skyMaterial = skyMat;
            sky.preset = day;
            if (sunGo != null) sky.sun = sunGo.GetComponent<Light>();
            sky.applyReflections = false; // rebuilding the reflection cubemap every few frames is not Quest-friendly
            EditorUtility.SetDirty(sky);

            // The sky now moves the sun; the survival clock only keeps time.
            SetRef(cycle, "sun", null);

            var tod = world.AddComponent<Makeshift.Rendering.TimeOfDaySky>();
            SetRef(tod, "sky", sky);
            SetRef(tod, "cycle", cycle);
            SetRef(tod, "night", night);
            SetRef(tod, "goldenHour", golden);
            SetRef(tod, "day", day);
        }

        /// <summary>Props the template builds get URP Lit; move them to Looksmith Lit so the styles reach them. The player rig stays as it is.</summary>
        static void RestyleSceneMaterials()
        {
            if (!RenderSetup.UseLooksmithLit) return; // Looksmith Lit is parked (breaks Android builds); keep URP Lit
            var styled = RenderSetup.StyledShader();
            if (styled == null) return;
            var player = GameObject.Find("XRPlayer");
            int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (player != null && r.transform.IsChildOf(player.transform)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.shader.name != "Universal Render Pipeline/Lit") continue;
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    m.shader = styled;
                    TintMaterial(m, c);
                    n++;
                }
            }
            Debug.Log($"[PLAYGROUND] restyled {n} scene materials to Looksmith Lit");
        }

        // ---------- playground helpers ----------

        static void MakeFlammable(GameObject go, float ignitionHeat, float burnSeconds, Vector3 flamePos, float flameSize,
            float flameBaseRadius = -1f)
        {
            var f = go.AddComponent<Flammable>();
            SetFloat(f, "ignitionHeat", ignitionHeat);
            SetFloat(f, "burnDuration", burnSeconds);
            SetRef(f, "fireEffect", ParticleFactory.Flame(go.transform, flamePos, flameSize, false, flameBaseRadius));
        }

        static GameObject MakeMeatPrefab()
        {
            var meat = MakeMeat(Vector3.zero);
            var prefab = PrefabUtility.SaveAsPrefabAsset(meat, PrefabDir() + "RawMeat.prefab");
            Object.DestroyImmediate(meat);
            return prefab;
        }

        static GameObject MakeRaiderPrefab(GameObject meatPrefab)
        {
            var go = Root("Raider", new Vector3(0f, 0.02f, 0f));
            go.transform.localScale = Vector3.one * 1.5f; // 50% bigger than the first version
            var mouthPos = BuildScavengerVisual(go);
            go.AddComponent<Rigidbody>().isKinematic = true;
            go.AddComponent<Damageable>();
            var mouth = new GameObject("Mouth");
            mouth.transform.SetParent(go.transform, false);
            mouth.transform.localPosition = mouthPos;
            var creature = go.AddComponent<ScavengerCreature>();
            SetRef(creature, "mouth", mouth.transform);
            // Carcass: hit it once it is down and it drops meat.
            var carcass = go.AddComponent<Harvestable>();
            SetRef(carcass, "dropPrefab", meatPrefab);
            SetBool(carcass, "onlyWhenDead", true);
            SetBool(carcass, "destroyWhenDepleted", false);
            SetFloat(carcass, "strengthToBreak", 4f);
            var so = new SerializedObject(carcass);
            so.FindProperty("dropCount").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir() + "Raider.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A zone title in the air. 35% smaller than the first version.</summary>
        static TextMesh Label(string text, Vector3 pos, float yaw)
        {
            var go = new GameObject("Label");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = go.AddComponent<TextMesh>();
            go.AddComponent<Makeshift.Rendering.FloatingLabel>();
            tm.text = text;
            tm.characterSize = 0.0325f;
            tm.fontSize = 60;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return tm;
        }
    }
}
