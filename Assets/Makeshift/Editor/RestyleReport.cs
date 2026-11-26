using System.IO;
using System.Text;
using PaleFernStudio.Looksmith.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Makeshift.EditorTools
{
    public static class RestyleReport
    {
        // Writes D:\DEV\MakeShift\Logs\restyle_report.txt (also logged to the Console).
        [MenuItem("Makeshift/Report unrestyled materials")]
        public static void Print()
        {
            if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/Playground.unity");
            var plan = LooksmithRestyle.Plan(RestyleScope.Scene);
            var sb = new StringBuilder();
            sb.AppendLine($"scene={EditorSceneManager.GetActiveScene().name} items={plan.items.Count} convertible={plan.ConvertibleCount}");
            foreach (var i in plan.items)
                sb.AppendLine($"{i.material?.name} | shader={i.sourceShader} | group={i.group} | convertible={(i.spec != null)} | path={i.assetPath} | reason={i.reason}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/restyle_report.txt", sb.ToString());
            Debug.Log("[RESTYLE] " + sb);
        }
    }
}
