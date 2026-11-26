using System.Collections;
using Autohand;
using Makeshift.Combat;
using Makeshift.Crafting;
using Makeshift.Movement;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>Crafting, gliding and climbing checks.</summary>
    public class CraftingSmokeTest : SmokeSuite
    {
        public Vector3 benchSpot;
        public Climbable[] climbHolds;
        public Glider umbrella;

        private Rigidbody Box(string name, Vector3 pos, Vector3 size, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = size;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            return rb;
        }

        public override IEnumerator Run()
        {
            // 1. Glue alone: a head on a stick, which gives way after a few hits.
            var weakStick = Stick("WeakStick", benchSpot + new Vector3(3f, 1.5f, 0f));
            var weakHead = Box("WeakHead", benchSpot + new Vector3(3.24f, 1.5f, 0f), Vector3.one * 0.09f, 1f);
            var weak = GlueBond.Join(weakStick, weakHead, benchSpot + new Vector3(3.2f, 1.5f, 0f));
            Check(weakHead == null || weakHead.transform.parent == weakStick.transform, "glue makes the head part of the stick");

            // 2. A swung glued weapon hurts, and it is the head that lands.
            var dummyGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dummyGo.name = "TestDummy";
            dummyGo.transform.position = benchSpot + new Vector3(3.27f, 1.5f, 0.6f);
            dummyGo.transform.localScale = new Vector3(0.08f, 0.4f, 0.08f);
            var hp = dummyGo.AddComponent<Damageable>();
            yield return new WaitForFixedUpdate();
            weakStick.useGravity = false;
            weakStick.linearVelocity = Vector3.forward * 4f;
            yield return new WaitForSeconds(0.35f);
            Check(hp.Health < hp.MaxHealth, $"a swung glued head damages what it hits ({hp.MaxHealth - hp.Health:0.0} damage)");
            Destroy(dummyGo);
            weakStick.linearVelocity = Vector3.zero;
            weakStick.angularVelocity = Vector3.zero;
            while (weak != null && weak.Hits < weak.HitsNeeded) weak.RegisterHit();
            int needed = weak != null ? weak.HitsNeeded : 0;
            yield return new WaitForSeconds(0.2f);
            Check(needed >= 2 && needed <= 3, $"glue gives way after 2 or 3 hits ({needed})");
            Check(weakStick.GetComponent<GlueBond>() == null && GameObject.Find("WeakHead") != null
                && GameObject.Find("WeakHead").GetComponent<Rigidbody>() != null, "the head comes off as a loose thing again");
            weakStick.useGravity = true;

            // 3. The vine: touching the joint does nothing until it has gone round twice; then it never breaks.
            var stick = Stick("TestStick", benchSpot + new Vector3(0f, 1.2f, 0f));
            stick.isKinematic = true;
            var stone = Box("TestStone", benchSpot + new Vector3(0.24f, 1.2f, 0f), Vector3.one * 0.09f, 1f);
            var bond = GlueBond.Join(stick, stone, benchSpot + new Vector3(0.2f, 1.2f, 0f));
            Vector3 joint = bond.JointWorld;

            var vine = Box("TestVine", joint + Vector3.up * 0.08f, Vector3.one * 0.05f, 0.05f);
            vine.isKinematic = true;
            var binder = vine.gameObject.AddComponent<StickyBinder>();
            SetPrivate(binder, "requireHeld", false); // no hands in the headless test
            SetPrivate(binder, "consumeOnLash", false);
            yield return new WaitForSeconds(0.2f);
            Check(binder.Target == bond, "the end of a vine sticks to a glued joint");

            float turned = 0f, halfway = 0f;
            while (turned < 900f && !bond.Lashed)
            {
                turned += 6f;
                float a = turned * Mathf.Deg2Rad;
                vine.position = joint + new Vector3(-0.03f, Mathf.Cos(a), Mathf.Sin(a)) * 0.18f;
                vine.transform.position = vine.position;
                yield return new WaitForFixedUpdate();
                yield return null;
                if (turned >= 360f && halfway == 0f) halfway = bond.Lashed ? -1f : binder.Progress01;
            }
            Check(halfway > 0.35f && halfway < 0.65f, $"one wrap is only half way ({halfway:0.00})");
            Check(bond.Lashed && turned >= 700f && turned <= 800f, $"two wraps snap it into place (after {turned:0} degrees)");
            Check(stick.transform.Find("Lashing") != null, "the finished lashing stays on the weapon");
            for (int k = 0; k < 6; k++) bond.RegisterHit();
            yield return new WaitForSeconds(0.2f);
            Check(bond != null && stone != null || stick.transform.childCount >= 2, "a vined weapon does not break");
            Destroy(vine.gameObject);

            // Glue keeps working: two sticks dipped one after the other, one of them taking a machine part.
            var tub = FindAnyObjectByType<GlueTub>();
            for (int n = 1; n <= 2 && tub != null; n++)
            {
                var s = Stick("GlueStick" + n, tub.transform.position + new Vector3(0f, 0.01f, 0f));
                s.gameObject.AddComponent<Gluable>();
                s.useGravity = false;
                yield return new WaitForSeconds(0.2f);
                var glue = s.GetComponent<Glue>();
                Check(glue != null && glue.Wet, $"weapon {n}: dipping a stick coats it");
                s.position = s.transform.position = benchSpot + new Vector3(-3f * n, 2f, 2f);
                s.linearVelocity = Vector3.zero;
                s.angularVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();
                var head = Box("GlueHead" + n, s.position + new Vector3(0.24f, 0f, 0f), Vector3.one * 0.09f, 0.5f);
                head.useGravity = false;
                if (n == 2) head.gameObject.AddComponent<Makeshift.Machines.MachinePart>();
                head.linearVelocity = Vector3.left * 0.4f;
                yield return new WaitForSeconds(0.5f);
                Check(s.GetComponent<GlueBond>() != null && (head == null || head.transform.parent == s.transform),
                    $"weapon {n}: the wet end takes a head{(n == 2 ? " (a machine part)" : "")}");
                Destroy(s.gameObject);
            }

            // 4. Glider: falls slower than a plain body, keeps its forward motion.
            Vector3 top = benchSpot + new Vector3(-6f, 12f, 6f);
            var plain = Box("FallPlain", top, Vector3.one * 0.3f, 1f);
            var gliding = Box("FallGlide", top + Vector3.right * 3f, Vector3.one * 0.3f, 1f);
            var g = gliding.gameObject.AddComponent<Glider>();
            g.SetOpen(true);
            gliding.linearVelocity = Vector3.forward * 3f;
            yield return new WaitForSeconds(1.2f);
            Check(plain.linearVelocity.y < -8f, $"a plain body is in free fall ({plain.linearVelocity.y:0.0} m/s)");
            Check(gliding.linearVelocity.y > -3f, $"an open glider caps the fall ({gliding.linearVelocity.y:0.0} m/s)");
            Check(gliding.linearVelocity.z > 2f, $"and keeps drifting forward ({gliding.linearVelocity.z:0.0} m/s)");
            g.SetOpen(false);
            yield return new WaitForSeconds(0.5f);
            Check(gliding.linearVelocity.y < -4f, "a closed glider does nothing");

            Destroy(plain.gameObject); Destroy(gliding.gameObject);

            // 5. Playground sanity: umbrella exists, the climbing wall has holds.
            Check(umbrella != null, "playground has a glider umbrella");
            Check(climbHolds != null && climbHolds.Length >= 12, $"climbing wall has holds ({(climbHolds == null ? 0 : climbHolds.Length)})");
            bool holdsOk = true;
            if (climbHolds != null)
                foreach (var h in climbHolds)
                    if (h == null || h.GetComponent<Grabbable>() == null || h.GetComponent<Rigidbody>() == null) holdsOk = false;
            Check(holdsOk, "every climbing hold is a grabbable");
        }

        private static Rigidbody Stick(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 0;
            cap.radius = 0.022f;
            cap.height = 0.44f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            go.AddComponent<ImpactDamage>();
            return rb;
        }

        private static void SetPrivate(Object target, string field, bool value)
        {
            var f = target.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f?.SetValue(target, value);
        }

        private static void SetPrivate(Object target, string field, float value)
        {
            var f = target.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f?.SetValue(target, value);
        }
    }
}
