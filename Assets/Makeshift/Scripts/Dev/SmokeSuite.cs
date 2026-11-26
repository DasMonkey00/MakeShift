using System.Collections;
using UnityEngine;

namespace Makeshift.Dev
{
    /// <summary>Base for automated play-mode checks. GreyboxSmokeTest runs every suite in the scene.</summary>
    public abstract class SmokeSuite : MonoBehaviour
    {
        public int Failures { get; private set; }

        protected void Check(bool ok, string what)
        {
            if (!ok) Failures++;
            Debug.Log($"[SMOKE] {(ok ? "PASS" : "FAIL")}: {what}");
        }

        public abstract IEnumerator Run();

        protected static IEnumerator WaitGameSeconds(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }
    }
}
