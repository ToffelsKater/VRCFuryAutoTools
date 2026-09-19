using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    public static class ZeroWeightBoneAnalysis {
        public class Result {
            public readonly List<Transform> roots = new List<Transform>(); // topmost removable subtrees
            public readonly HashSet<Transform> all = new HashSet<Transform>(); // every transform in those subtrees
        }

        public static GameObject AvatarRoot(Component c) {
            var d = c.GetComponentInParent<VRCAvatarDescriptor>();
            return d != null ? d.gameObject : c.transform.root.gameObject;
        }

        /// <summary>Per-bone "has weight above threshold" for a mesh. Null = unknown, treat all as used.</summary>
        public static bool[] UsedBones(Mesh mesh, int boneCount, float threshold) {
            if (mesh == null) return null;
            try {
                var used = new bool[boneCount];
                var perVertex = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                var i = 0;
                for (var v = 0; v < perVertex.Length; v++) {
                    for (var k = 0; k < perVertex[v]; k++) {
                        var w = weights[i + k];
                        if (w.weight > threshold && w.boneIndex < boneCount) used[w.boneIndex] = true;
                    }
                    i += perVertex[v];
                }
                return used;
            } catch (Exception) {
                return null;
            }
        }

        public static Result Analyze(ZeroWeightBoneRemover c) {
            var root = AvatarRoot(c);
            var listed = new HashSet<Transform>();   // bones any mesh lists
            var weighted = new HashSet<Transform>(); // bones any mesh actually uses
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var bones = smr.bones;
                var used = UsedBones(smr.sharedMesh, bones.Length, c.weightThreshold);
                for (var i = 0; i < bones.Length; i++) {
                    if (bones[i] == null) continue;
                    listed.Add(bones[i]);
                    if (used == null || used[i]) weighted.Add(bones[i]);
                }
            }

            var prot = Protected(root);

            var removable = new Dictionary<Transform, bool>();
            bool Removable(Transform t) {
                if (removable.TryGetValue(t, out var cached)) return cached;
                var ok = !weighted.Contains(t) && !prot.Contains(t)
                    && !t.GetComponents<Component>().Any(k => !(k is Transform))
                    && !c.keepNames.Any(n => !string.IsNullOrEmpty(n) && t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                if (ok) foreach (Transform ch in t) if (!Removable(ch)) { ok = false; break; }
                return removable[t] = ok;
            }
            bool HasListed(Transform t) => listed.Contains(t) || t.Cast<Transform>().Any(HasListed);

            var result = new Result();
            void Collect(Transform t) {
                foreach (Transform ch in t) {
                    if (Removable(ch) && HasListed(ch)) {
                        result.roots.Add(ch);
                        foreach (var d in ch.GetComponentsInChildren<Transform>(true)) result.all.Add(d);
                    } else Collect(ch);
                }
            }
            Collect(c.transform);
            return result;
        }

        /// Transforms that must survive: humanoid bones, anything any component points at, animated paths, physbone chains.
        private static HashSet<Transform> Protected(GameObject root) {
            var prot = new HashSet<Transform>();
            void Add(Object o) {
                if (o is GameObject g) prot.Add(g.transform);
                else if (o is Component k) prot.Add(k.transform);
            }

            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
                for (var i = 0; i < (int)HumanBodyBones.LastBone; i++) Add(animator.GetBoneTransform((HumanBodyBones)i));

            foreach (var comp in root.GetComponentsInChildren<Component>(true)) {
                if (comp == null || comp is Transform || comp is ZeroWeightBoneRemover || comp is AutoOutfitToggles) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                while (it.Next(true)) {
                    // mesh bone lists are what we are compacting, not references to protect
                    if (it.propertyType == SerializedPropertyType.ObjectReference && !it.propertyPath.StartsWith("m_Bones"))
                        Add(it.objectReferenceValue);
                }
                // physics chains: keep the whole chain, removing an end bone changes the motion
                var name = comp.GetType().Name;
                if (name == "VRCPhysBone" || name == "DynamicBone") {
                    var rp = so.FindProperty("rootTransform") ?? so.FindProperty("m_Root");
                    var chain = (rp != null ? rp.objectReferenceValue as Transform : null) ?? comp.transform;
                    foreach (var t in chain.GetComponentsInChildren<Transform>(true)) prot.Add(t);
                }
            }

            var controllers = new List<RuntimeAnimatorController>();
            if (animator != null && animator.runtimeAnimatorController != null) controllers.Add(animator.runtimeAnimatorController);
            var desc = root.GetComponent<VRCAvatarDescriptor>();
            if (desc != null)
                controllers.AddRange(desc.baseAnimationLayers.Concat(desc.specialAnimationLayers)
                    .Select(l => l.animatorController).Where(a => a != null));
            var paths = new HashSet<string>();
            foreach (var clip in controllers.Distinct().SelectMany(a => a.animationClips).Distinct()) {
                foreach (var b in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                    paths.Add(b.path);
            }
            foreach (var p in paths) {
                var t = root.transform.Find(p);
                if (t != null) prot.Add(t);
            }
            return prot;
        }

        /// <summary>Drops the given bones from a mesh's bone list + bindposes + weights, on a cloned mesh.</summary>
        private static void Compact(SkinnedMeshRenderer smr, HashSet<Transform> removed) {
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            if (mesh == null) return;
            var keep = Enumerable.Range(0, bones.Length).Where(i => bones[i] == null || !removed.Contains(bones[i])).ToList();
            if (keep.Count == bones.Length) return;

            var map = Enumerable.Repeat(-1, bones.Length).ToArray();
            for (var k = 0; k < keep.Count; k++) map[keep[k]] = k;

            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            var newPerVertex = new NativeArray<byte>(perVertex.Length, Allocator.Temp);
            var newWeights = new List<BoneWeight1>(weights.Length);
            var i = 0;
            for (var v = 0; v < perVertex.Length; v++) {
                byte count = 0;
                for (var k = 0; k < perVertex[v]; k++) {
                    var w = weights[i + k];
                    if (w.boneIndex >= map.Length || map[w.boneIndex] < 0) continue; // zero-weight entry on a removed bone
                    w.boneIndex = map[w.boneIndex];
                    newWeights.Add(w);
                    count++;
                }
                i += perVertex[v];
                newPerVertex[v] = count;
            }

            var clone = Object.Instantiate(mesh);
            clone.name = mesh.name;
            var bind = mesh.bindposes;
            clone.bindposes = keep.Select(k => bind[k]).ToArray();
            if (perVertex.Length > 0) {
                var native = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
                clone.SetBoneWeights(newPerVertex, native);
                native.Dispose();
            }
            newPerVertex.Dispose();
            smr.sharedMesh = clone;
            smr.bones = keep.Select(k => bones[k]).ToArray();
        }

        public static int Apply(ZeroWeightBoneRemover c) {
            var result = Analyze(c);
            if (result.roots.Count == 0) return 0;
            foreach (var smr in AvatarRoot(c).GetComponentsInChildren<SkinnedMeshRenderer>(true)) Compact(smr, result.all);
            if (c.logRemoved)
                Debug.Log($"[VRCFuryAutoTools] Removed {result.all.Count} zero-weight bones under '{c.name}':\n"
                    + string.Join("\n", result.all.Select(t => t.name)));
            foreach (var t in result.roots) Object.DestroyImmediate(t.gameObject);
            return result.all.Count;
        }
    }

    /// <summary>Runs just after VRCFury (-10000) so merged outfit armatures, physbones and animations are final.</summary>
    internal class ZeroWeightBoneRemoverHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -9000;

        public bool OnPreprocessAvatar(GameObject avatar) {
            foreach (var c in avatar.GetComponentsInChildren<ZeroWeightBoneRemover>(true)) ZeroWeightBoneAnalysis.Apply(c);
            return true;
        }
    }
}
