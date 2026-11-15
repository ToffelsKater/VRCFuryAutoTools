using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    [CustomEditor(typeof(AutoOutfitToggles))]
    internal class AutoOutfitTogglesEditor : Editor {
        private static readonly string[] ActionLabels = { "Turn On", "Turn Off", "BlendShape", "Animation Clip", "Set an FX Float" };

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            var c = (AutoOutfitToggles)target;
            var plan = AutoOutfitTogglesPlan.Plan(c);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Toggles created at build", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(plan.Count == 0
                ? "No toggleable meshes found."
                : "Added actions run while the piece's toggle is on and go back to the resting state when it is off.", MessageType.None);

            serializedObject.Update();
            var pieces = serializedObject.FindProperty(nameof(AutoOutfitToggles.pieceActions));
            foreach (var (renderer, path) in plan) {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(path);
                if (GUILayout.Button("Add Action", GUILayout.Width(80))) ShowAddMenu(c, renderer);
                EditorGUILayout.EndHorizontal();
                for (var p = 0; p < pieces.arraySize; p++) {
                    var piece = pieces.GetArrayElementAtIndex(p);
                    if (piece.FindPropertyRelative("piece").objectReferenceValue == renderer) DrawActions(c, piece.FindPropertyRelative("actions"));
                }
                EditorGUILayout.EndVertical();
            }

            // actions whose piece was deleted, replaced or excluded: keep them visible so they can be moved to another mesh
            for (var p = 0; p < pieces.arraySize; p++) {
                var piece = pieces.GetArrayElementAtIndex(p);
                var actions = piece.FindPropertyRelative("actions");
                var mesh = piece.FindPropertyRelative("piece");
                if (actions.arraySize == 0 || plan.Any(x => x.renderer == mesh.objectReferenceValue)) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.HelpBox("These actions belong to a piece that has no toggle (deleted, replaced or excluded). "
                    + "Assign the mesh they should run with, or remove them.", MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(mesh, new GUIContent("Piece"));
                var remove = GUILayout.Button("Remove", GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
                DrawActions(c, actions);
                EditorGUILayout.EndVertical();
                if (!remove) continue;
                pieces.DeleteArrayElementAtIndex(p);
                break;
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawActions(AutoOutfitToggles c, SerializedProperty actions) {
            for (var i = 0; i < actions.arraySize; i++) {
                if (!DrawAction(c, actions.GetArrayElementAtIndex(i))) continue;
                actions.DeleteArrayElementAtIndex(i);
                break;
            }
        }

        private static Transform AvatarRoot(AutoOutfitToggles c) {
            var descriptor = c.GetComponentInParent<VRC_AvatarDescriptor>(true);
            return descriptor != null ? descriptor.transform : null;
        }

        /// <summary>Blendshape names of one mesh, or of every mesh on the avatar (the outfit alone when it is not on an avatar).</summary>
        // ponytail: rescans the meshes on every repaint that shows a BlendShape action; cache per mesh if the inspector gets slow
        private static IEnumerable<string> Shapes(AutoOutfitToggles c, Renderer only) {
            var meshes = only != null
                ? new[] { only as SkinnedMeshRenderer }
                : (AvatarRoot(c) ?? c.transform).GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return meshes.Where(m => m != null && m.sharedMesh != null)
                .SelectMany(m => Enumerable.Range(0, m.sharedMesh.blendShapeCount).Select(m.sharedMesh.GetBlendShapeName));
        }

        private static void ShowAddMenu(AutoOutfitToggles c, Renderer renderer) {
            var menu = new GenericMenu();
            for (var t = 0; t < ActionLabels.Length; t++) {
                var type = (OutfitActionType)t;
                menu.AddItem(new GUIContent(ActionLabels[t]), false, () => {
                    Undo.RecordObject(c, "Add outfit action");
                    var piece = c.pieceActions.Find(p => p.piece == renderer);
                    if (piece == null) c.pieceActions.Add(piece = new OutfitPieceActions { piece = renderer });
                    piece.actions.Add(new OutfitAction { type = type, value = type == OutfitActionType.BlendShape ? 100 : 1 });
                    EditorUtility.SetDirty(c);
                });
            }
            menu.ShowAsContext();
        }

        /// <summary>Draws one action row. True when its remove button was pressed.</summary>
        private bool DrawAction(AutoOutfitToggles c, SerializedProperty a) {
            var type = (OutfitActionType)a.FindPropertyRelative("type").enumValueIndex;
            void Field(string name, string tooltip, params GUILayoutOption[] options) =>
                EditorGUILayout.PropertyField(a.FindPropertyRelative(name), new GUIContent("", tooltip), options);
            string warning = null;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(ActionLabels[(int)type], GUILayout.Width(100));
            switch (type) {
                case OutfitActionType.TurnOn:
                case OutfitActionType.TurnOff:
                    Field("obj", "Object to turn " + (type == OutfitActionType.TurnOn ? "on" : "off"));
                    break;
                case OutfitActionType.BlendShape:
                    var shape = a.FindPropertyRelative("name");
                    var only = a.FindPropertyRelative("renderer").objectReferenceValue as Renderer;
                    Field("name", "Blendshape name");
                    if (EditorGUILayout.DropdownButton(new GUIContent("Pick", "Choose from the blendshapes that exist"), FocusType.Passive, GUILayout.Width(45))) {
                        var menu = new GenericMenu();
                        var path = shape.propertyPath;
                        foreach (var s in Shapes(c, only).Distinct()) {
                            menu.AddItem(new GUIContent(s), s == shape.stringValue, () => {
                                serializedObject.Update();
                                serializedObject.FindProperty(path).stringValue = s;
                                serializedObject.ApplyModifiedProperties();
                            });
                        }
                        if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("No blendshapes found"));
                        menu.ShowAsContext();
                    }
                    Field("value", "Blendshape value, 0-100", GUILayout.Width(45));
                    Field("renderer", "Mesh to change. None = every mesh on the avatar that has this blendshape.", GUILayout.Width(110));
                    // off an avatar the body's blendshapes are out of reach, so only a chosen mesh can be checked there
                    if (shape.stringValue != "" && (only != null || AvatarRoot(c) != null) && !Shapes(c, only).Contains(shape.stringValue))
                        warning = $"No blendshape named \"{shape.stringValue}\" on " + (only != null ? only.name : "this avatar") + ". This action will do nothing.";
                    break;
                case OutfitActionType.AnimationClip:
                    Field("clip", "Clip played while the toggle is on. Paths are relative to the avatar root.");
                    break;
                case OutfitActionType.FxFloat:
                    Field("name", "FX parameter name");
                    Field("value", "Value while the toggle is on", GUILayout.Width(45));
                    break;
            }
            var remove = GUILayout.Button("x", GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();
            if (warning != null) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            return remove;
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
