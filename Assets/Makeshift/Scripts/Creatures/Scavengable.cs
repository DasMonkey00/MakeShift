using System.Collections.Generic;
using UnityEngine;

namespace Makeshift.Creatures
{
    /// <summary>Marks a loose item that scavenger creatures want (shiny, edible, small).</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Scavengable : MonoBehaviour
    {
        public static readonly List<Scavengable> All = new List<Scavengable>();

        public Rigidbody Body { get; private set; }

        /// <summary>The creature carrying this, if any.</summary>
        public ScavengerCreature Holder { get; set; }
        /// <summary>Just stashed at a den: left alone until this time.</summary>
        public float IgnoredUntil { get; set; }

        private void Awake() { Body = GetComponent<Rigidbody>(); }
        private void OnEnable() { All.Add(this); }
        private void OnDisable() { All.Remove(this); }
    }
}
