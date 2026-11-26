using System.Collections;
using Makeshift.Combat;
using Makeshift.Creatures;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>Sprint 2 checks: thrown-object damage, scavenger steals and drops loot, harvesting.</summary>
    public class Sprint2SmokeTest : SmokeSuite
    {
        public ScavengerCreature creature;
        public Damageable creatureHealth;
        public Scavengable spoon;
        public Harvestable bush;

        private Rigidbody Brick(Vector3 from, Vector3 to, float speed, float mass = 1f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = Vector3.one * 0.15f;
            go.transform.position = from;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            go.AddComponent<ImpactDamage>();
            rb.linearVelocity = (to - from).normalized * speed;
            Destroy(go, 5f);
            return rb;
        }

        public override IEnumerator Run()
        {
            // 1. Creature spots the loose spoon and runs off with it.
            // Earlier suites leave it with loot in its mouth and the spoon freshly stashed: start clean, spoon only.
            creature.DropLoot();
            foreach (var other in Scavengable.All) other.IgnoredUntil = other == spoon ? 0f : float.MaxValue;
            // Wherever earlier suites left it, put the spoon where the creature can see it.
            Vector3 near = creature.transform.position + creature.transform.forward * 1.2f;
            spoon.Body.position = spoon.transform.position = new Vector3(near.x, 0.1f, near.z);
            spoon.Body.linearVelocity = Vector3.zero;
            Time.timeScale = 3f;
            for (float t = 0f; t < 30f && !creature.HasLoot; t += Time.deltaTime) yield return null;
            Time.timeScale = 1f;
            Check(creature.HasLoot && creature.Loot == spoon, "scavenger steals the loose item");
            Check(creature.Mode == ScavengerMode.Flee, "scavenger flees with loot");
            yield return new WaitForSeconds(0.35f);
            Vector3 mouthAt = creature.Mouth.position;
            float off = Vector3.Distance(spoon.transform.position, mouthAt);
            Check(creature.HasLoot && off < 0.2f, $"the loot stays in its mouth while it runs ({off:0.00} m)");

            foreach (var other in Scavengable.All) if (other != spoon) other.IgnoredUntil = 0f;

            // 2. A hard throw knocks it loose and stuns it.
            float healthBefore = creatureHealth.Health;
            Vector3 aim = creature.transform.position + Vector3.up * 0.2f;
            Brick(aim + creature.transform.right * 0.6f, aim, 8f);
            yield return new WaitForSeconds(0.5f);
            Check(creatureHealth.Health < healthBefore, $"thrown brick hurts ({healthBefore - creatureHealth.Health:0.0} damage)");
            Check(!creature.HasLoot, "hit drops the loot");
            Check(creature.Mode == ScavengerMode.Stunned, "hit stuns the creature");
            Check(spoon.Holder == null && !spoon.Body.isKinematic, "dropped loot is a free object again");

            // 3. A gentle tap does nothing.
            float h2 = creatureHealth.Health;
            aim = creature.transform.position + Vector3.up * 0.2f;
            Brick(aim + creature.transform.right * 0.6f, aim, 0.3f, 0.2f);
            yield return new WaitForSeconds(0.6f);
            Check(Mathf.Approximately(creatureHealth.Health, h2), "a gentle tap does no damage");

            // 4. Harvesting: keep hitting the bush until it is picked clean.
            int guard = 0;
            while (bush != null && !bush.IsDepleted && guard++ < 20)
            {
                Vector3 p = bush.transform.position + Vector3.up * 0.1f;
                Brick(p + Vector3.back * 0.8f, p, 7f);
                yield return new WaitForSeconds(0.5f);
            }
            Check(guard < 20, "bush gets harvested by thrown objects");

            // 5. Keep throwing at the creature until it dies.
            guard = 0;
            while (!creatureHealth.IsDead && guard++ < 20)
            {
                aim = creature.transform.position + Vector3.up * 0.2f;
                Brick(aim + Vector3.right * 0.6f, aim, 9f, 2f);
                yield return new WaitForSeconds(0.6f);
            }
            Check(creatureHealth.IsDead && creature.Mode == ScavengerMode.Dead, "creature can be knocked out");
        }
    }
}
