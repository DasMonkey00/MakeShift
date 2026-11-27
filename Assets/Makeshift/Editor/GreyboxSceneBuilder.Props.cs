using System.IO;
using Autohand;
using Makeshift.Combat;
using Makeshift.Core;
using Makeshift.Creatures;
using Makeshift.Crafting;
using Makeshift.Fire;
using Makeshift.Machines;
using Makeshift.Movement;
using Makeshift.Survival;
using UnityEditor;
using UnityEngine;

namespace Makeshift.EditorTools
{
    /// <summary>
    /// Recognisable stand-in models for every test object, built from a few primitives each (about a dozen parts at most,
    /// one collider on the root). They are placeholders: swap a real mesh in later and keep the root, collider and components.
    /// </summary>
    public static partial class GreyboxSceneBuilder
    {
        // ---------- building helpers ----------

        static Material PropMat(Color c) => Mat("Prop_" + ColorUtility.ToHtmlStringRGB(c), c);

        /// <summary>A visual-only primitive part under a prop root.</summary>
        static GameObject Vis(Transform parent, string name, PrimitiveType shape, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = PropMat(c);
            return go;
        }

        /// <summary>A solid (collider kept) primitive part, for furniture.</summary>
        static GameObject Solid(Transform parent, string name, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = PropMat(c);
            return go;
        }

        static void BoxCol(GameObject go, Vector3 center, Vector3 size)
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
        }

        static void CapCol(GameObject go, Vector3 center, float radius, float height, int direction)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.center = center;
            c.radius = radius;
            c.height = height;
            c.direction = direction; // 0 = X, 1 = Y, 2 = Z
        }

        static void SphereCol(GameObject go, Vector3 center, float radius)
        {
            var c = go.AddComponent<SphereCollider>();
            c.center = center;
            c.radius = radius;
        }

        static PhysicsMaterial propPhysics;

        /// <summary>
        /// Shared friction for loose props. Rounded colliders (spheres, capsules) with no friction roll forever;
        /// this gives them real grip and a little bounce so they stop on the ground.
        /// </summary>
        static PhysicsMaterial PropPhysics()
        {
            if (propPhysics != null) return propPhysics;
            const string folder = "Assets/Makeshift/Physics";
            const string path = folder + "/PropFriction.physicsMaterial";
            propPhysics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (propPhysics == null)
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Makeshift", "Physics");
                propPhysics = new PhysicsMaterial("PropFriction")
                {
                    dynamicFriction = 0.7f,
                    staticFriction = 0.8f,
                    bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Maximum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
                AssetDatabase.CreateAsset(propPhysics, path);
            }
            return propPhysics;
        }

        /// <summary>Makes a prop a loose, grabbable rigidbody with the project's usual settings. Add colliders first.</summary>
        static GameObject Dyn(GameObject go, float mass, bool grabbable = true)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            // Damping and sleep threshold are what make a prop come to rest. The old values let rolling
            // props coast indefinitely; sleepThreshold makes slow props actually settle.
            rb.linearDamping = 0.3f;
            rb.angularDamping = 1.5f;
            rb.sleepThreshold = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                if (col.sharedMaterial == null) col.sharedMaterial = PropPhysics();
            go.AddComponent<Makeshift.Settling.PropSettle>();
            if (grabbable)
            {
                var g = go.AddComponent<Grabbable>();
                g.throwPower = 1.2f;
                g.ignoreReleaseTime = 0.3f;
                int layer = LayerMask.NameToLayer("Grabbable");
                if (layer >= 0) go.layer = layer;
            }
            return go;
        }

        static GameObject Root(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            return go;
        }

        static readonly Color Wood = new Color(0.5f, 0.35f, 0.2f);
        static readonly Color Bark = new Color(0.38f, 0.25f, 0.14f);
        static readonly Color Steel = new Color(0.72f, 0.74f, 0.78f);
        static readonly Color DarkMetal = new Color(0.16f, 0.17f, 0.2f);
        static readonly Color Rock = new Color(0.48f, 0.48f, 0.5f);

        // ---------- crafting and survival props ----------

        static GameObject MakeLog(Vector3 pos)
        {
            var go = Root("Log", pos);
            if (Model(go.transform, "log", 0.4f, 0, Pivot.Center, default, default, true) == null)
            {
                Vis(go.transform, "Bark", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.13f, 0.18f, 0.13f), Bark, new Vector3(0, 0, 90));
                Vis(go.transform, "EndA", PrimitiveType.Cylinder, new Vector3(0.181f, 0, 0), new Vector3(0.11f, 0.002f, 0.11f), new Color(0.78f, 0.62f, 0.4f), new Vector3(0, 0, 90));
                Vis(go.transform, "EndB", PrimitiveType.Cylinder, new Vector3(-0.181f, 0, 0), new Vector3(0.11f, 0.002f, 0.11f), new Color(0.78f, 0.62f, 0.4f), new Vector3(0, 0, 90));
            }
            CapCol(go, Vector3.zero, 0.065f, 0.36f, 0);
            Dyn(go, 1.5f);
            go.AddComponent<TraitHolder>().Add(Trait.Burns | Trait.Heavy);
            MakeFlammable(go, 3f, 20f, new Vector3(0f, 0.1f, 0f), 0.5f);
            return go;
        }

        static GameObject MakeTorch(Vector3 pos)
        {
            var go = Root("Torch", pos);
            if (Model(go.transform, "torch", 0.5f, 1) == null)
            {
                Vis(go.transform, "Handle", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.04f, 0.22f, 0.04f), Wood);
                Vis(go.transform, "Wrap", PrimitiveType.Cylinder, new Vector3(0, 0.2f, 0), new Vector3(0.056f, 0.025f, 0.056f), new Color(0.75f, 0.68f, 0.5f));
                Vis(go.transform, "Head", PrimitiveType.Sphere, new Vector3(0, 0.255f, 0), new Vector3(0.09f, 0.1f, 0.09f), new Color(0.14f, 0.1f, 0.07f));
            }
            CapCol(go, new Vector3(0, 0.03f, 0), 0.03f, 0.55f, 1);
            Dyn(go, 0.3f);
            go.AddComponent<TraitHolder>().Add(Trait.Burns);
            go.AddComponent<NotFuel>(); // lit at the campfire, never used up by it
            // The flame starts low in the head and is wide enough to swallow it.
            MakeFlammable(go, 2f, 90f, new Vector3(0f, 0.14f, 0f), 0.65f, 0.06f);
            return go;
        }

        static GameObject MakeStone(Vector3 pos)
        {
            var go = Root("Stone", pos);
            if (Model(go.transform, "stone", 0.17f) == null)
            {
                Vis(go.transform, "Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.16f, 0.11f, 0.14f), Rock, new Vector3(0, 25, 0));
                Vis(go.transform, "Chunk", PrimitiveType.Sphere, new Vector3(0.04f, 0.015f, 0.03f), new Vector3(0.1f, 0.08f, 0.09f), new Color(0.4f, 0.4f, 0.42f));
            }
            SphereCol(go, Vector3.zero, 0.07f);
            Dyn(go, 1f);
            go.AddComponent<TraitHolder>().Add(Trait.Heavy);
            go.AddComponent<ImpactDamage>();
            var breakable = go.AddComponent<Breakable>();
            SetRef(breakable, "piecePrefab", StonePiecePrefab());
            SetRef(breakable, "flatPrefab", FlatRockPrefab());
            return go;
        }

        static GameObject stonePiece;

        /// <summary>What a cracked stone leaves behind: a small stone that does not break again.</summary>
        static GameObject StonePiecePrefab()
        {
            if (stonePiece != null) return stonePiece;
            var go = Root("StonePiece", Vector3.zero);
            if (Model(go.transform, "stone", 0.09f) == null)
                Vis(go.transform, "Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.085f, 0.065f, 0.075f), Rock);
            SphereCol(go, Vector3.zero, 0.037f);
            Dyn(go, 0.3f);
            go.AddComponent<TraitHolder>().Add(Trait.Heavy);
            go.AddComponent<ImpactDamage>();
            stonePiece = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir() + "StonePiece.prefab");
            Object.DestroyImmediate(go);
            return stonePiece;
        }

        static GameObject flatRock;

        /// <summary>The flat, sharp piece of a cracked stone. Glue it to a stick for an axe.</summary>
        static GameObject FlatRockPrefab()
        {
            if (flatRock != null) return flatRock;
            var go = Root("FlatRock", Vector3.zero);
            var size = new Vector3(0.15f, 0.03f, 0.11f);
            if (ModelFit(go.transform, "stone", size) == null)
                Vis(go.transform, "Slab", PrimitiveType.Cube, Vector3.zero, size, Rock, new Vector3(0, 15, 0));
            BoxCol(go, Vector3.zero, size);
            Dyn(go, 0.45f);
            go.AddComponent<TraitHolder>().Add(Trait.Heavy | Trait.Sharp);
            go.AddComponent<ImpactDamage>();
            go.AddComponent<AxeHead>(); // glued to a stick, it cuts trees
            flatRock = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir() + "FlatRock.prefab");
            Object.DestroyImmediate(go);
            return flatRock;
        }

        static GameObject MakeFlint(Vector3 pos, ParticleSystem sparks)
        {
            var go = Root("Flint", pos);
            Vis(go.transform, "Blade", PrimitiveType.Cube, Vector3.zero, new Vector3(0.05f, 0.014f, 0.09f), new Color(0.13f, 0.14f, 0.19f), new Vector3(0, 12, 0));
            Vis(go.transform, "Tip", PrimitiveType.Cube, new Vector3(0.004f, 0, 0.055f), new Vector3(0.03f, 0.012f, 0.03f), new Color(0.13f, 0.14f, 0.19f), new Vector3(0, 45, 0));
            Vis(go.transform, "Chip", PrimitiveType.Cube, new Vector3(0.01f, 0.008f, -0.01f), new Vector3(0.02f, 0.005f, 0.04f), new Color(0.4f, 0.45f, 0.55f), new Vector3(0, 30, 0));
            BoxCol(go, new Vector3(0, 0, 0.01f), new Vector3(0.055f, 0.025f, 0.11f));
            Dyn(go, 0.2f);
            go.AddComponent<TraitHolder>().Add(Trait.Sharp);
            go.AddComponent<ImpactDamage>();
            var striker = go.AddComponent<SparkStriker>();
            SetRef(striker, "sparks", sparks);
            return go;
        }

        static GameObject MakeStick(Vector3 pos)
        {
            var go = Root("Stick", pos);
            if (Model(go.transform, "stick", 0.44f, 0, Pivot.Center, default, default, true) == null)
            {
                Vis(go.transform, "Branch", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.035f, 0.22f, 0.035f), Wood, new Vector3(0, 0, 90));
                Vis(go.transform, "Twig", PrimitiveType.Cylinder, new Vector3(0.06f, 0.03f, 0f), new Vector3(0.014f, 0.045f, 0.014f), Wood, new Vector3(0, 0, 40));
                Vis(go.transform, "Knot", PrimitiveType.Sphere, new Vector3(-0.08f, 0.012f, 0f), new Vector3(0.05f, 0.045f, 0.05f), Bark);
            }
            CapCol(go, Vector3.zero, 0.022f, 0.44f, 0);
            Dyn(go, 0.2f);
            go.AddComponent<TraitHolder>().Add(Trait.Burns);
            go.AddComponent<ImpactDamage>();
            MakeFlammable(go, 3f, 15f, new Vector3(0f, 0.05f, 0f), 0.4f);
            go.AddComponent<Makeshift.Crafting.Gluable>();
            return go;
        }

        /// <summary>A bucket to scoop water from the trough and pour on the fire.</summary>
        static GameObject MakeBucket(Vector3 pos)
        {
            var go = Root("Bucket", pos);
            if (Model(go.transform, "bucket", 0.13f, 1, Pivot.Bottom) != null)
            {
                // Poly by Google bucket model, at half size. The collider is fitted to the drawn model.
                var hb = HolderBounds(go.transform);
                BoxCol(go, hb.center, hb.size);
            }
            else
            {
                Vis(go.transform, "Pail", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.22f, 0.12f, 0.22f), Wood);
                Vis(go.transform, "Rim", PrimitiveType.Cylinder, new Vector3(0, 0.12f, 0), new Vector3(0.23f, 0.01f, 0.23f), DarkMetal);
                // The pail is drawn at 0.22 m across and 0.24 m tall (the cylinder primitive is 1 m across, 2 m tall before scaling).
                BoxCol(go, Vector3.zero, new Vector3(0.22f, 0.24f, 0.22f));
            }
            Dyn(go, 0.6f);
            // Water you can see: a surface inside the pail when it is full, and a stream from the lip when tipped.
            var surface = Vis(go.transform, "WaterInBucket", PrimitiveType.Cylinder, new Vector3(0, 0.09f, 0), new Vector3(0.09f, 0.003f, 0.09f), new Color(0.25f, 0.55f, 0.9f));
            var stream = ParticleFactory.Spray(go.transform, new Vector3(0.07f, 0.13f, 0f), Vector3.down);
            var bucket = go.AddComponent<Makeshift.Survival.Bucket>();
            bucket.SetVisuals(surface, stream);
            return go;
        }

        /// <summary>A tub of glue. Dip a stick in it to make the end sticky for a while.</summary>
        static GameObject MakeGlueTub(Vector3 pos)
        {
            var go = Root("GlueTub", pos);
            Vis(go.transform, "Tub", PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(0.2f, 0.05f, 0.2f), new Color(0.55f, 0.4f, 0.2f));
            Vis(go.transform, "Glue", PrimitiveType.Cylinder, new Vector3(0, 0.095f, 0), new Vector3(0.17f, 0.01f, 0.17f), new Color(0.9f, 0.75f, 0.35f));
            BoxCol(go, new Vector3(0, 0.05f, 0), new Vector3(0.2f, 0.1f, 0.2f));
            var zone = new GameObject("DipZone");
            zone.transform.SetParent(go.transform, false);
            zone.transform.localPosition = new Vector3(0, 0.08f, 0);
            var box = zone.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.16f, 0.12f, 0.16f);
            // A grabbable prop, so it can be picked up and carried to the stick. It drops and sits on the table.
            Dyn(go, 0.5f);
            zone.AddComponent<Makeshift.Crafting.GlueTub>();
            return go;
        }

        static GameObject MakeVine(Vector3 pos)
        {
            // A loose green coil, in the same rope and colour as the tight lashing it ends up as.
            var coil = ParticleFactory.VineCoil();
            var go = Root("Vine", pos);
            var loops = new GameObject("Loops");
            loops.transform.SetParent(go.transform, false);
            loops.AddComponent<MeshFilter>().sharedMesh = coil;
            loops.AddComponent<MeshRenderer>().sharedMaterial = ParticleFactory.LashMaterial();
            BoxCol(go, coil.bounds.center, coil.bounds.size);
            Dyn(go, 0.05f);
            var binder = go.AddComponent<StickyBinder>();
            SetRef(binder, "ringMesh", coil);
            SetRef(binder, "ringMaterial", ParticleFactory.LashMaterial());
            go.AddComponent<TraitHolder>().Add(Trait.Sticks);
            return go;
        }

        /// <summary>Raw drumstick. The first child is the meat: Cookable tints that renderer.</summary>
        static GameObject MakeMeat(Vector3 pos)
        {
            var go = Root("RawMeat", pos);
            // The model's first material is the meat, so Cookable's tint (raw, cooked brown, burnt black) lands on the meat.
            if (Model(go.transform, "meat", 0.26f, 0, Pivot.Center, new Vector3(0, 0, -90)) == null)
            {
                Vis(go.transform, "Meat", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.11f, 0.08f, 0.08f), new Color(0.75f, 0.2f, 0.25f));
                Vis(go.transform, "Bone", PrimitiveType.Cylinder, new Vector3(0.1f, 0, 0), new Vector3(0.018f, 0.05f, 0.018f), new Color(0.93f, 0.9f, 0.8f), new Vector3(0, 0, 90));
                Vis(go.transform, "KnobA", PrimitiveType.Sphere, new Vector3(0.155f, 0.012f, 0), new Vector3(0.035f, 0.035f, 0.035f), new Color(0.93f, 0.9f, 0.8f));
                Vis(go.transform, "KnobB", PrimitiveType.Sphere, new Vector3(0.155f, -0.012f, 0), new Vector3(0.035f, 0.035f, 0.035f), new Color(0.93f, 0.9f, 0.8f));
            }
            CapCol(go, new Vector3(0.04f, 0, 0), 0.045f, 0.24f, 0);
            Dyn(go, 0.4f);
            go.AddComponent<TraitHolder>().Add(Trait.Burns);
            go.AddComponent<Edible>();
            go.AddComponent<Cookable>();
            go.AddComponent<Scavengable>(); // scavengers love meat
            return go;
        }

        /// <summary>A flat sheet you can pick up and read. The text is on the top face (red corner = bottom left).</summary>
        static GameObject MakePaper(string name, Vector3 pos, float yaw, params string[] lines)
        {
            var go = Root(name, pos);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Vis(go.transform, "Sheet", PrimitiveType.Cube, Vector3.zero, new Vector3(0.34f, 0.006f, 0.26f), new Color(0.93f, 0.9f, 0.78f));
            Vis(go.transform, "Corner", PrimitiveType.Cube, new Vector3(-0.15f, 0.004f, -0.11f), new Vector3(0.05f, 0.002f, 0.05f), new Color(0.85f, 0.2f, 0.2f), new Vector3(0, 45, 0));
            string text = string.Join("\n", lines);
            // One face only: Unity's text shader ignores depth, so a second face would show through the sheet.
            PaperText(go.transform, text, new Vector3(0f, 0.004f, 0f), Quaternion.Euler(90f, 0f, 0f));
            BoxCol(go, Vector3.zero, new Vector3(0.34f, 0.012f, 0.26f));
            Dyn(go, 0.05f);
            return go;
        }

        static void PaperText(Transform parent, string text, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 40;
            tm.characterSize = 0.0042f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Left;
            tm.color = new Color(0.1f, 0.08f, 0.05f);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        // ---------- furniture and scenery ----------

        /// <summary>A table. With a model file it uses that model scaled to modelHeight (size is ignored); else primitives.</summary>
        static GameObject MakeTable(string name, Vector3 pos, Vector2 size, string modelFile = null, float modelHeight = 0.75f)
        {
            var go = Root(name, pos);
            if (modelFile != null && Model(go.transform, modelFile, modelHeight, 1, Pivot.Bottom) != null)
            {
                var b = HolderBounds(go.transform);
                // Hands and dropped things use the full table. The player's body passes this one by...
                var top = new GameObject("TopCollider");
                top.transform.SetParent(go.transform, false);
                BoxCol(top, b.center, b.size);
                top.layer = FurnitureLayer();
                // ...and is stopped by a smaller box inside it, so you can stand right up against the edge and reach.
                var block = new GameObject("PlayerBlock");
                block.transform.SetParent(go.transform, false);
                BoxCol(block, b.center, new Vector3(Mathf.Max(0.1f, b.size.x - 0.5f), b.size.y * 0.96f, Mathf.Max(0.1f, b.size.z - 0.5f)));
                return go;
            }
            Solid(go.transform, "Top", new Vector3(0, 0.725f, 0), new Vector3(size.x, 0.05f, size.y), new Color(0.5f, 0.38f, 0.25f));
            float lx = size.x / 2f - 0.05f, lz = size.y / 2f - 0.05f;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Solid(go.transform, "Leg", new Vector3(sx * lx, 0.35f, sz * lz), new Vector3(0.06f, 0.7f, 0.06f), new Color(0.4f, 0.3f, 0.2f));
            return go;
        }

        static Campfire MakeCampfire(Vector3 pos)
        {
            var go = Root("Campfire", pos);
            bool modelled = Model(go.transform, "campfire", 0.95f, -1, Pivot.Bottom) != null;
            if (!modelled) Vis(go.transform, "Ash", PrimitiveType.Cylinder, new Vector3(0, 0.022f, 0), new Vector3(0.56f, 0.004f, 0.56f), new Color(0.1f, 0.09f, 0.09f));
            // The pit: a floor and a ring of stones, so things dropped in stay in.
            var floor = new GameObject("PitFloor");
            floor.transform.SetParent(go.transform, false);
            BoxCol(floor, new Vector3(0, 0f, 0), new Vector3(0.5f, 0.045f, 0.5f));
            // Fuel bed: solid only while the fire has fuel (Campfire toggles it). Logs and meat dropped in land on it.
            var bed = new GameObject("FuelBed");
            bed.transform.SetParent(go.transform, false);
            bed.transform.localPosition = new Vector3(0, 0.12f, 0);
            BoxCol(bed, Vector3.zero, new Vector3(0.42f, 0.1f, 0.42f));
            // Warm light from the fire. Off until lit (Campfire toggles it). No shadows, to keep Quest cheap.
            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 0.35f, 0);
            var fl = lightGo.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.color = ParticleFactory.FireLightColor;
            fl.range = 4.5f;
            fl.intensity = 1f;
            fl.shadows = LightShadows.None;
            fl.enabled = false;
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f;
                float ringR = modelled ? 0.38f : 0.3f;
                var s = Vis(go.transform, "RingStone" + i, PrimitiveType.Sphere,
                    new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * ringR, new Vector3(0.16f, 0.11f, 0.14f),
                    Color.Lerp(new Color(0.42f, 0.42f, 0.44f), new Color(0.55f, 0.54f, 0.52f), (i % 3) / 2f), new Vector3(0, -a * Mathf.Rad2Deg, 0));
                var col = s.AddComponent<SphereCollider>();
                col.radius = 0.5f; // of the stone's own scale
                if (modelled) s.GetComponent<Renderer>().enabled = false; // the model draws the stones; these only hold things in
            }

            var pile = new GameObject("FuelPile");
            pile.transform.SetParent(go.transform, false);
            ParticleFactory.StyleFuelPile(pile.transform);
            pile.SetActive(false);

            var trig = go.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 0.4f;
            trig.center = new Vector3(0f, 0.2f, 0f);
            var heat = go.AddComponent<HeatSource>();
            SetFloat(heat, "radius", 0.65f);       // cooking
            SetFloat(heat, "igniteRadius", 0.4f);  // sets things alight: only about as far as the flames
            SetFloat(heat, "igniteHeight", 0.2f);
            SetBool(heat, "active", false);
            var fire = go.AddComponent<Campfire>();
            SetRef(fire, "flameVisual", ParticleFactory.Flame(go.transform, new Vector3(0f, 0.08f, 0f), 1f, true));
            SetRef(fire, "fuelVisual", pile);
            SetRef(fire, "outSmoke", ParticleFactory.SmokeColumn(go.transform, new Vector3(0f, 0.12f, 0f), 3.5f, 0.22f));
            return fire;
        }

        static GameObject MakeTrough(Vector3 pos)
        {
            var go = Root("WaterTrough", pos);
            Color plank = new Color(0.45f, 0.32f, 0.2f);
            bool modelled = Model(go.transform, "trough", 1.05f, 0, Pivot.Bottom, new Vector3(0, 90, 0)) != null;
            var floorPart = Solid(go.transform, "Floor", new Vector3(0, 0.02f, 0), new Vector3(1.0f, 0.04f, 0.6f), plank);
            var wA = Solid(go.transform, "WallA", new Vector3(0, 0.19f, 0.28f), new Vector3(1.0f, 0.34f, 0.05f), plank);
            var wB = Solid(go.transform, "WallB", new Vector3(0, 0.19f, -0.28f), new Vector3(1.0f, 0.34f, 0.05f), plank);
            var wC = Solid(go.transform, "WallC", new Vector3(0.475f, 0.19f, 0), new Vector3(0.05f, 0.34f, 0.5f), plank);
            var wD = Solid(go.transform, "WallD", new Vector3(-0.475f, 0.19f, 0), new Vector3(0.05f, 0.34f, 0.5f), plank);
            var waterVis = Vis(go.transform, "Water", PrimitiveType.Cube, new Vector3(0, 0.22f, 0), new Vector3(0.9f, 0.02f, 0.5f), new Color(0.2f, 0.5f, 0.85f));
            if (modelled)
                foreach (var part in new[] { floorPart, wA, wB, wC, wD, waterVis }) part.GetComponent<Renderer>().enabled = false; // colliders stay

            var volume = new GameObject("Volume");
            volume.transform.SetParent(go.transform, false);
            var col = volume.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0, 0.2f, 0);
            col.size = new Vector3(0.9f, 0.28f, 0.5f);
            var water = volume.AddComponent<WaterVolume>();

            // A spout over the trough: a stream of droplets, and a splash where things drop in.
            Solid(go.transform, "SpoutPost", new Vector3(0.35f, 0.36f, -0.36f), new Vector3(0.07f, 0.72f, 0.07f), plank);
            Solid(go.transform, "SpoutArm", new Vector3(0.35f, 0.69f, -0.22f), new Vector3(0.05f, 0.05f, 0.34f), new Color(0.45f, 0.47f, 0.5f));
            ParticleFactory.Spray(go.transform, new Vector3(0.35f, 0.67f, -0.05f), new Vector3(0f, -0.35f, 1f));
            SetRef(water, "splash", ParticleFactory.Burst(go.transform, new Vector3(0.35f, 0.25f, 0.2f), false));
            return go;
        }

        static GameObject MakeDummy(Vector3 pos)
        {
            var go = Root("Dummy", pos);
            Color straw = new Color(0.75f, 0.65f, 0.4f);
            // The model faces +Z; the dummies stand south-to-north facing the player, so turn it round.
            if (Model(go.transform, "dummy", 1.3f, 1, Pivot.Center, new Vector3(0, 180, 0)) == null)
            {
                Vis(go.transform, "Post", PrimitiveType.Cylinder, new Vector3(0, -0.05f, 0), new Vector3(0.07f, 0.55f, 0.07f), Wood);
                Vis(go.transform, "Base", PrimitiveType.Cylinder, new Vector3(0, -0.635f, 0), new Vector3(0.4f, 0.015f, 0.4f), new Color(0.35f, 0.27f, 0.18f));
                Vis(go.transform, "Torso", PrimitiveType.Capsule, new Vector3(0, 0.15f, 0), new Vector3(0.32f, 0.26f, 0.22f), straw);
                Vis(go.transform, "Arms", PrimitiveType.Cylinder, new Vector3(0, 0.25f, 0), new Vector3(0.06f, 0.32f, 0.06f), Wood, new Vector3(0, 0, 90));
                Vis(go.transform, "Head", PrimitiveType.Sphere, new Vector3(0, 0.53f, 0), Vector3.one * 0.2f, new Color(0.8f, 0.7f, 0.5f));
            }
            // Everything drawn hangs off a pivot at the foot, so a hit can rock it.
            var pivot = new GameObject("Sway").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(0f, -0.65f, 0f);
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                if (go.transform.GetChild(i) != pivot) go.transform.GetChild(i).SetParent(pivot, true);
            // No rigidbody: the dummy is fixed, so walking into it cannot push it.
            CapCol(go, new Vector3(0, 0f, 0), 0.2f, 1.3f, 1);
            var h = go.AddComponent<Damageable>();
            SetFloat(h, "maxHealth", 100000f); // a practice post never dies
            SetRef(go.AddComponent<TrainingDummy>(), "visual", pivot);
            return go;
        }

        static GameObject MakeHold(string name, Vector3 pos, float yaw)
        {
            var go = Root(name, pos);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // Climbing holds are the stone model, painted orange so they stand out on the wall.
            if (Model(go.transform, "stone", 0.2f, -1, Pivot.Center, default, default, false, new Color(0.85f, 0.5f, 0.18f)) == null)
            {
                Vis(go.transform, "Rock", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.18f, 0.12f, 0.14f), new Color(0.8f, 0.5f, 0.2f));
                Vis(go.transform, "Lip", PrimitiveType.Sphere, new Vector3(0, -0.03f, -0.02f), new Vector3(0.14f, 0.07f, 0.12f), new Color(0.65f, 0.4f, 0.16f));
            }
            BoxCol(go, Vector3.zero, new Vector3(0.18f, 0.12f, 0.14f));
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            int layer = LayerMask.NameToLayer("Grabbable");
            if (layer >= 0) go.layer = layer;
            // Fixed to the wall: the hand goes to the hold, the hold never goes to the hand.
            var g = go.AddComponent<Grabbable>();
            g.grabType = HandGrabType.HandToGrabbable;
            g.parentOnGrab = false;
            g.instantGrab = false;
            g.throwPower = 0f;
            return go;
        }

        static GameObject MakeUmbrella(Vector3 pos)
        {
            var go = Root("Umbrella", pos);
            Color red = new Color(0.78f, 0.15f, 0.15f);
            Vis(go.transform, "Shaft", PrimitiveType.Cylinder, new Vector3(0, 0.075f, 0), new Vector3(0.025f, 0.375f, 0.025f), new Color(0.25f, 0.2f, 0.18f));
            Vis(go.transform, "Crook", PrimitiveType.Sphere, new Vector3(0, -0.3f, 0), Vector3.one * 0.055f, new Color(0.25f, 0.2f, 0.18f));
            var furled = new GameObject("Furled");
            furled.transform.SetParent(go.transform, false);
            Vis(furled.transform, "Cloth", PrimitiveType.Cylinder, new Vector3(0, 0.28f, 0), new Vector3(0.075f, 0.17f, 0.075f), red);
            Vis(furled.transform, "Tie", PrimitiveType.Cylinder, new Vector3(0, 0.2f, 0), new Vector3(0.082f, 0.015f, 0.082f), new Color(0.9f, 0.85f, 0.7f));
            var canopy = new GameObject("Canopy");
            canopy.transform.SetParent(go.transform, false);
            Vis(canopy.transform, "Dome", PrimitiveType.Sphere, new Vector3(0, 0.5f, 0), new Vector3(1.1f, 0.34f, 1.1f), red);
            Vis(canopy.transform, "Ferrule", PrimitiveType.Cylinder, new Vector3(0, 0.67f, 0), new Vector3(0.02f, 0.05f, 0.02f), Steel);
            canopy.SetActive(false);
            CapCol(go, new Vector3(0, 0.05f, 0), 0.04f, 0.8f, 1);
            // Everything drawn sits under one child, so it can stand upright while the player floats.
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            for (int i = go.transform.childCount - 1; i >= 0; i--)
                if (go.transform.GetChild(i) != visual) go.transform.GetChild(i).SetParent(visual, true);
            Dyn(go, 0.4f);
            go.AddComponent<TraitHolder>().Add(Trait.Blows);
            var glider = go.AddComponent<Glider>();
            SetRef(glider, "canopy", canopy);
            SetRef(glider, "furled", furled);
            return go;
        }

        // ---------- creatures ----------

        /// <summary>The scavenger's body, facing +Z, standing on y = 0 with its origin at its feet.</summary>
        /// <summary>The scavenger's body (frog model, else primitives). Returns where its mouth is, in root space.</summary>
        static Vector3 BuildScavengerVisual(GameObject root)
        {
            if (Model(root.transform, "critter", 0.45f, -1, Pivot.Bottom) != null)
            {
                CapCol(root, new Vector3(0, 0.17f, 0.02f), 0.17f, 0.42f, 2);
                return new Vector3(0f, 0.14f, 0.14f);
            }
            BuildScavengerPlaceholder(root);
            return new Vector3(0f, 0.24f, 0.34f);
        }

        static void BuildScavengerPlaceholder(GameObject root)
        {
            Color fur = new Color(0.45f, 0.3f, 0.5f);
            Color belly = new Color(0.7f, 0.58f, 0.6f);
            var t = root.transform;
            Vis(t, "Body", PrimitiveType.Sphere, new Vector3(0, 0.2f, 0), new Vector3(0.22f, 0.2f, 0.32f), fur);
            Vis(t, "Belly", PrimitiveType.Sphere, new Vector3(0, 0.15f, 0.02f), new Vector3(0.17f, 0.12f, 0.24f), belly);
            Vis(t, "Head", PrimitiveType.Sphere, new Vector3(0, 0.27f, 0.2f), new Vector3(0.15f, 0.14f, 0.15f), fur);
            Vis(t, "Snout", PrimitiveType.Sphere, new Vector3(0, 0.25f, 0.29f), new Vector3(0.07f, 0.06f, 0.08f), belly);
            Vis(t, "Nose", PrimitiveType.Sphere, new Vector3(0, 0.26f, 0.33f), Vector3.one * 0.025f, DarkMetal);
            Vis(t, "EyeL", PrimitiveType.Sphere, new Vector3(-0.045f, 0.3f, 0.26f), Vector3.one * 0.03f, new Color(1f, 0.85f, 0.2f));
            Vis(t, "EyeR", PrimitiveType.Sphere, new Vector3(0.045f, 0.3f, 0.26f), Vector3.one * 0.03f, new Color(1f, 0.85f, 0.2f));
            Vis(t, "EarL", PrimitiveType.Sphere, new Vector3(-0.065f, 0.36f, 0.17f), new Vector3(0.05f, 0.08f, 0.02f), fur, new Vector3(0, 0, 15));
            Vis(t, "EarR", PrimitiveType.Sphere, new Vector3(0.065f, 0.36f, 0.17f), new Vector3(0.05f, 0.08f, 0.02f), fur, new Vector3(0, 0, -15));
            Vis(t, "Tail", PrimitiveType.Cylinder, new Vector3(0, 0.25f, -0.3f), new Vector3(0.03f, 0.1f, 0.03f), fur, new Vector3(60, 0, 0));
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    Vis(t, "Leg", PrimitiveType.Cylinder, new Vector3(sx * 0.075f, 0.065f, sz * 0.1f), new Vector3(0.05f, 0.065f, 0.05f), fur);
            CapCol(root, new Vector3(0, 0.2f, 0), 0.15f, 0.5f, 2);
        }

        // ---------- machine parts ----------

        static GameObject MakeMotor(Vector3 pos)
        {
            var go = Root("PowerDrill", pos);
            Color orange = new Color(0.92f, 0.5f, 0.1f);
            Vis(go.transform, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(0.14f, 0.09f, 0.2f), orange);
            Vis(go.transform, "Grip", PrimitiveType.Cube, new Vector3(0, -0.08f, -0.04f), new Vector3(0.05f, 0.11f, 0.06f), DarkMetal, new Vector3(12, 0, 0));
            Vis(go.transform, "Barrel", PrimitiveType.Cylinder, new Vector3(0, 0.005f, 0.13f), new Vector3(0.07f, 0.04f, 0.07f), Steel, new Vector3(90, 0, 0));
            Vis(go.transform, "Chuck", PrimitiveType.Cylinder, new Vector3(0, 0.005f, 0.185f), new Vector3(0.04f, 0.02f, 0.04f), DarkMetal, new Vector3(90, 0, 0));
            Vis(go.transform, "Trigger", PrimitiveType.Cube, new Vector3(0, -0.03f, 0.02f), new Vector3(0.015f, 0.03f, 0.02f), Color.black);
            BoxCol(go, new Vector3(0, -0.02f, 0.03f), new Vector3(0.14f, 0.16f, 0.3f));
            Dyn(go, 0.8f);
            return go;
        }

        static GameObject MakeToaster(Vector3 pos)
        {
            var go = Root("Toaster", pos);
            if (Model(go.transform, "toaster", 0.22f, 0) == null)
            {
                Vis(go.transform, "Body", PrimitiveType.Cube, Vector3.zero, new Vector3(0.2f, 0.12f, 0.12f), Steel);
                Vis(go.transform, "SlotA", PrimitiveType.Cube, new Vector3(-0.045f, 0.061f, 0), new Vector3(0.07f, 0.004f, 0.012f), DarkMetal);
                Vis(go.transform, "SlotB", PrimitiveType.Cube, new Vector3(0.045f, 0.061f, 0), new Vector3(0.07f, 0.004f, 0.012f), DarkMetal);
                Vis(go.transform, "Toast", PrimitiveType.Cube, new Vector3(-0.045f, 0.075f, 0), new Vector3(0.055f, 0.045f, 0.008f), new Color(0.78f, 0.55f, 0.25f));
            }
            BoxCol(go, Vector3.zero, new Vector3(0.2f, 0.13f, 0.12f));
            Dyn(go, 1f);
            return go;
        }

        static GameObject MakeBattery(Vector3 pos)
        {
            var go = Root("CarBattery", pos);
            if (Model(go.transform, "battery", 0.22f, 0) == null)
            {
                Vis(go.transform, "Case", PrimitiveType.Cube, Vector3.zero, new Vector3(0.2f, 0.14f, 0.12f), new Color(0.13f, 0.14f, 0.2f));
                Vis(go.transform, "Label", PrimitiveType.Cube, new Vector3(0, 0, -0.061f), new Vector3(0.12f, 0.07f, 0.003f), new Color(0.9f, 0.8f, 0.2f));
                Vis(go.transform, "PlusPost", PrimitiveType.Cylinder, new Vector3(0.07f, 0.09f, 0), new Vector3(0.03f, 0.02f, 0.03f), new Color(0.85f, 0.15f, 0.15f));
                Vis(go.transform, "MinusPost", PrimitiveType.Cylinder, new Vector3(-0.07f, 0.09f, 0), new Vector3(0.03f, 0.02f, 0.03f), DarkMetal);
            }
            BoxCol(go, new Vector3(0, 0.01f, 0), new Vector3(0.22f, 0.19f, 0.17f));
            Dyn(go, 2.5f);
            go.AddComponent<TraitHolder>().Add(Trait.Powers | Trait.Heavy);
            go.AddComponent<Makeshift.Power.PowerSource>();
            go.AddComponent<ImpactDamage>();
            return go;
        }

        static GameObject MakePlug(Vector3 pos, Makeshift.Power.PowerSource source)
        {
            var go = Root("CablePlug", pos);
            Color yellow = new Color(0.95f, 0.85f, 0.1f);
            // The cable model is a coil with plug ends. Real rope/wire physics is a later pass.
            if (Model(go.transform, "cable", 0.24f, -1, Pivot.Center) == null)
            {
                Vis(go.transform, "Head", PrimitiveType.Cube, Vector3.zero, new Vector3(0.055f, 0.05f, 0.07f), yellow);
                Vis(go.transform, "ProngA", PrimitiveType.Cube, new Vector3(-0.012f, 0, 0.05f), new Vector3(0.008f, 0.014f, 0.04f), Steel);
                Vis(go.transform, "ProngB", PrimitiveType.Cube, new Vector3(0.012f, 0, 0.05f), new Vector3(0.008f, 0.014f, 0.04f), Steel);
                Vis(go.transform, "Cable", PrimitiveType.Cylinder, new Vector3(0, 0, -0.07f), new Vector3(0.018f, 0.04f, 0.018f), DarkMetal, new Vector3(90, 0, 0));
            }
            SphereCol(go, new Vector3(0, 0, 0), 0.07f);
            Dyn(go, 0.1f);
            var plug = go.AddComponent<Makeshift.Power.PowerPlug>();
            plug.SetSource(source);
            // Visible wire from the plug to the battery. Visual only; the plug's power logic is unchanged.
            if (source != null)
                Makeshift.Rendering.RopeLine.Create(go, go.transform, Vector3.zero, source.transform, Vector3.up * 0.1f, new Color(0.1f, 0.1f, 0.1f), 0.01f);
            return go;
        }

        /// <summary>Half-width of the club's collider. The handle is thin; the old 0.06 made it about four times too fat.</summary>
        const float ClubShaftRadius = 0.03f;

        static GameObject MakeClub(Vector3 pos)
        {
            var go = Root("Club", pos);
            if (Model(go.transform, "club", 0.62f, 1) == null)
            {
                Vis(go.transform, "Handle", PrimitiveType.Cylinder, new Vector3(0, -0.07f, 0), new Vector3(0.045f, 0.2f, 0.045f), Wood);
                Vis(go.transform, "Head", PrimitiveType.Capsule, new Vector3(0, 0.15f, 0), new Vector3(0.1f, 0.12f, 0.1f), Bark);
                Vis(go.transform, "Grip", PrimitiveType.Cylinder, new Vector3(0, -0.2f, 0), new Vector3(0.05f, 0.05f, 0.05f), new Color(0.75f, 0.68f, 0.5f));
                CapCol(go, new Vector3(0, 0, 0), ClubShaftRadius, 0.62f, 1);
            }
            else
            {
                // A box fitted to the club model was worse than a capsule (the diagonal head inflates the bounds).
                CapCol(go, new Vector3(0, 0, 0), ClubShaftRadius, 0.62f, 1);
            }
            Dyn(go, 1.2f);
            go.AddComponent<TraitHolder>().Add(Trait.Heavy);
            go.AddComponent<ImpactDamage>();
            go.AddComponent<StoneBreaker>();
            return go;
        }

        static GameObject MakeOreNugget(Vector3 pos)
        {
            var go = Root("OreChunk", pos);
            Color gold = new Color(0.82f, 0.62f, 0.15f);
            if (Model(go.transform, "orechunk", 0.1f) == null)
            {
                Vis(go.transform, "Core", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.08f, 0.065f, 0.07f), gold);
                Vis(go.transform, "BumpA", PrimitiveType.Sphere, new Vector3(0.03f, 0.02f, 0.01f), Vector3.one * 0.045f, new Color(0.95f, 0.78f, 0.25f));
            }
            SphereCol(go, Vector3.zero, 0.05f);
            Dyn(go, 0.3f);
            return go;
        }

        /// <summary>Visual-only drill: body, chuck, spinning bit and marked slots, added to the frame.</summary>
        static void AddDrillVisuals(GameObject frame, Drill drill)
        {
            var t = frame.transform;
            Vis(t, "Chuck", PrimitiveType.Cylinder, new Vector3(0, 0, 0.26f), new Vector3(0.15f, 0.03f, 0.15f), DarkMetal, new Vector3(90, 0, 0));
            var spin = new GameObject("SpinningBit");
            spin.transform.SetParent(t, false);
            spin.transform.localPosition = new Vector3(0, 0, 0.3f);
            Vis(spin.transform, "Shank", PrimitiveType.Cylinder, new Vector3(0, 0, 0.06f), new Vector3(0.045f, 0.06f, 0.045f), Steel, new Vector3(90, 0, 0));
            Vis(spin.transform, "Flute", PrimitiveType.Cube, new Vector3(0, 0, 0.06f), new Vector3(0.1f, 0.012f, 0.12f), Steel);
            SetRef(drill, "spinningBit", spin.transform);

            bool modelled = Model(t, "drill", 0.8f, 1, Pivot.Bottom, default, new Vector3(0f, -0.2f, 0f)) != null;
            if (modelled)
            {
                var body = t.Find("Visual");
                if (body != null) body.GetComponent<Renderer>().enabled = false; // the model replaces the yellow box
            }
            float padY = modelled ? -0.19f : 0.205f;
            float padX = modelled ? 0.38f : 0.12f;
            float ringZ = modelled ? -0.28f : -0.205f;
            float labelY = modelled ? 0.0f : 0.27f;
            Vis(t, "MotorPad", PrimitiveType.Cube, new Vector3(-padX, padY, 0), new Vector3(0.2f, 0.012f, 0.2f), new Color(0.2f, 0.6f, 0.9f));
            Vis(t, "HandlePad", PrimitiveType.Cube, new Vector3(padX, padY, 0), new Vector3(0.2f, 0.012f, 0.2f), new Color(0.9f, 0.6f, 0.2f));
            Vis(t, "PowerRing", PrimitiveType.Cylinder, new Vector3(0, 0, ringZ), new Vector3(0.12f, 0.01f, 0.12f), new Color(0.95f, 0.85f, 0.1f), new Vector3(90, 0, 0));
            Vis(t, "PowerHole", PrimitiveType.Cylinder, new Vector3(0, 0, ringZ - 0.007f), new Vector3(0.07f, 0.01f, 0.07f), Color.black, new Vector3(90, 0, 0));

            SmallLabel("HAND DRILL", t, new Vector3(0f, modelled ? 0.72f : 0.5f, 0f));
            SmallLabel("MOTOR", t, new Vector3(-padX, labelY, 0f));
            SmallLabel("HANDLE", t, new Vector3(padX, labelY, 0f));
        }

        static TextMesh SmallLabel(string text, Transform parent, Vector3 localPos)
        {
            var l = Label(text, parent.TransformPoint(localPos), 0f);
            l.characterSize = 0.0065f;
            l.transform.SetParent(parent, true);
            return l;
        }

        static GameObject MakeSwitch(Vector3 pos, Machine machine)
        {
            var post = Box("SwitchPost", new Vector3(pos.x, pos.y / 2f - 0.02f, pos.z), new Vector3(0.06f, pos.y - 0.04f, 0.06f), new Color(0.3f, 0.3f, 0.34f));
            _ = post;
            var go = Root("Switch", pos);
            if (Model(go.transform, "switch", 0.22f, -1, Pivot.Bottom, default, new Vector3(0f, -0.02f, 0f)) == null)
            {
                Vis(go.transform, "Base", PrimitiveType.Cube, Vector3.zero, new Vector3(0.14f, 0.04f, 0.14f), DarkMetal);
                Vis(go.transform, "Button", PrimitiveType.Cylinder, new Vector3(0, 0.035f, 0), new Vector3(0.09f, 0.02f, 0.09f), new Color(0.85f, 0.1f, 0.1f));
                BoxCol(go, new Vector3(0, 0.03f, 0), new Vector3(0.14f, 0.1f, 0.14f));
            }
            else
            {
                // The collider has to match the model drawn on it, not the placeholder box size.
                var hb = HolderBounds(go.transform);
                BoxCol(go, hb.center, hb.size);
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<MachineSwitch>().Bind(machine);
            SmallLabel("START / STOP", go.transform, new Vector3(0, 0.14f, 0));
            return go;
        }

        static GameObject MakeBoulder(Vector3 pos)
        {
            var go = Root("Rock", pos);
            Color rock = new Color(0.45f, 0.42f, 0.4f);
            if (Model(go.transform, "ore", 0.72f, 1, Pivot.Center, new Vector3(0, 180, 0)) == null)
            {
                Vis(go.transform, "Main", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.62f, 0.56f, 0.58f), rock);
                Vis(go.transform, "Lump", PrimitiveType.Sphere, new Vector3(0.18f, 0.2f, -0.1f), new Vector3(0.34f, 0.3f, 0.32f), new Color(0.5f, 0.47f, 0.44f));
                Vis(go.transform, "Seam", PrimitiveType.Sphere, new Vector3(0, 0.02f, -0.26f), new Vector3(0.3f, 0.22f, 0.1f), new Color(0.82f, 0.62f, 0.15f));
            }
            BoxCol(go, Vector3.zero, new Vector3(0.6f, 0.62f, 0.58f));
            return go;
        }

        static GameObject MakeBerry(Vector3 pos)
        {
            var go = Root("Berry", pos);
            Vis(go.transform, "Fruit", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.05f, new Color(0.7f, 0.1f, 0.3f));
            Vis(go.transform, "Leaf", PrimitiveType.Sphere, new Vector3(0, 0.025f, 0), new Vector3(0.03f, 0.008f, 0.02f), new Color(0.2f, 0.55f, 0.2f));
            SphereCol(go, Vector3.zero, 0.028f);
            Dyn(go, 0.05f);
            SetFloat(go.AddComponent<Edible>(), "nutrition", 8f);
            go.AddComponent<Smashable>();
            return go;
        }

        static GameObject MakeBush(Vector3 pos)
        {
            var go = Root("BerryBush", pos);
            Color leaf = new Color(0.15f, 0.45f, 0.15f);
            Vis(go.transform, "A", PrimitiveType.Sphere, new Vector3(0, 0.28f, 0), Vector3.one * 0.6f, leaf);
            Vis(go.transform, "B", PrimitiveType.Sphere, new Vector3(0.22f, 0.2f, 0.08f), Vector3.one * 0.42f, new Color(0.2f, 0.5f, 0.18f));
            Vis(go.transform, "C", PrimitiveType.Sphere, new Vector3(-0.2f, 0.22f, -0.05f), Vector3.one * 0.4f, new Color(0.12f, 0.4f, 0.14f));
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                Vis(go.transform, "Berry" + i, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * 0.26f, 0.28f + 0.06f * (i % 2), Mathf.Sin(a) * 0.26f), Vector3.one * 0.05f, new Color(0.7f, 0.1f, 0.3f));
            }
            BoxCol(go, new Vector3(0, 0.28f, 0), new Vector3(0.6f, 0.56f, 0.6f));
            return go;
        }

        static GameObject MakeSpoon(Vector3 pos)
        {
            var go = Root("ShinySpoon", pos);
            Vis(go.transform, "Handle", PrimitiveType.Cylinder, new Vector3(0, 0, -0.03f), new Vector3(0.012f, 0.05f, 0.012f), Steel, new Vector3(90, 0, 0));
            Vis(go.transform, "Bowl", PrimitiveType.Sphere, new Vector3(0, 0, 0.04f), new Vector3(0.04f, 0.012f, 0.05f), new Color(0.9f, 0.9f, 0.95f));
            CapCol(go, new Vector3(0, 0, 0), 0.02f, 0.14f, 2);
            Dyn(go, 0.05f);
            return go;
        }

        static string PrefabDir()
        {
            Directory.CreateDirectory("Assets/Makeshift/Prefabs");
            return "Assets/Makeshift/Prefabs/";
        }
    }
}
