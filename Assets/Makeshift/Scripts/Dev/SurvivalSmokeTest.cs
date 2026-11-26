using System.Collections;
using Makeshift.Combat;
using Makeshift.Core;
using Makeshift.Fire;
using Makeshift.Survival;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>Survival checks: fire, warmth, hunger, cooking, eating, day/night raids.</summary>
    public class SurvivalSmokeTest : SmokeSuite
    {
        public Campfire campfire;
        public DayNightCycle cycle;
        public NightRaids raids;

        private GameObject Blob(string name, Vector3 pos, float size = 0.15f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<Rigidbody>();
            return go;
        }

        private Survivor MakeSurvivor(Vector3 pos, bool withHealth = false)
        {
            var go = new GameObject("TestSurvivor");
            go.transform.position = pos;
            if (withHealth) go.AddComponent<Damageable>();
            return go.AddComponent<Survivor>();
        }

        public override IEnumerator Run()
        {
            Vector3 fire = campfire.transform.position;
            Check(!campfire.IsLit, "campfire starts unlit");

            // 1. Fuel, then light.
            var log = Blob("TestLog", fire + Vector3.up * 0.3f);
            log.AddComponent<TraitHolder>().Add(Trait.Burns);
            yield return new WaitForSeconds(0.4f);
            Check(log != null && campfire.Fuel > 0f, "a Burns object dropped in feeds the fire and stays there");
            Check(!campfire.IsLit, "fuel alone does not light it");

            var torch = Blob("TestTorch", fire + Vector3.up * 0.3f + Vector3.right * 0.05f);
            var flam = torch.AddComponent<Flammable>(); // no Burns trait: it lights the fire without being eaten
            flam.Ignite();
            yield return new WaitForSeconds(0.4f);
            Check(campfire.IsLit, "touching a burning object lights the fire");
            Destroy(torch);
            campfire.AddFuel(120f);

            // 2. Warmth and hunger.
            Time.timeScale = 4f;
            var near = MakeSurvivor(fire + new Vector3(2f, 0f, 0f));
            var far = MakeSurvivor(fire + new Vector3(30f, 0f, 0f));
            near.SetNeeds(100f, 20f);
            far.SetNeeds(100f, 20f);
            yield return WaitGameSeconds(5f);
            Time.timeScale = 1f;
            Check(near.Warmth > 30f, $"standing by the fire warms you ({near.Warmth:0})");
            Check(far.Warmth < 20f, $"away from the fire you get colder ({far.Warmth:0})");
            Check(far.Hunger < 100f, $"hunger drains over time ({far.Hunger:0.0})");

            // 3. Cooking: raw -> cooked -> burnt.
            // Dropped inside the stone ring (the pit now has a floor and walls, so things stay put).
            var meatGo = Blob("TestMeat", fire + new Vector3(0.08f, 0.25f, 0f));
            var cook = meatGo.AddComponent<Cookable>();
            var edible = meatGo.AddComponent<Edible>();
            float rawValue = edible.Nutrition;
            Time.timeScale = 4f;
            for (float t = 0f; t < 60f && cook.State == CookState.Raw; t += Time.deltaTime) yield return null;
            Check(cook.State == CookState.Cooked, $"meat by the fire cooks ({cook.HeatTaken:0} heat)");
            float cookedValue = edible.Nutrition;
            campfire.AddFuel(120f);
            for (float t = 0f; t < 90f && cook.State != CookState.Burnt; t += Time.deltaTime) yield return null;
            Time.timeScale = 1f;
            Check(cook.State == CookState.Burnt, "forgotten meat burns");
            Check(cookedValue > rawValue && edible.Nutrition < rawValue,
                $"cooked is best, burnt is worst (raw {rawValue:0.0}, cooked {cookedValue:0.0}, burnt {edible.Nutrition:0.0})");

            // 4. Eating.
            var eater = MakeSurvivor(fire + new Vector3(40f, 0f, 0f));
            eater.SetNeeds(30f, 100f);
            var apple = Blob("TestFood", fire + new Vector3(40f, 1f, 5f));
            apple.AddComponent<Edible>();
            eater.Eat(apple.GetComponent<Edible>());
            yield return null;
            Check(eater.Hunger > 50f && apple == null, $"eating restores hunger and uses up the food ({eater.Hunger:0})");

            // 5. Being starved or frozen hurts.
            var starving = MakeSurvivor(fire + new Vector3(50f, 0f, 0f), withHealth: true);
            var hp = starving.GetComponent<Damageable>();
            starving.SetNeeds(0f, 100f);
            float hpBefore = hp.Health;
            Time.timeScale = 4f;
            yield return WaitGameSeconds(4f);
            Time.timeScale = 1f;
            Check(hp.Health < hpBefore, $"starving hurts ({hpBefore:0} -> {hp.Health:0})");

            // 6. Water puts the fire out.
            campfire.Douse();
            Check(!campfire.IsLit, "dousing puts the fire out");

            // 7. Night raids.
            cycle.SetDayLength(4f);
            cycle.SetTime01(0.7f);
            bool sawRaiders = false;
            for (float t = 0f; t < 3f; t += Time.deltaTime)
            {
                if (cycle.IsNight && raids.ActiveRaiders > 0) { sawRaiders = true; break; }
                yield return null;
            }
            Check(sawRaiders, $"raiders turn up when night falls ({raids.ActiveRaiders})");
            Check(Survivor.NightFalling, "nights are colder");

            bool left = false;
            for (float t = 0f; t < 6f; t += Time.deltaTime)
            {
                if (!cycle.IsNight && raids.ActiveRaiders == 0) { left = true; break; }
                yield return null;
            }
            Check(left, "raiders leave at dawn");

            cycle.SetDayLength(240f);
            cycle.SetTime01(0.35f);
            Destroy(near.gameObject); Destroy(far.gameObject); Destroy(eater.gameObject); Destroy(starving.gameObject);
        }
    }
}
