using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    [CustomEditor(typeof(AutoOutfitToggles))]
    internal class AutoOutfitTogglesEditor : Editor {
        private static readonly string[] ActionLabels = { "Turn On", "Turn Off", "BlendShape", "Animation Clip", "Set an FX Float", "Material Swap", "Material Property", "Scale" };

        /// <summary>Searchable pick list: big shaders have thousands of properties, too many for a plain menu.</summary>
        private class PickDropdown : AdvancedDropdown {
            private readonly string title;
            private readonly string[] items;
            private readonly Action<int> picked;

            public PickDropdown(string title, string[] items, Action<int> picked) : base(new AdvancedDropdownState()) {
                this.title = title;
                this.items = items;
                this.picked = picked;
                minimumSize = new Vector2(280, 320);
            }

            protected override AdvancedDropdownItem BuildRoot() {
                var root = new AdvancedDropdownItem(title);
                for (var i = 0; i < items.Length; i++) root.AddChild(new AdvancedDropdownItem(items[i]) { id = i });
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item) => picked(item.id);
        }

        /// <summary>"Pick" button that opens the list under itself.</summary>
        private static void PickButton(string tooltip, string title, Func<string[]> items, Action<int> picked) {
            var clicked = EditorGUILayout.DropdownButton(new GUIContent("Pick", tooltip), FocusType.Passive, GUILayout.Width(45));
            if (clicked) new PickDropdown(title, items(), picked).Show(GUILayoutUtility.GetLastRect());
        }

        /// <summary>Materials of one mesh, or of every mesh on the avatar (the outfit alone when it is not on an avatar).</summary>
        private static IEnumerable<Material> Materials(AutoOutfitToggles c, Renderer only) =>
            (only != null ? new[] { only } : (AvatarRoot(c) ?? c.transform).GetComponentsInChildren<Renderer>(true))
                .SelectMany(r => r.sharedMaterials).Where(m => m != null);

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
                    piece.actions.Add(new OutfitAction {
                        type = type,
                        value = type == OutfitActionType.BlendShape ? 100 : 1,
                        // most material changes recolor the piece itself
                        renderer = type == OutfitActionType.MaterialSwap || type == OutfitActionType.MaterialProperty ? renderer : null,
                    });
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
                    var shapePath = shape.propertyPath;
                    string[] shapes = null;
                    PickButton("Choose from the blendshapes that exist", "Blendshapes", () => shapes = Shapes(c, only).Distinct().ToArray(), i => {
                        serializedObject.Update();
                        serializedObject.FindProperty(shapePath).stringValue = shapes[i];
                        serializedObject.ApplyModifiedProperties();
                    });
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
                case OutfitActionType.MaterialSwap:
                    Field("renderer", "Mesh whose material is swapped");
                    var swapped = a.FindPropertyRelative("renderer").objectReferenceValue as Renderer;
                    var slot = a.FindPropertyRelative("materialIndex");
                    var slots = swapped == null ? new[] { "Slot 0" } : swapped.sharedMaterials
                        .Select((m, i) => $"{i}: {(m != null ? m.name.Replace('/', '-') : "None")}").ToArray();
                    EditorGUI.BeginChangeCheck();
                    var picked = EditorGUILayout.Popup(slot.intValue, slots, GUILayout.Width(100));
                    if (EditorGUI.EndChangeCheck()) slot.intValue = picked;
                    Field("material", "Material shown in that slot while the toggle is on");
                    if (swapped != null && slot.intValue >= slots.Length)
                        warning = $"{swapped.name} has no material slot {slot.intValue}. This action will do nothing.";
                    break;
                case OutfitActionType.MaterialProperty:
                    var property = a.FindPropertyRelative("name");
                    var mesh = a.FindPropertyRelative("renderer").objectReferenceValue as Renderer;
                    Field("renderer", "Mesh to change. None = every mesh on the avatar with a material that has this property.", GUILayout.Width(110));
                    Field("name", "Shader property name, like _Color");
                    var actionPath = a.propertyPath;
                    var found = new List<(string name, OutfitPropertyType type)>();
                    PickButton("Choose from the properties of the materials", "Material properties", () => {
                        var labels = new List<string>();
                        foreach (var shader in Materials(c, mesh).Select(m => m.shader).Distinct()) {
                            for (var i = 0; i < shader.GetPropertyCount(); i++) {
                                var kind = shader.GetPropertyType(i);
                                var name = shader.GetPropertyName(i);
                                if (kind == ShaderPropertyType.Texture || found.Any(f => f.name == name)) continue;
                                found.Add((name, kind == ShaderPropertyType.Color ? OutfitPropertyType.Color
                                    : kind == ShaderPropertyType.Vector ? OutfitPropertyType.Vector : OutfitPropertyType.Float));
                                labels.Add($"{shader.GetPropertyDescription(i)} ({name})");
                            }
                        }
                        return labels.ToArray();
                    }, i => {
                        serializedObject.Update();
                        serializedObject.FindProperty(actionPath + ".name").stringValue = found[i].name;
                        serializedObject.FindProperty(actionPath + ".propertyType").enumValueIndex = (int)found[i].type;
                        serializedObject.ApplyModifiedProperties();
                    });
                    if (property.stringValue.Contains("."))
                        warning = "Use the property name without a channel like .r or .x. This action will do nothing.";
                    else if (property.stringValue != "" && (mesh != null || AvatarRoot(c) != null) && !Materials(c, mesh).Any(m => m.HasProperty(property.stringValue)))
                        warning = $"No material on {(mesh != null ? mesh.name : "this avatar")} has a property named \"{property.stringValue}\". This action will do nothing.";
                    break;
                case OutfitActionType.Scale:
                    Field("obj", "Object to scale");
                    EditorGUILayout.LabelField(new GUIContent("x", "Multiplier of the object's current scale"), GUILayout.Width(10));
                    Field("value", "Multiplier of the object's current scale while the toggle is on", GUILayout.Width(45));
                    break;
            }
            var remove = GUILayout.Button("x", GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();
            if (type == OutfitActionType.MaterialProperty) {
                // second row: the value, in the shape the property has
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(104);
                var kind = a.FindPropertyRelative("propertyType");
                EditorGUILayout.PropertyField(kind, GUIContent.none, GUILayout.Width(110));
                switch ((OutfitPropertyType)kind.enumValueIndex) {
                    case OutfitPropertyType.Float:
                        Field("value", "Value while the toggle is on");
                        break;
                    case OutfitPropertyType.Color:
                        var color = a.FindPropertyRelative("color");
                        EditorGUI.BeginChangeCheck();
                        var hdr = EditorGUILayout.ColorField(GUIContent.none, color.colorValue, true, true, true); // HDR for emission
                        if (EditorGUI.EndChangeCheck()) color.colorValue = hdr;
                        break;
                    case OutfitPropertyType.Vector:
                        var vector = a.FindPropertyRelative("vector");
                        EditorGUI.BeginChangeCheck();
                        var xyzw = EditorGUILayout.Vector4Field(GUIContent.none, vector.vector4Value);
                        if (EditorGUI.EndChangeCheck()) vector.vector4Value = xyzw;
                        break;
                }
                EditorGUILayout.EndHorizontal();
            }
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

    [CustomEditor(typeof(AutoDigitigradeLegs))]
    internal class AutoDigitigradeLegsEditor : Editor {
        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            var c = (AutoDigitigradeLegs)target;
            // ponytail: searches the armature again on every repaint while the leg fields are empty; cache it if the inspector gets slow
            var legs = AutoDigitigradeLegsBuild.Plan(c);
            if (legs.Count == 0) {
                EditorGUILayout.HelpBox("Put this on a humanoid avatar.", MessageType.Warning);
                return;
            }

            var ik = AutoDigitigradeLegsBuild.UsesFinalIK(c);
            if (c.mode == DigitigradeLegMode.FinalIK && !ik)
                EditorGUILayout.HelpBox(AutoDigitigradeLegsBuild.FinalIK("FABRIK") == null
                    ? "Final IK is not in this project, so rotation constraints are used instead."
                    : "VRChat only runs Final IK on PC, so this build uses rotation constraints instead.", MessageType.Warning);
            foreach (var leg in legs) {
                if (leg.error != null)
                    EditorGUILayout.HelpBox($"{leg.side} leg is skipped: {leg.error}.", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox($"{leg.side} leg, set up at build with "
                        + (ik && !leg.Humanoid ? "Final IK" + (c.rotationLimits ? " and hinge limits" : "")
                            : "rotation constraints" + (ik ? " (built like the real leg, so it copies it)" : "")) + ":\n"
                        + string.Join(" > ", leg.chain.Select(t => t.name)) + $"\nfollows {leg.realUpper.name} > {leg.realFoot.name}", MessageType.Info);
            }
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
