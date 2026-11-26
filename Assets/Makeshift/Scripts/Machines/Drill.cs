using UnityEngine;

namespace Makeshift.Machines
{
    /// <summary>
    /// The first Phase 1 machine: a power drill on a frame. While its Machine runs,
    /// it digs whatever OreNode is in front of the bit. Point the bit (this transform's
    /// forward) at the rock.
    /// </summary>
    [RequireComponent(typeof(Machine))]
    public class Drill : MonoBehaviour
    {
        [SerializeField] private Transform bitTip;
        [SerializeField] private float reach = 0.4f;
        [Tooltip("Progress per second at full output.")]
        [SerializeField] private float digRate = 2f;
        [Tooltip("Spins at output speed. Optional.")]
        [SerializeField] private Transform spinningBit;
        [SerializeField] private float maxSpinDegreesPerSecond = 1440f;

        private Machine machine;

        private float nextDrillSound;

        private void Awake()
        {
            machine = GetComponent<Machine>();
            if (bitTip == null) bitTip = transform;
        }

        private void Update()
        {
            float output = machine.Output01;

            if (spinningBit != null && output > 0f)
                spinningBit.Rotate(Vector3.forward, maxSpinDegreesPerSecond * output * Time.deltaTime, Space.Self);

            if (output <= 0f) return;
            if (!Physics.Raycast(bitTip.position, bitTip.forward, out var hit, reach,
                    ~0, QueryTriggerInteraction.Ignore)) return;

            var node = hit.collider.GetComponentInParent<OreNode>();
            if (node != null)
            {
                node.Dig(digRate * output * Time.deltaTime, hit.point, hit.normal);
                if (Time.time >= nextDrillSound)
                {
                    nextDrillSound = Time.time + 0.5f;
                    Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Drill, bitTip.position, 0.5f);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            var t = bitTip != null ? bitTip : transform;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(t.position, t.position + t.forward * reach);
        }
    }
}
