using System;
using System.Collections.Generic;
using Autohand;
using Makeshift.Fire;
using Makeshift.Rendering;
using Makeshift.Survival;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Makeshift.UI
{
    /// <summary>
    /// The playground test menu on your left wrist.
    ///   1. Look at the back of your left wrist: a round button appears.
    ///   2. Touch it with the index finger of your right hand: the menu opens in front of you and stays put.
    ///   3. Poke buttons with your right index finger; drag along the time bar to move the sun.
    /// Everything is poked by fingertip distance (no physics colliders), so the menu never shoves anything.
    /// Walk away from it and it closes. On a keyboard, M opens and closes it.
    ///
    /// Offsets are in the left hand's local axes (robot hand: palm normal +X on the left hand, fingers +Z).
    /// If the button sits in the wrong place on the wrist, change them on this component.
    /// </summary>
    public class WristMenu : MonoBehaviour
    {
        [Header("Rig")]
        [SerializeField] private AutoHandPlayer player;
        [SerializeField] private Hand leftHand;
        [SerializeField] private Hand rightHand;
        [SerializeField] private Camera head;

        [Header("What the menu controls")]
        [SerializeField] private DayNightCycle cycle;
        [SerializeField] private TimeOfDaySky timeOfDay;
        [SerializeField] private Survivor survivor;
        [SerializeField] private NightRaids raids;
        [SerializeField] private Campfire campfire;
        [SerializeField] private Light sun;
        [Tooltip("Teleport targets. The menu button is labelled with the object's name without the TP_ prefix.")]
        [SerializeField] private Transform[] teleportPoints;

        [Header("Look")]
        [SerializeField] private Material surfaceMaterial;

        [Header("Wrist placement (left hand local axes)")]
        [SerializeField] private Vector3 buttonLocalOffset = new Vector3(-0.045f, 0f, -0.03f);
        [Tooltip("Direction out of the back of the left hand, in hand-local axes.")]
        [SerializeField] private Vector3 backLocalNormal = new Vector3(-1f, 0f, 0f);
        [SerializeField] private float lookAtWristDot = 0.5f;
        [SerializeField] private float buttonPokeDistance = 0.035f;

        [Header("Panel placement (in front of the player, fixed while open)")]
        [SerializeField] private float openDistance = 0.68f;
        [Tooltip("How far below the eyes the panel's middle sits.")]
        [SerializeField] private float openDrop = 0.08f;
        [SerializeField] private float openScale = 1.2f;
        [Tooltip("The menu closes if the player gets this far from it.")]
        [SerializeField] private float closeDistance = 1.5f;

        private static readonly Color Idle = new Color(0.16f, 0.18f, 0.22f);
        private static readonly Color Hover = new Color(0.3f, 0.34f, 0.42f);
        private static readonly Color Active = new Color(0.85f, 0.65f, 0.15f);
        private static readonly Color Good = new Color(0.2f, 0.55f, 0.3f);

        private class Control
        {
            public Transform root;
            public Vector2 half;
            public bool slider;
            public Action press;
            public Action<float> drag;
            public Func<string> text;
            public Func<float> value;
            public Func<bool> lit;
            public TextMesh label;
            public Renderer bg;
            public Transform handle;
            public bool inside;
            public float ready;
        }

        private readonly List<Control> controls = new List<Control>();
        private GameObject buttonRoot;
        private Renderer buttonRend;
        private GameObject panelRoot;
        private TextMesh header;
        private MaterialPropertyBlock block;
        private Font font;
        private Transform fingerTip;
        private Transform fingerOwner;
        private bool open;
        private bool buttonArmed = true;
        private int replaceFrames;
        private float nextButtonPress;
        private float fpsTimer, fpsFrames;
        private float fps;
        private bool slowMo;
        private bool shadowsOn = true;
        private bool clockPaused;

        private void Start()
        {
            block = new MaterialPropertyBlock();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (head == null) head = Camera.main;
            if (player == null) player = GetComponentInChildren<AutoHandPlayer>(true);
            if (leftHand == null && player != null) leftHand = player.handLeft;
            if (rightHand == null && player != null) rightHand = player.handRight;
            if (sun == null) sun = RenderSettings.sun;
            if (campfire == null) campfire = FindAnyObjectByType<Campfire>();
            BuildButton();
            BuildPanel();
            SetOpen(false);
        }

        private void OnDestroy()
        {
            if (buttonRoot != null) Destroy(buttonRoot);
            if (panelRoot != null) Destroy(panelRoot);
            if (slowMo) { Time.timeScale = 1f; }
        }

        // ---------- building ----------

        private void BuildButton()
        {
            buttonRoot = new GameObject("WristMenuButton");
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(buttonRoot.transform, false);
            disc.transform.localScale = new Vector3(0.05f, 0.004f, 0.05f);
            buttonRend = disc.GetComponent<Renderer>();
            if (surfaceMaterial != null) buttonRend.sharedMaterial = surfaceMaterial;
            buttonRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(buttonRend, Good);
            var dot = MakeText(buttonRoot.transform, "MENU", new Vector3(0f, 0.005f, 0f), Quaternion.Euler(90f, 0f, 0f), 0.0016f, Color.white);
            dot.anchor = TextAnchor.MiddleCenter;
        }

        private void BuildPanel()
        {
            panelRoot = new GameObject("WristMenuPanel");
            // A solid face the hand stops against, so a finger presses a button instead of passing through it.
            // The Furniture layer stops hands but not the player's body.
            var wall = panelRoot.AddComponent<BoxCollider>();
            wall.center = new Vector3(0f, -0.045f, 0.012f);
            wall.size = new Vector3(0.37f, 0.51f, 0.03f);   // front face just ahead of the button faces
            int solid = LayerMask.NameToLayer("Furniture");
            if (solid >= 0) panelRoot.layer = solid;

            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(panelRoot.transform, false);
            back.transform.localScale = new Vector3(0.36f, 0.5f, 0.01f);
            back.transform.localPosition = new Vector3(0f, -0.045f, 0.012f);
            var br = back.GetComponent<Renderer>();
            if (surfaceMaterial != null) br.sharedMaterial = surfaceMaterial;
            br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(br, new Color(0.06f, 0.07f, 0.09f));

            header = MakeText(panelRoot.transform, "TEST MENU", new Vector3(0f, 0.185f, -0.002f), Quaternion.identity, 0.0028f, Color.white);

            float w = 0.34f;
            float half = 0.083f;
            float h = 0.034f;

            AddButton("Labels", new Vector2(-half, 0.145f), new Vector2(half, h / 2f), 0.0026f,
                () => FloatingLabel.Toggle(),
                () => "Labels: " + (FloatingLabel.Visible ? "ON" : "OFF"),
                () => FloatingLabel.Visible);
            AddButton("Clock", new Vector2(half, 0.145f), new Vector2(half, h / 2f), 0.0026f,
                () => { clockPaused = !clockPaused; if (cycle != null) cycle.Paused = clockPaused; },
                () => "Clock: " + (clockPaused ? "PAUSED" : "running"),
                () => !clockPaused);

            AddSlider(new Vector2(0f, 0.1f), new Vector2(w / 2f - 0.01f, h / 2f + 0.002f),
                () => cycle != null ? cycle.Time01 : 0f,
                v => SetHour(v * 24f),
                () => cycle != null ? "TIME  " + HourText(cycle.Time01 * 24f) + (cycle.IsNight ? "  (night)" : "") : "TIME");

            AddButton("Refill", new Vector2(-half, 0.055f), new Vector2(half, h / 2f), 0.0026f,
                () => { if (survivor != null) survivor.Refill(); }, () => "Refill food+warmth", null);
            AddButton("Needs", new Vector2(half, 0.055f), new Vector2(half, h / 2f), 0.0026f,
                () => { if (survivor != null) survivor.NeedsFrozen = !survivor.NeedsFrozen; },
                () => "Needs: " + (survivor != null && survivor.NeedsFrozen ? "FROZEN" : "draining"),
                () => survivor != null && survivor.NeedsFrozen);

            AddButton("Fire", new Vector2(-half, 0.01f), new Vector2(half, h / 2f), 0.0026f,
                () => { if (campfire != null) { campfire.AddFuel(120f); campfire.Light(); } },
                () => "Fuel + light fire", null);
            AddButton("Douse", new Vector2(half, 0.01f), new Vector2(half, h / 2f), 0.0026f,
                () => { if (campfire != null) campfire.Douse(); }, () => "Put fire out", null);

            AddButton("Raid", new Vector2(-half, -0.035f), new Vector2(half, h / 2f), 0.0026f,
                () => { if (raids != null) raids.SpawnNow(); }, () => "Raiders arrive", null);
            AddButton("Leave", new Vector2(half, -0.035f), new Vector2(half, h / 2f), 0.0026f,
                () => { if (raids != null) raids.LeaveNow(); }, () => "Raiders leave", null);

            AddButton("SlowMo", new Vector2(-half, -0.08f), new Vector2(half, h / 2f), 0.0026f,
                () => { slowMo = !slowMo; Time.timeScale = slowMo ? 0.25f : 1f; },
                () => "Slow-mo: " + (slowMo ? "ON" : "off"), () => slowMo);
            AddButton("Shadows", new Vector2(half, -0.08f), new Vector2(half, h / 2f), 0.0026f,
                () =>
                {
                    shadowsOn = !shadowsOn;
                    if (sun != null) sun.shadows = shadowsOn ? LightShadows.Soft : LightShadows.None;
                },
                () => "Shadows: " + (shadowsOn ? "ON" : "OFF"), () => shadowsOn);

            // Teleports
            int n = teleportPoints != null ? teleportPoints.Length : 0;
            if (n > 0)
            {
                float bw = (w - 0.02f - (n - 1) * 0.006f) / n;
                for (int i = 0; i < n; i++)
                {
                    var tp = teleportPoints[i];
                    if (tp == null) continue;
                    float x = -w / 2f + 0.01f + bw / 2f + i * (bw + 0.006f);
                    string name = tp.name.StartsWith("TP_") ? tp.name.Substring(3) : tp.name;
                    var target = tp;
                    AddButton(name, new Vector2(x, -0.125f), new Vector2(bw / 2f, h / 2f), 0.0021f,
                        () => TeleportTo(target), () => name, null);
                }
            }

            AddButton("Reload", new Vector2(-half, -0.17f), new Vector2(half, h / 2f), 0.0026f,
                () => { Time.timeScale = 1f; SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); },
                () => "Reload playground", null);
            AddButton("Close", new Vector2(half, -0.17f), new Vector2(half, h / 2f), 0.0026f,
                () => SetOpen(false), () => "Close menu", null);
        }

        private Control AddButton(string id, Vector2 pos, Vector2 half, float charSize, Action press, Func<string> text, Func<bool> lit)
        {
            var c = NewControl(id, pos, half);
            c.press = press;
            c.text = text;
            c.lit = lit;
            c.label = MakeText(c.root, text(), new Vector3(0f, 0f, -0.002f), Quaternion.identity, charSize, Color.white);
            c.label.anchor = TextAnchor.MiddleCenter;
            controls.Add(c);
            return c;
        }

        private Control AddSlider(Vector2 pos, Vector2 half, Func<float> value, Action<float> drag, Func<string> text)
        {
            var c = NewControl("TimeSlider", pos, half);
            c.slider = true;
            c.value = value;
            c.drag = drag;
            c.text = text;
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(handle.GetComponent<Collider>());
            handle.transform.SetParent(c.root, false);
            handle.transform.localScale = new Vector3(0.012f, half.y * 2f + 0.01f, 0.01f);
            handle.transform.localPosition = new Vector3(0f, 0f, -0.006f);
            var hr = handle.GetComponent<Renderer>();
            if (surfaceMaterial != null) hr.sharedMaterial = surfaceMaterial;
            hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(hr, Active);
            c.handle = handle.transform;
            c.label = MakeText(c.root, text(), new Vector3(0f, 0f, -0.012f), Quaternion.identity, 0.0026f, Color.white);
            c.label.anchor = TextAnchor.MiddleCenter;
            controls.Add(c);
            return c;
        }

        private Control NewControl(string id, Vector2 pos, Vector2 half)
        {
            var go = new GameObject(id);
            go.transform.SetParent(panelRoot.transform, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cube.GetComponent<Collider>());
            cube.transform.SetParent(go.transform, false);
            cube.transform.localScale = new Vector3(half.x * 2f, half.y * 2f, 0.008f);
            cube.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            var r = cube.GetComponent<Renderer>();
            if (surfaceMaterial != null) r.sharedMaterial = surfaceMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SetColor(r, Idle);
            return new Control { root = go.transform, half = half, bg = r };
        }

        private TextMesh MakeText(Transform parent, string text, Vector3 localPos, Quaternion localRot, float charSize, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 60;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return tm;
        }

        // ---------- behaviour ----------

        private void SetOpen(bool on)
        {
            open = on;
            if (panelRoot != null) panelRoot.SetActive(on);
            if (on) PlacePanel();
            foreach (var c in controls) c.inside = true; // no accidental press the instant it opens
        }

        private void SetHour(float hour)
        {
            if (cycle == null) return;
            if (timeOfDay != null) timeOfDay.SetHour(hour);
            else cycle.SetTime01(hour / 24f);
        }

        private void TeleportTo(Transform target)
        {
            if (player == null || target == null) return;
            player.SetPosition(target.position, target.rotation);
            replaceFrames = 3; // bring the menu along once the head has arrived
        }

        private static string HourText(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            int hh = Mathf.FloorToInt(hour);
            int mm = Mathf.FloorToInt((hour - hh) * 60f);
            return $"{hh:00}:{mm:00}";
        }

        private Transform FingerTip()
        {
            if (rightHand == null) return null;
            if (fingerOwner == rightHand.transform && fingerTip != null) return fingerTip;
            fingerOwner = rightHand.transform;
            fingerTip = null;
            if (rightHand.fingers != null)
            {
                foreach (var f in rightHand.fingers)
                {
                    if (f != null && f.tip != null && f.name.ToLowerInvariant().Contains("index")) { fingerTip = f.tip; break; }
                }
                if (fingerTip == null && rightHand.fingers.Length > 1 && rightHand.fingers[1] != null) fingerTip = rightHand.fingers[1].tip;
            }
            if (fingerTip == null) fingerTip = rightHand.palmTransform != null ? rightHand.palmTransform : rightHand.transform;
            return fingerTip;
        }

        private void Update()
        {
            if (head == null) return;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) SetOpen(!open);
#endif
            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fps = fpsFrames / fpsTimer;
                fpsFrames = 0f;
                fpsTimer = 0f;
                if (header != null) header.text = $"TEST MENU    {fps:0} fps";
            }

            var tipT = FingerTip();
            Vector3 tip = tipT != null ? tipT.position : new Vector3(0f, -999f, 0f);

            UpdateWristButton(tip);
            if (open) UpdateControls(tip);
        }

        private void UpdateWristButton(Vector3 tip)
        {
            if (buttonRoot == null) return;
            if (leftHand == null)
            {
                buttonRoot.SetActive(false);
                return;
            }
            Vector3 backWorld = leftHand.transform.TransformDirection(backLocalNormal).normalized;
            Vector3 pos = leftHand.transform.TransformPoint(buttonLocalOffset);
            bool lookingAtWrist = Vector3.Dot(backWorld, (head.transform.position - pos).normalized) > lookAtWristDot;
            buttonRoot.SetActive(lookingAtWrist || open);
            buttonRoot.transform.position = pos;
            buttonRoot.transform.rotation = Quaternion.FromToRotation(Vector3.up, backWorld);

            if (!buttonRoot.activeSelf) return;
            bool near = (tip - pos).magnitude < buttonPokeDistance;
            SetColor(buttonRend, near ? Active : Good);
            if ((tip - pos).magnitude > buttonPokeDistance * 1.8f) buttonArmed = true;
            if (near && buttonArmed && Time.unscaledTime >= nextButtonPress)
            {
                buttonArmed = false;
                nextButtonPress = Time.unscaledTime + 0.6f;
                SetOpen(!open);
            }
        }

        /// <summary>Sets the panel in front of wherever the player is looking, level with the chest, and leaves it there.</summary>
        private void PlacePanel()
        {
            if (panelRoot == null || head == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(head.transform.up, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 pos = head.transform.position + forward.normalized * openDistance + Vector3.down * openDrop;
            panelRoot.transform.localScale = Vector3.one * openScale;
            panelRoot.transform.position = pos;
            // Faces the eyes, so the whole panel tilts up a little towards them.
            panelRoot.transform.rotation = Quaternion.LookRotation((pos - head.transform.position).normalized, Vector3.up);
        }

        private void LateUpdate()
        {
            if (!open || panelRoot == null || head == null) return;
            if (replaceFrames > 0)
            {
                if (--replaceFrames == 0) PlacePanel();
                return;
            }
            if (Vector3.Distance(head.transform.position, panelRoot.transform.position) > closeDistance) SetOpen(false);
        }

        private void UpdateControls(Vector3 tip)
        {
            foreach (var c in controls)
            {
                Vector3 local = c.root.InverseTransformPoint(tip);
                bool over = Mathf.Abs(local.x) <= c.half.x && Mathf.Abs(local.y) <= c.half.y;
                bool hover = over && local.z > -0.06f && local.z < 0.03f;
                // A finger that has pressed must come well back out before it can press again, so a shaky hand
                // does not double-click; going a bit through the panel still counts as touching.
                bool touching = over && local.z > (c.inside ? -0.055f : -0.022f) && local.z < 0.2f;

                if (c.slider)
                {
                    if (touching)
                    {
                        float v = Mathf.InverseLerp(-c.half.x + 0.01f, c.half.x - 0.01f, local.x);
                        c.drag?.Invoke(Mathf.Clamp01(v));
                    }
                    float value = c.value != null ? c.value() : 0f;
                    c.handle.localPosition = new Vector3(Mathf.Lerp(-c.half.x + 0.01f, c.half.x - 0.01f, value), 0f, -0.006f);
                    SetColor(c.bg, hover ? Hover : Idle);
                }
                else
                {
                    if (touching && !c.inside && Time.unscaledTime >= c.ready)
                    {
                        c.ready = Time.unscaledTime + 0.5f;
                        c.press?.Invoke();
                    }
                    bool lit = c.lit != null && c.lit();
                    SetColor(c.bg, touching ? Active : hover ? Hover : lit ? Good : Idle);
                }
                c.inside = touching;

                if (c.text != null && c.label != null)
                {
                    string s = c.text();
                    if (c.label.text != s) c.label.text = s;
                }
            }
        }

        private void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }
    }
}
