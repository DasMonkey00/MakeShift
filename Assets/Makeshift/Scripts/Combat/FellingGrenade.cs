using System;
using System.Collections;
using System.Collections.Generic;
using Autohand;
using Makeshift.Audio;
using Makeshift.Core;
using Makeshift.Nature;
using UnityEngine;

namespace Makeshift.Combat
{
    /// <summary>
    /// A hand grenade, on the Auto Hand grenade's pin (the pin carries the joint that breaks when it is pulled). The pin
    /// can only be pulled while the grenade is in the other hand. Pulling it starts the fuse; when it runs out every pine
    /// within the blast comes down and loose things close by are thrown. A <see cref="GrenadeRack"/> puts a new one out.
    /// </summary>
    public class FellingGrenade : MonoBehaviour
    {
        [SerializeField] private Grabbable grenade;
        [SerializeField] private Grabbable pin;
        [SerializeField] private ConfigurableJoint pinJoint;
        [Tooltip("Force needed to pull the pin out.")]
        [SerializeField] private float pullStrength = 750f;
        [Tooltip("Seconds from the pin coming out to the blast.")]
        [SerializeField] private float fuse = 5f;
        [Tooltip("Trees whose trunk is this near, in metres, come down.")]
        [SerializeField] private float treeRadius = 15f;
        [Tooltip("Loose things this near are thrown.")]
        [SerializeField] private float throwRadius = 6f;
        [SerializeField] private float throwForce = 250f;
        [SerializeField] private AudioClip boom;
        [SerializeField] private GameObject blastEffect;

        private const float Held = 100000f; // the pin stays put until a hand pulls it

        /// <summary>Raised as it blows up, just before it is destroyed.</summary>
        public event Action<FellingGrenade> Exploded;

        public bool Armed { get; private set; }
        private bool gone;
        private Transform holder;

        private void Awake() => holder = grenade != null ? grenade.transform.parent : null;

        private void OnEnable()
        {
            pin.isGrabbable = false;
            grenade.OnGrabEvent += OnGrenadeGrab;
            grenade.OnReleaseEvent += OnGrenadeRelease;
            pin.OnGrabEvent += OnPinGrab;
            pin.OnReleaseEvent += OnPinRelease;
            if (!grenade.jointedBodies.Contains(pin.body)) grenade.jointedBodies.Add(pin.body);
            if (!pin.jointedBodies.Contains(grenade.body)) pin.jointedBodies.Add(grenade.body);
        }

        private void OnDisable()
        {
            grenade.OnGrabEvent -= OnGrenadeGrab;
            grenade.OnReleaseEvent -= OnGrenadeRelease;
            pin.OnGrabEvent -= OnPinGrab;
            pin.OnReleaseEvent -= OnPinRelease;
        }

        private void OnGrenadeGrab(Hand hand, Grabbable g)
        {
            if (!Armed) pin.isGrabbable = true;
        }

        private void OnGrenadeRelease(Hand hand, Grabbable g)
        {
            if (!Armed) pin.isGrabbable = false;
        }

        private void OnPinGrab(Hand hand, Grabbable g)
        {
            if (pinJoint != null) pinJoint.breakForce = pullStrength;
        }

        private void OnPinRelease(Hand hand, Grabbable g)
        {
            if (pinJoint != null) pinJoint.breakForce = Held;
        }

        private void OnJointBreak(float force) => StartCoroutine(Pulled());

        private IEnumerator Pulled()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            if (pinJoint == null) Arm();
        }

        /// <summary>The one way a fuse starts.</summary>
        public void Arm()
        {
            if (Armed) return;
            Armed = true;
            if (pinJoint != null) Destroy(pinJoint);
            pin.isGrabbable = true;
            pin.maintainGrabOffset = false;
            pin.RemoveJointedBody(grenade.body);
            grenade.RemoveJointedBody(pin.body);
            StartCoroutine(Burn());
        }

        // Ticks once a second, then fast in the last one.
        private IEnumerator Burn()
        {
            float tick = 0f;
            for (float left = fuse; left > 0f; left -= Time.deltaTime)
            {
                tick -= Time.deltaTime;
                if (tick <= 0f)
                {
                    PlaceholderSfx.Play(SfxKind.Click, grenade.transform.position, 1f, 6f);
                    tick = left > 1.2f ? 1f : 0.18f;
                }
                yield return null;
            }
            Explode();
        }

        /// <summary>The one way it blows up.</summary>
        public void Explode()
        {
            if (gone) return;
            gone = true;
            Vector3 at = grenade.transform.position;
            if (grenade.GetHeldBy().Count > 0) grenade.ForceHandsRelease();
            if (pin != null && pin.GetHeldBy().Count > 0) pin.ForceHandsRelease();

            if (TreeCutting.Instance != null) TreeCutting.Instance.Blast(at, treeRadius);
            Throw(at);
            if (blastEffect != null) Instantiate(blastEffect, at, Quaternion.identity);
            Boom(at);

            Exploded?.Invoke(this);
            // The pin may have been carried off by a hand, so each part is destroyed by name.
            if (grenade != null) Destroy(grenade.gameObject);
            if (holder != null) Destroy(holder.gameObject);
            Destroy(gameObject);
        }

        // Loud from anywhere near the blast: the clip, with a slowed copy under it for weight.
        private void Boom(Vector3 at)
        {
            if (boom == null) return;
            var sound = new GameObject("GrenadeBoom");
            sound.transform.position = at;
            foreach (float pitch in new[] { 1f, 0.5f })
            {
                var source = sound.AddComponent<AudioSource>();
                source.clip = boom;
                source.pitch = pitch;
                source.volume = 1f;
                source.priority = 0;
                source.spatialBlend = 0.6f;   // still comes from its direction
                source.minDistance = 20f;     // full volume anywhere inside the blast
                source.maxDistance = 250f;
                source.Play();
            }
            Destroy(sound, boom.length / 0.5f + 0.5f);
        }

        private void Throw(Vector3 at)
        {
            var thrown = new HashSet<Rigidbody>();
            foreach (var hit in Physics.OverlapSphere(at, throwRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var body = hit.attachedRigidbody;
                if (body == null || body.isKinematic || body == grenade.body || body == pin.body) continue;
                if (HandCheck.PlayerBody(body) || !thrown.Add(body)) continue;
                body.AddExplosionForce(throwForce, at, throwRadius, 0.5f);
            }
        }
    }
}
