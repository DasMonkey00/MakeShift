using Makeshift.Fire;
using UnityEngine;

namespace Makeshift.Survival
{
    public enum CookState { Raw, Cooked, Burnt }

    /// <summary>
    /// Food that cooks near a lit HeatSource: raw, then cooked, then burnt if forgotten.
    /// No recipes, just heat over time. Tints its renderer so you can see the state.
    /// </summary>
    public class Cookable : MonoBehaviour
    {
        [SerializeField] private float cookedAt = 20f;
        [SerializeField] private float burntAt = 50f;
        [SerializeField] private Color rawColor = new Color(0.75f, 0.2f, 0.25f);
        [SerializeField] private Color cookedColor = new Color(0.5f, 0.3f, 0.15f);
        [SerializeField] private Color burntColor = new Color(0.1f, 0.08f, 0.07f);

        public CookState State { get; private set; } = CookState.Raw;
        public float HeatTaken { get; private set; }

        /// <summary>Multiplies the food's nutrition: raw is poor, cooked is best, burnt is nearly useless.</summary>
        public float NutritionMultiplier
        {
            get
            {
                switch (State)
                {
                    case CookState.Cooked: return 1.5f;
                    case CookState.Burnt: return 0.1f;
                    default: return 0.3f;
                }
            }
        }

        private Renderer rend;
        private float nextSizzle;

        private void Awake()
        {
            rend = GetComponentInChildren<Renderer>();
            Tint();
        }

        private void Update()
        {
            float heat = HeatSource.HeatAt(transform.position);
            if (heat <= 0f || State == CookState.Burnt) return;

            if (Time.time >= nextSizzle)
            {
                nextSizzle = Time.time + 0.8f;
                Makeshift.Audio.PlaceholderSfx.Play(Makeshift.Audio.SfxKind.Sizzle, transform.position, 0.45f);
            }
            HeatTaken += heat * Time.deltaTime;
            var next = HeatTaken >= burntAt ? CookState.Burnt : HeatTaken >= cookedAt ? CookState.Cooked : CookState.Raw;
            if (next != State)
            {
                State = next;
                // One loud burst when it is done; a duller hiss when it burns.
                var kind = next == CookState.Cooked ? Makeshift.Audio.SfxKind.Done : Makeshift.Audio.SfxKind.Cook;
                Makeshift.Audio.PlaceholderSfx.Play(kind, transform.position, next == CookState.Cooked ? 1f : 0.6f);
                Tint();
            }
        }

        private void Tint()
        {
            if (rend == null) return;
            rend.material.color = State == CookState.Raw ? rawColor : State == CookState.Cooked ? cookedColor : burntColor;
        }
    }
}
