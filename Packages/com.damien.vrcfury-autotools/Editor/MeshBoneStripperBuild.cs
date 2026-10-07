using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    public static class MeshBoneStripperAnalysis {
        private static string Norm(string s) => s.Replace(" ", "").Replace("_", "");

        /// Listed meshes inside this avatar. Anything outside is ignored so a stray reference can never delete scene objects.
        public static List<SkinnedMeshRenderer> Meshes(MeshBoneStripper c) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c).transform;
            return c.meshes.Where(m => m != null && m.transform.IsChildOf(root)).Distinct().ToList();
        }

        public static ZeroWeightBoneAnalysis.Result Analyze(MeshBoneStripper c) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c);
            var scope = new HashSet<Transform>(); // bones the listed meshes list
            var keep = new HashSet<Transform>();
            foreach (var smr in Meshes(c)) {
                keep.Add(smr.transform); // a listed mesh parented under a bone must not go with it
                if (smr.rootBone != null) keep.Add(smr.rootBone); // bounds are relative to it
                var bones = smr.bones;
                var used = ZeroWeightBoneAnalysis.UsedBones(smr.sharedMesh, bones.Length, c.weightThreshold);
                for (var i = 0; i < bones.Length; i++) {
                    if (bones[i] == null || !bones[i].IsChildOf(root.transform)) continue;
                    scope.Add(bones[i]);
                    if (used == null || used[i]) keep.Add(bones[i]);
                }
            }

            var spine = new[] {
                (HumanBodyBones.Hips, c.keepHips), (HumanBodyBones.Spine, c.keepSpine), (HumanBodyBones.Chest, c.keepChest),
                (HumanBodyBones.UpperChest, c.keepUpperChest), (HumanBodyBones.Neck, c.keepNeck), (HumanBodyBones.Head, c.keepHead)
            }.Where(s => s.Item2).Select(s => s.Item1).ToList();
            // matched by name too, so an outfit's own copy of the armature counts
            var spineNames = new HashSet<string>(spine.Select(b => b.ToString()), StringComparer.OrdinalIgnoreCase);
            foreach (var a in root.GetComponentsInChildren<Animator>(true).Where(a => a.isHuman)) {
                foreach (var b in spine) {
                    var t = a.GetBoneTransform(b);
                    if (t != null) { keep.Add(t); spineNames.Add(Norm(t.name)); }
                }
                // the avatar's own humanoid bones never go, that would break the rig
                if (a.gameObject != root) continue;
                for (var i = 0; i < (int)HumanBodyBones.LastBone; i++) {
                    var t = a.GetBoneTransform((HumanBodyBones)i);
                    if (t != null) keep.Add(t);
                }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                if (spineNames.Contains(Norm(t.name))
                    || c.keepNames.Any(n => !string.IsNullOrEmpty(n) && t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                    keep.Add(t);
            }

            // kept bones and everything above them
            var blocked = new HashSet<Transform>();
            foreach (var k in keep) for (var t = k; t != null && blocked.Add(t); t = t.parent) { }

            var result = new ZeroWeightBoneAnalysis.Result();
            foreach (var t in scope) {
                if (blocked.Contains(t) || (scope.Contains(t.parent) && !blocked.Contains(t.parent))) continue;
                result.roots.Add(t);
                foreach (var d in t.GetComponentsInChildren<Transform>(true)) result.all.Add(d);
            }
            return result;
        }

        /// <summary>Meshes not in the list that are weighted to bones being removed. They will deform wrong.</summary>
        public static List<SkinnedMeshRenderer> Broken(MeshBoneStripper c, ZeroWeightBoneAnalysis.Result result) {
            var listed = new HashSet<SkinnedMeshRenderer>(Meshes(c));
            return ZeroWeightBoneAnalysis.AvatarRoot(c).GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(smr => {
                if (listed.Contains(smr) || result.all.Contains(smr.transform)) return false;
                var bones = smr.bones;
                var used = ZeroWeightBoneAnalysis.UsedBones(smr.sharedMesh, bones.Length, c.weightThreshold);
                return Enumerable.Range(0, bones.Length).Any(i => bones[i] != null && result.all.Contains(bones[i]) && (used == null || used[i]));
            }).ToList();
        }

        public static int Apply(MeshBoneStripper c) {
            var result = Analyze(c);
            if (result.roots.Count == 0) return 0;
            var broken = Broken(c, result);
            if (broken.Count > 0)
                Debug.LogWarning($"[VRCFuryAutoTools] '{c.name}' removes bones these unlisted meshes are weighted to: "
                    + string.Join(", ", broken.Select(s => s.name)), c);
            foreach (var smr in Meshes(c)) ZeroWeightBoneAnalysis.Compact(smr, result.all);
            // a physbone/collider/contact left pointing at a deleted root would fall back to its own object, so it goes too
            foreach (var mb in ZeroWeightBoneAnalysis.AvatarRoot(c).GetComponentsInChildren<MonoBehaviour>(true)) {
                if (mb == null || result.all.Contains(mb.transform)) continue;
                var rp = new SerializedObject(mb).FindProperty("rootTransform");
                if (rp != null && rp.propertyType == SerializedPropertyType.ObjectReference
                    && rp.objectReferenceValue is Transform t && result.all.Contains(t))
                    Object.DestroyImmediate(mb);
            }
            var count = result.all.Count;
            Debug.Log($"[VRCFuryAutoTools] '{c.name}' removed {count} bones:\n" + string.Join("\n", result.all.Select(t => t.name)));
            foreach (var t in result.roots) if (t != null) Object.DestroyImmediate(t.gameObject);
            return count;
        }
    }

    /// <summary>Runs before VRCFury (-10000) so each outfit still has its own armature and Armature Link only merges what is left.</summary>
    internal class MeshBoneStripperHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -15000;

        public bool OnPreprocessAvatar(GameObject avatar) {
            foreach (var c in avatar.GetComponentsInChildren<MeshBoneStripper>(true)) MeshBoneStripperAnalysis.Apply(c);
            return true;
        }
    }
}
