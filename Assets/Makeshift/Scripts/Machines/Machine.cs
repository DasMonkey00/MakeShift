using System.Collections.Generic;
using Makeshift.Core;
using Makeshift.Power;
using UnityEngine;
using UnityEngine.Events;

namespace Makeshift.Machines
{
    public enum MachineState
    {
        Off,
        Running,
        Sputtering,   // running at random, weak output for a while
        Jammed,       // stopped until someone bonks it
        Overheated,   // stopped until it cools down or gets doused
    }

    /// <summary>Requests a non-authority client can relay to the machine's authority.</summary>
    public enum MachineCommand { TurnOn, TurnOff, Douse, Bonk }

    /// <summary>
    /// The "it works, badly" framework. A machine is a set of sockets filled with parts.
    /// The worse the parts, the more often it sputters, jams, overheats or backfires.
    /// A good bonk usually fixes it. Usually.
    ///
    /// Every state change goes through SetState, so a netcode layer can later make the
    /// host the only one allowed to change it.
    /// </summary>
    public class Machine : MonoBehaviour
    {
        [Header("Parts and power")]
        [SerializeField] private List<PartSocket> sockets = new List<PartSocket>();
        [SerializeField] private PowerPort powerPort;
        [Tooltip("Power used per second at full output. 0 = needs no power (hand cranked).")]
        [SerializeField] private float powerDemandPerSecond = 2f;

        [Header("Failures (per minute, at reliability 0)")]
        [SerializeField] private float failuresPerMinute = 6f;
        [Tooltip("Even perfect parts fail this often, relative to the rate above.")]
        [SerializeField, Range(0f, 1f)] private float minimumFailureShare = 0.1f;
        [SerializeField, Range(0f, 1f)] private float sputterWeight = 0.5f;
        [SerializeField, Range(0f, 1f)] private float jamWeight = 0.35f;
        [SerializeField, Range(0f, 1f)] private float backfireWeight = 0.15f;
        [SerializeField] private Vector2 sputterSeconds = new Vector2(2f, 5f);

        [Header("Heat")]
        [SerializeField] private float heatPerSecond = 4f;
        [SerializeField] private float coolPerSecond = 6f;
        [SerializeField] private float overheatAt = 100f;
        [SerializeField] private float recoverBelow = 40f;

        [Header("Bonking")]
        [Tooltip("Collision impulse needed to count as a bonk.")]
        [SerializeField] private float bonkImpulse = 3f;
        [SerializeField, Range(0f, 1f)] private float bonkFixesJam = 0.7f;
        [SerializeField, Range(0f, 1f)] private float bonkFixesSputter = 0.6f;
        [SerializeField, Range(0f, 1f)] private float bonkCausesBackfire = 0.15f;
        [SerializeField] private float partDamagePerBonk = 0.02f;

        [Header("Backfire")]
        [SerializeField] private float backfireForce = 6f;
        [SerializeField] private float backfireRadius = 1.5f;
        [SerializeField] private bool backfireEjectsAPart = true;

        [Header("Events")]
        public UnityEvent<MachineState> onStateChanged;
        public UnityEvent onBackfire;
        public UnityEvent onBonked;
        public UnityEvent onNoPower;

        public MachineState State { get; private set; } = MachineState.Off;
        public float Heat { get; private set; }
        public bool SwitchedOn { get; private set; }

        /// <summary>0..1. What the machine is putting out this frame. Drills, pumps
        /// and lights read this.</summary>
        public float Output01 { get; private set; }

        public float Reliability
        {
            get
            {
                float total = 0f;
                int count = 0;
                foreach (var s in sockets)
                {
                    if (s == null || !s.IsFilled) continue;
                    total += s.EffectiveReliability;
                    count++;
                }
                return count > 0 ? total / count : 0f;
            }
        }

        public bool IsAssembled
        {
            get
            {
                foreach (var s in sockets)
                    if (s != null && s.Required && !s.IsFilled) return false;
                return true;
            }
        }

        private float sputterTimeLeft;
        private float sputterNoise;
        private bool hadPower = true;
        private static readonly Collider[] Hits = new Collider[24];

        // ---------- Controls (levers, buttons, cranks call these) ----------

        public void TurnOn()
        {
            if (!NetAuthority.RouteOrRun(this, (int)MachineCommand.TurnOn)) return;
            SwitchedOn = true;
            if (State == MachineState.Off && IsAssembled) SetState(MachineState.Running);
        }

        public void TurnOff()
        {
            if (!NetAuthority.RouteOrRun(this, (int)MachineCommand.TurnOff)) return;
            SwitchedOn = false;
            if (State == MachineState.Running || State == MachineState.Sputtering)
                SetState(MachineState.Off);
        }

        public void Toggle()
        {
            if (SwitchedOn) TurnOff(); else TurnOn();
        }

        /// <summary>Water: instant cool-down, and a running machine may short out.</summary>
        public void Douse()
        {
            if (!NetAuthority.RouteOrRun(this, (int)MachineCommand.Douse)) return;
            Heat = 0f;
            if (State == MachineState.Overheated) SetState(SwitchedOn ? MachineState.Running : MachineState.Off);
            else if (State == MachineState.Running && Random.value < 0.5f) StartSputter();
        }

        // ---------- Loop ----------

        private void Update()
        {
            // Only the authority simulates; everyone else mirrors via ApplyRemoteState.
            if (!NetAuthority.Has(this)) return;
            float dt = Time.deltaTime;

            switch (State)
            {
                case MachineState.Running:
                case MachineState.Sputtering:
                    TickRunning(dt);
                    break;

                case MachineState.Overheated:
                    Output01 = 0f;
                    Heat = Mathf.Max(0f, Heat - coolPerSecond * dt);
                    if (Heat <= recoverBelow)
                        SetState(SwitchedOn ? MachineState.Running : MachineState.Off);
                    break;

                default:
                    Output01 = 0f;
                    Heat = Mathf.Max(0f, Heat - coolPerSecond * dt);
                    // Switched on before the last part went in: start once it is assembled.
                    if (State == MachineState.Off && SwitchedOn && IsAssembled)
                        SetState(MachineState.Running);
                    break;
            }
        }

        private void TickRunning(float dt)
        {
            if (!IsAssembled) { SetState(MachineState.Off); return; }

            float power = 1f;
            if (powerDemandPerSecond > 0f)
            {
                power = powerPort != null ? powerPort.Request(powerDemandPerSecond, dt) : 0f;
                bool hasPower = power > 0f;
                if (!hasPower && hadPower) onNoPower?.Invoke();
                hadPower = hasPower;
            }

            float stateFactor = 1f;
            if (State == MachineState.Sputtering)
            {
                // Lurch between weak and nothing.
                sputterNoise = Mathf.Lerp(sputterNoise, Random.Range(0f, 0.7f), 10f * dt);
                stateFactor = sputterNoise;
                sputterTimeLeft -= dt;
                if (sputterTimeLeft <= 0f) SetState(MachineState.Running);
            }

            Output01 = Mathf.Clamp01(power * stateFactor);

            Heat += heatPerSecond * Output01 * dt;
            if (Heat >= overheatAt) { SetState(MachineState.Overheated); return; }

            RollForFailure(dt);
        }

        private void RollForFailure(float dt)
        {
            if (State != MachineState.Running) return;
            float badness = Mathf.Max(minimumFailureShare, 1f - Reliability);
            float chance = failuresPerMinute / 60f * badness * dt;
            if (Random.value >= chance) return;

            float total = sputterWeight + jamWeight + backfireWeight;
            float roll = Random.value * total;
            if (roll < sputterWeight) StartSputter();
            else if (roll < sputterWeight + jamWeight) SetState(MachineState.Jammed);
            else Backfire();
        }

        private void StartSputter()
        {
            sputterTimeLeft = Random.Range(sputterSeconds.x, sputterSeconds.y);
            sputterNoise = 0.5f;
            SetState(MachineState.Sputtering);
        }

        // ---------- Bonking ----------

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.impulse.magnitude >= bonkImpulse) Bonk();
        }

        /// <summary>Also callable directly, e.g. from a weapon's own hit detection.</summary>
        public void Bonk()
        {
            if (!NetAuthority.RouteOrRun(this, (int)MachineCommand.Bonk)) { onBonked?.Invoke(); return; }
            onBonked?.Invoke();
            DamageRandomPart(partDamagePerBonk);

            if (Random.value < bonkCausesBackfire && State != MachineState.Off)
            {
                Backfire();
                return;
            }

            switch (State)
            {
                case MachineState.Jammed:
                    if (Random.value < bonkFixesJam) SetState(SwitchedOn ? MachineState.Running : MachineState.Off);
                    break;
                case MachineState.Sputtering:
                    if (Random.value < bonkFixesSputter) SetState(MachineState.Running);
                    break;
                case MachineState.Off:
                    // Percussive maintenance sometimes starts things.
                    if (SwitchedOn && IsAssembled) SetState(MachineState.Running);
                    break;
            }
        }

        // ---------- Backfire ----------

        public void Backfire()
        {
            onBackfire?.Invoke();

            int count = Physics.OverlapSphereNonAlloc(transform.position, backfireRadius, Hits,
                ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var rb = Hits[i].attachedRigidbody;
                if (rb != null && !rb.isKinematic)
                    rb.AddExplosionForce(backfireForce, transform.position, backfireRadius, 0.3f, ForceMode.Impulse);
            }

            if (backfireEjectsAPart && Random.value < 0.5f)
            {
                var filled = sockets.FindAll(s => s != null && s.IsFilled);
                if (filled.Count > 0) filled[Random.Range(0, filled.Count)].Eject();
            }

            Heat += overheatAt * 0.25f;
            SetState(MachineState.Jammed);
        }

        private void DamageRandomPart(float amount)
        {
            var filled = sockets.FindAll(s => s != null && s.IsFilled);
            if (filled.Count > 0) filled[Random.Range(0, filled.Count)].Part.Damage(amount);
        }

        // ---------- State ----------

        private void SetState(MachineState next)
        {
            if (next == State) return;
            State = next;
            if (next != MachineState.Running && next != MachineState.Sputtering) Output01 = 0f;
            onStateChanged?.Invoke(next);
        }

        /// <summary>Runs a command relayed from another client. Call on the authority only.</summary>
        public void ApplyCommand(MachineCommand command)
        {
            switch (command)
            {
                case MachineCommand.TurnOn: TurnOn(); break;
                case MachineCommand.TurnOff: TurnOff(); break;
                case MachineCommand.Douse: Douse(); break;
                case MachineCommand.Bonk: Bonk(); break;
            }
        }

        /// <summary>
        /// Mirror path for non-authority clients: copies the authority's state in. This is the
        /// only way state changes without authority, and it fires the same events.
        /// </summary>
        public void ApplyRemoteState(MachineState next, float heat, bool switchedOn)
        {
            Heat = heat;
            SwitchedOn = switchedOn;
            SetState(next);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, backfireRadius);
        }
    }
}
