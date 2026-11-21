using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    /// <summary>
    /// The SDK's performance rank is taken from the avatar in the scene, before VRCFury, NDMF and the other build tools change it.
    /// This window runs the SDK's avatar build hooks on a copy, like an upload does, and ranks the result for PC and Quest.
    /// </summary>
    internal class BuildPerformanceWindow : EditorWindow {
        private VRCAvatarDescriptor avatar, questAvatar;
        private AvatarPerformanceStats pc, quest;
        private string builtPc, builtQuest, error;
        private BuildTarget builtFor;
        private Vector2 scroll;

        [MenuItem("Tools/VRCFury Auto Tools/Performance Rank After Build")]
        private static void Open() {
            var w = GetWindow<BuildPerformanceWindow>("Build Performance");
            if (w.avatar == null && Selection.activeGameObject != null)
                w.avatar = Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>();
        }

        private static readonly string[] Icons = { null, "Perf_Great_32", "Perf_Good_32", "Perf_Medium_32", "Perf_Poor_32", "Perf_Horrible_32" };

        private static GUIContent Rating(PerformanceRating r, object value) {
            var icon = (int)r < Icons.Length && Icons[(int)r] != null ? Resources.Load<Texture>("PerformanceIcons/" + Icons[(int)r]) : null;
            var text = value == null ? AvatarPerformanceStats.GetPerformanceRatingDisplayName(r) : $"{value}  ({AvatarPerformanceStats.GetPerformanceRatingDisplayName(r)})";
            return new GUIContent(" " + text, icon);
        }

        /// <summary>The stat behind the category: the field named like it (PolyCount: polyCount), by name since SDKs add categories.</summary>
        private static object Value(AvatarPerformanceStats s, AvatarPerformanceCategory c) {
            if (c == AvatarPerformanceCategory.AABB) return s.aabb?.size.ToString("F2");
            var name = c.ToString();
            object owner = s;
            if (name.StartsWith("PhysBone")) (owner, name) = (s.physBone, name.Substring("PhysBone".Length)); // componentCount, ...
            var v = owner?.GetType().GetField(char.ToLowerInvariant(name[0]) + name.Substring(1))?.GetValue(owner);
            return v is float f ? f.ToString("0.#") : v;
        }

        private static readonly MethodInfo RefreshConstraints = typeof(VRCConstraintBase).Assembly.GetType("VRC.Dynamics.VRCConstraintManager")
            ?.GetMethod("Sdk_ManuallyRefreshGroups", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// Copies the avatar, runs the SDK's avatar build hooks on the copy (VRCFury, NDMF, this package, the SDK's own EditorOnly
        /// removal), ranks it for each of <paramref name="mobile"/> and deletes the copy. Null when a hook stops the build.
        /// </summary>
        private static AvatarPerformanceStats[] Measure(GameObject original, params bool[] mobile) {
            var clone = Object.Instantiate(original);
            clone.SetActive(true);
            if (original.scene.IsValid() && clone.scene != original.scene) SceneManager.MoveGameObjectToScene(clone, original.scene);
            try {
                if (!VRCBuildPipelineCallbacks.OnPreprocessAvatar(clone)) return null;
                // like the SDK's builder: unregistered constraints give a wrong constraint depth otherwise. Internal to the SDK.
                RefreshConstraints?.Invoke(null, new object[] { clone.GetComponentsInChildren<VRCConstraintBase>(true) });
                return mobile.Select(m => {
                    var s = new AvatarPerformanceStats(m);
                    AvatarPerformance.CalculatePerformanceStats(original.name, clone, s, m);
                    return s;
                }).ToArray();
            } finally {
                VRCBuildPipelineCallbacks.OnPostprocessAvatar();
                Object.DestroyImmediate(clone);
            }
        }

        private void Run() {
            pc = quest = null;
            error = null;
            builtFor = EditorUserBuildSettings.activeBuildTarget;
            var q = questAvatar != null ? questAvatar : avatar;
            try {
                if (q == avatar) {
                    var both = Measure(avatar.gameObject, false, true);
                    if (both != null) (pc, quest) = (both[0], both[1]);
                } else {
                    pc = Measure(avatar.gameObject, false)?[0];
                    quest = Measure(q.gameObject, true)?[0];
                }
                if (pc == null || quest == null) error = "A build hook stopped the build. The console says why.";
            } catch (Exception e) {
                Debug.LogException(e);
                error = "The build failed: " + e.Message;
            }
            builtPc = avatar.name;
            builtQuest = q.name;
        }

        private void OnGUI() {
            avatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField("Avatar (PC)", avatar, typeof(VRCAvatarDescriptor), true);
            var copy = avatar != null ? avatar.GetComponent<AutoQuestCopy>() : null;
            var auto = copy != null && copy.questCopy != null ? copy.questCopy.GetComponent<VRCAvatarDescriptor>() : null;
            if (auto != null && questAvatar == null) questAvatar = auto;
            questAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                new GUIContent("Avatar (Quest)", "The avatar you upload for Android. Empty: the PC avatar"), questAvatar, typeof(VRCAvatarDescriptor), true);

            EditorGUILayout.HelpBox(
                "Copies the avatar and runs every avatar build hook on the copy (VRCFury, NDMF, Modular Avatar, this package), as an upload "
                + "does, then ranks the result. The copy is deleted afterwards.\n"
                + $"The hooks run for the current build target ({EditorUserBuildSettings.activeBuildTarget}): steps a tool only does for "
                + "the other platform are left out.", MessageType.Info);

            using (new EditorGUI.DisabledScope(avatar == null || EditorApplication.isPlayingOrWillChangePlaymode)) {
                if (GUILayout.Button("Build and Rank", GUILayout.Height(28))) Run();
            }

            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (pc == null || quest == null) return;

            EditorGUILayout.LabelField($"Built for {builtFor}. PC: {builtPc}, Quest: {builtQuest}", EditorStyles.miniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.LabelField("", EditorStyles.boldLabel, GUILayout.Width(200));
                EditorGUILayout.LabelField("PC", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Quest", EditorStyles.boldLabel);
            }
            foreach (AvatarPerformanceCategory c in Enum.GetValues(typeof(AvatarPerformanceCategory))) {
                if (c == AvatarPerformanceCategory.None || c == AvatarPerformanceCategory.AvatarPerformanceCategoryCount) continue;
                var (rp, rq) = (pc.GetPerformanceRatingForCategory(c), quest.GetPerformanceRatingForCategory(c));
                if (rp == PerformanceRating.None && rq == PerformanceRating.None) continue; // not measured without an asset bundle
                var style = c == AvatarPerformanceCategory.Overall ? EditorStyles.boldLabel : EditorStyles.label;
                using (new EditorGUILayout.HorizontalScope()) {
                    EditorGUILayout.LabelField(AvatarPerformanceStats.GetPerformanceCategoryDisplayName(c), style, GUILayout.Width(200));
                    EditorGUILayout.LabelField(Rating(rp, Value(pc, c)), style, GUILayout.Height(18));
                    EditorGUILayout.LabelField(Rating(rq, Value(quest, c)), style, GUILayout.Height(18));
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
