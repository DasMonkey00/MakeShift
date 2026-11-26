using UnityEngine;

namespace Makeshift.Power
{
    /// <summary>
    /// Turns rotation into charge: a hand crank, a bike wheel, a hamster wheel with a boar in it.
    /// Put it on the rotating part (usually on a HingeJoint the hands can grab).
    /// Measures how far it turned around its local axis each frame and charges the target source.
    /// </summary>
    public class CrankGenerator : MonoBehaviour
    {
        [SerializeField] private PowerSource target;
        [SerializeField] private Vector3 localAxis = Vector3.right;
        [SerializeField] private float chargePerRevolution = 2f;
        [Tooltip("Cranking backwards does nothing, unless this is on.")]
        [SerializeField] private bool bothDirections = false;

        private Quaternion lastRotation;

        public float RevolutionsPerSecond { get; private set; }

        private void OnEnable()
        {
            lastRotation = transform.localRotation;
        }

        private void Update()
        {
            Quaternion delta = Quaternion.Inverse(lastRotation) * transform.localRotation;
            lastRotation = transform.localRotation;

            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;

            // Signed rotation around our crank axis this frame.
            float signed = angle * Vector3.Dot(axis.normalized, localAxis.normalized);
            if (!bothDirections && signed < 0f) signed = 0f;

            float revolutions = Mathf.Abs(signed) / 360f;
            RevolutionsPerSecond = Time.deltaTime > 0f ? revolutions / Time.deltaTime : 0f;

            if (target != null && revolutions > 0f)
                target.AddCharge(revolutions * chargePerRevolution);
        }
    }
}
