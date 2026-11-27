using UnityEngine;

namespace Makeshift.Rendering
{
    /// <summary>
    /// Switches the extra forest's tiers on one after another (off, low, medium, high), to see how frame time grows
    /// with the number of trees. The wrist menu cycles it.
    /// </summary>
    public class ForestDensity : MonoBehaviour
    {
        private static readonly string[] Names = { "OFF", "LOW", "MED", "HIGH" };

        [SerializeField] private GameObject[] tiers;
        [Tooltip("Tiers switched on at the start.")]
        [SerializeField] private int startTiers = 3;

        /// <summary>How many tiers are on.</summary>
        public int Level { get; private set; }

        public string Label
        {
            get
            {
                int trees = 0;
                for (int i = 0; planted != null && i < Level && i < planted.Length; i++) trees += planted[i];
                return Names[Mathf.Min(Level, Names.Length - 1)] + (trees > 0 ? " +" + trees : "");
            }
        }

        private int[] planted; // trees per tier at the start; stumps join a tier later and are not counted

        // Before the first frame, so the scenery is collected as it will be drawn.
        private void Awake()
        {
            planted = new int[tiers != null ? tiers.Length : 0];
            for (int i = 0; i < planted.Length; i++)
                if (tiers[i] != null) planted[i] = tiers[i].transform.childCount;
            Show(startTiers);
        }

        public void Cycle()
        {
            Show((Level + 1) % ((tiers != null ? tiers.Length : 0) + 1));
            var looks = GetComponent<ToonLookSwitcher>();
            if (looks != null) looks.SceneryChanged();
        }

        private void Show(int level)
        {
            Level = tiers == null ? 0 : Mathf.Clamp(level, 0, tiers.Length);
            for (int i = 0; tiers != null && i < tiers.Length; i++)
                if (tiers[i] != null) tiers[i].SetActive(i < Level);
        }
    }
}
