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
            if (result != null) DrawRemoved(result);
        }

        internal static void DrawRemoved(ZeroWeightBoneAnalysis.Result result) {
            var names = result.all.Where(t => t != null).Select(t => t.name).ToList();
            EditorGUILayout.HelpBox($"{names.Count} bones removed at build:\n" + string.Join("\n", names.Take(60))
                + (names.Count > 60 ? $"\n... +{names.Count - 60} more" : ""), MessageType.Info);
        }
    }

    [CustomEditor(typeof(AutoPhysBoneColliders))]
    internal class AutoPhysBoneCollidersEditor : Editor {
        private AutoPhysBoneColliders component; // kept so its collider list is still readable once the component is gone

        private void OnEnable() => component = (AutoPhysBoneColliders)target;

        // the inspector closing with a destroyed target means the component (or its object) was removed: its colliders go too.
        // Not in play mode, where build tools strip the component from the avatar and the colliders have to stay.
        private void OnDestroy() {
            if (target != null || ReferenceEquals(component, null) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var colliders = component.colliders;
            EditorApplication.delayCall += () => AutoPhysBoneCollidersGenerator.Remove(colliders);
        }

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            if (GUILayout.Button("Regenerate colliders")) AutoPhysBoneCollidersGenerator.Generate((AutoPhysBoneColliders)target);
        }
    }

    [CustomEditor(typeof(AutoQuestCopy))]
    internal class AutoQuestCopyEditor : Editor {
        private AutoQuestCopy component; // kept so its copy is still known once the component is gone
        private Transform owner;

        private void OnEnable() {
            component = (AutoQuestCopy)target;
            owner = component.transform;
        }

        // like the collider creator: a destroyed target means the component (or its object) was removed, so the copy goes too.
        // Not in play mode, where build tools strip the component from the avatar.
        private void OnDestroy() {
            if (target != null || ReferenceEquals(component, null) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var copy = component.questCopy;
            var folder = component.copiesFolder;
            var o = owner;
            EditorApplication.delayCall += () => AutoQuestCopyGenerator.Remove(copy, o, folder);
        }

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("The copy takes the avatar's blueprint ID, so upload the PC version first and regenerate if it had none yet. "
                + "Then switch the SDK to Android, select the copy and upload it. Edits to the original only reach the copy when you regenerate. "
                + "If the copy is over the 10 MB download limit, the upload first halves its biggest textures until it fits.",
                MessageType.Info);
            if (GUILayout.Button("Regenerate Quest copy")) AutoQuestCopyGenerator.Generate((AutoQuestCopy)target);
        }
    }

    [CustomEditor(typeof(MeshBoneStripper))]
    internal class MeshBoneStripperEditor : Editor {
        private ZeroWeightBoneAnalysis.Result result;
        private string broken;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            var c = (MeshBoneStripper)target;
            if (GUILayout.Button("Preview bones to remove")) {
                result = MeshBoneStripperAnalysis.Analyze(c);
                broken = string.Join(", ", MeshBoneStripperAnalysis.Broken(c, result).Select(s => s.name));
            }
            if (result == null) return;
            ZeroWeightBoneRemoverEditor.DrawRemoved(result);
            if (broken != "")
                EditorGUILayout.HelpBox("These meshes are not in the list but are weighted to bones being removed. Add them or they will break:\n"
                    + broken, MessageType.Warning);
        }
    }
}
