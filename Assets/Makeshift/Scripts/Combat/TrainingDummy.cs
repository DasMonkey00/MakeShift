using UnityEngine;

namespace Makeshift.Combat
{
    /// <summary>
    /// A practice post fixed to the ground: walking into it does nothing, hitting it rocks the straw body away from
    /// the blow and it springs back. Only the visual moves; the collider stays put.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class TrainingDummy : MonoBehaviour
    {
        [Tooltip("Pivot at the foot of the post, holding everything that rocks.")]
        [SerializeField] private Transform visual;
        [SerializeField] private float degreesPerDamage = 3f;
        [SerializeField] private float maxDegrees = 30f;
        [SerializeField] private float stiffness = 90f;
        [SerializeField] private float damping = 5f;

        private Damageable damageable;
        private Quaternion rest = Quaternion.identity;
        private Vector3 axis = Vector3.right;
        private float angle;
        private float speed;

        private void Awake()
        {
            damageable = GetComponent<Damageable>();
            if (visual != null) rest = visual.localRotation;
        }

        private void OnEnable() { damageable.onHit.AddListener(OnHit); }
        private void OnDisable() { damageable.onHit.RemoveListener(OnHit); }

        private void OnHit(float amount)
        {
            Vector3 away = damageable.LastHitDirection;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = transform.forward;
            if (visual == null) return;
            Transform space = visual.parent != null ? visual.parent : visual;
            axis = space.InverseTransformDirection(Vector3.Cross(Vector3.up, away.normalized));
            // Enough speed to swing out to roughly the wanted angle.
            speed += Mathf.Min(maxDegrees, amount * degreesPerDamage) * Mathf.Sqrt(stiffness);
        }

        private void Update()
        {
            if (visual == null || (angle == 0f && speed == 0f)) return;
            float dt = Mathf.Min(Time.deltaTime, 0.03f);
            speed += (-stiffness * angle - damping * speed) * dt;
            angle = Mathf.Clamp(angle + speed * dt, -maxDegrees, maxDegrees);
            if (Mathf.Abs(angle) < 0.05f && Mathf.Abs(speed) < 0.5f) { angle = 0f; speed = 0f; }
            visual.localRotation = Quaternion.AngleAxis(angle, axis) * rest;
        }
    }
}
