using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRCFuryAutoTools {
    [CustomEditor(typeof(AutoOutfitToggles))]
    internal class AutoOutfitTogglesEditor : Editor {
        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            var plan = AutoOutfitTogglesPlan.Plan((AutoOutfitToggles)target);
            EditorGUILayout.HelpBox(plan.Count == 0
                ? "No toggleable meshes found."
                : "Toggles created at build:\n" + string.Join("\n", plan.Select(p => p.path)), MessageType.Info);
        }
    }

    [CustomEditor(typeof(ZeroWeightBoneRemover))]
    internal class ZeroWeightBoneRemoverEditor : Editor {
        private ZeroWeightBoneAnalysis.Result result;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            var c = (ZeroWeightBoneRemover)target;
            if (GUILayout.Button("Preview bones to remove")) result = ZeroWeightBoneAnalysis.Analyze(c);
            if (result == null) return;
            var names = result.all.Where(t => t != null).Select(t => t.name).ToList();
            EditorGUILayout.HelpBox($"{names.Count} bones removed at build:\n" + string.Join("\n", names.Take(60))
                + (names.Count > 60 ? $"\n... +{names.Count - 60} more" : ""), MessageType.Info);
        }
    }
}
