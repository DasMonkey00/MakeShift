using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// A poke button for the style board. Any hard enough knock picks the style (or flips lean/full).
    /// Lights up while its style is the current one.
    /// </summary>
    public class RenderStyleButton : MonoBehaviour
    {
        [SerializeField] private RenderStyleSwitcher switcher;
        [SerializeField] private RenderStyle style;
        [Tooltip("This button flips lean/full effects instead of picking a style.")]
        [SerializeField] private bool togglesEffects;
        [SerializeField] private float minImpulse = 0.4f;
        [SerializeField] private float cooldown = 0.5f;
        [SerializeField] private Color idle = new Color(0.25f, 0.25f, 0.3f);
        [SerializeField] private Color active = new Color(0.9f, 0.75f, 0.2f);

        private float nextAllowed;
        private MaterialPropertyBlock block;
        private Renderer rend;

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (switcher != null) switcher.Changed += OnChanged;
        }

        private void OnDisable()
        {
            if (switcher != null) switcher.Changed -= OnChanged;
        }

        private void Start()
        {
            if (switcher != null) OnChanged(switcher.Current, switcher.FullEffects);
        }

        public void Press()
        {
            if (switcher == null || Time.time < nextAllowed) return;
            nextAllowed = Time.time + cooldown;
            if (togglesEffects) switcher.ToggleFullEffects(); else switcher.Select(style);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.impulse.magnitude >= minImpulse) Press();
        }

        private void OnChanged(RenderStyle current, bool full)
        {
            if (rend == null) return;
            bool lit = togglesEffects ? full : current == style;
            Color c = lit ? active : idle;
            rend.GetPropertyBlock(block);
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            rend.SetPropertyBlock(block);
        }
    }
}
