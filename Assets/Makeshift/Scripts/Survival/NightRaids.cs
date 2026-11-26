using System.Collections.Generic;
using Makeshift.Creatures;
using UnityEngine;

namespace Makeshift.Survival
{
    /// <summary>
    /// When night falls, scavengers turn up around the camp and go after loose loot.
    /// At dawn they leave (dropping whatever they carry).
    /// </summary>
    public class NightRaids : MonoBehaviour
    {
        [SerializeField] private DayNightCycle cycle;
        [SerializeField] private ScavengerCreature raiderPrefab;
        [SerializeField] private Transform center;
        [SerializeField] private int raiders = 3;
        [SerializeField] private float ringRadius = 5f;

        private readonly List<ScavengerCreature> active = new List<ScavengerCreature>();

        public int ActiveRaiders
        {
            get
            {
                active.RemoveAll(r => r == null);
                return active.Count;
            }
        }

        private void Awake()
        {
            if (cycle == null) return;
            cycle.onNightFalls.AddListener(Spawn);
            cycle.onDawn.AddListener(Leave);
        }

        /// <summary>Raiders turn up now (the wrist menu uses this to test a raid without waiting for night).</summary>
        public void SpawnNow() => Spawn();

        /// <summary>Raiders leave now, dropping what they carry.</summary>
        public void LeaveNow() => Leave();

        private void Spawn()
        {
            if (raiderPrefab == null) return;
            Vector3 c = center != null ? center.position : transform.position;
            for (int i = 0; i < raiders; i++)
            {
                float a = (i + Random.value * 0.5f) / raiders * Mathf.PI * 2f;
                Vector3 pos = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ringRadius;
                pos.y = raiderPrefab.transform.position.y;
                active.Add(Instantiate(raiderPrefab, pos, Quaternion.identity));
            }
        }

        private void Leave()
        {
            foreach (var r in active)
            {
                if (r == null) continue;
                r.DropLoot();
                Destroy(r.gameObject);
            }
            active.Clear();
        }
    }
}
