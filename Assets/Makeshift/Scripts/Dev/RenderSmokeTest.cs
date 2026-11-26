using System.Collections;
using Makeshift.Rendering;
using PaleFernStudio.Looksmith;
using UnityEngine;
using UnityEngine.Rendering;

namespace Makeshift.Dev
{
    /// <summary>
    /// Render style switching: None really turns Looksmith off, each style turns it on, and lean mode
    /// strips the full-screen costs (lines, finish) that would hurt on Quest.
    /// </summary>
    public class RenderSmokeTest : SmokeSuite
    {
        public override IEnumerator Run()
        {
            var switcher = FindAnyObjectByType<RenderStyleSwitcher>();
            Check(switcher != null, "render style switcher exists");
            if (switcher == null) yield break;
            var volume = FindAnyObjectByType<Volume>();

            switcher.SetFullEffects(false);
            switcher.Select(RenderStyle.None);
            yield return null;
            Check(LooksmithRuntime.ActiveVolumes.Count == 0, "None: no Looksmith volume registered, so no extra passes");

            foreach (RenderStyle style in new[] { RenderStyle.StylizedRealism, RenderStyle.CelShading, RenderStyle.Foggy })
            {
                switcher.SetFullEffects(false);
                switcher.Select(style);
                yield return null;
                Check(LooksmithRuntime.ActiveVolumes.Count == 1, $"{style}: Looksmith volume registered");
                Check(volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out LooksmithLook lean), $"{style}: profile has a Look");
                if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out LooksmithLook l))
                {
                    var lines = LooksmithLookState.LinesOf(l);
                    Check(!lines.enabled && l.grain.value < 0.001f && l.vignette.value < 0.001f,
                        $"{style} (lean): no lines, grain or vignette");
                    Check(volume.sharedProfile.components.Count == 1, $"{style} (lean): no post-processing overrides ({volume.sharedProfile.components.Count} components)");
                }
            }

            // Full mode keeps the authored Look: cel shading is the one with lines.
            switcher.SetFullEffects(true);
            switcher.Select(RenderStyle.CelShading);
            yield return null;
            bool hasLines = false;
            if (volume != null && volume.sharedProfile != null && volume.sharedProfile.TryGet(out LooksmithLook cel))
                hasLines = LooksmithLookState.LinesOf(cel).enabled;
            Check(hasLines, "Cel shading (full): lines are on as authored");

            // Poke buttons work.
            var buttons = FindObjectsByType<RenderStyleButton>(FindObjectsInactive.Exclude);
            Check(buttons.Length == 5, $"style board has 5 buttons ({buttons.Length})");
            switcher.SetFullEffects(false);
            switcher.Select(RenderStyle.None);
            foreach (var b in buttons)
            {
                if (b.name == "StyleButton3") { b.Press(); break; }
            }
            yield return null;
            Check(switcher.Current == RenderStyle.Foggy, "poking the Foggy button selects Foggy");

            switcher.Select(RenderStyle.None);
            switcher.SetFullEffects(Application.platform != RuntimePlatform.Android);
        }
    }
}
