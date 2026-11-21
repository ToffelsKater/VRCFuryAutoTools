using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    public static class SkinnedMeshMergerBuild {
        /// <summary>Holds the merged meshes of the last build (the SDK saves the avatar as a prefab, a mesh that is no asset
        /// would be lost). Emptied at the start of every build with a merger.</summary>
        internal const string Folder = "Assets/VRCFuryAutoTools/Merged Meshes";

        /// <summary>What the avatar's animator controllers animate, by object path: (component type, property).</summary>
        private static Dictionary<string, List<(Type type, string property)>> Animated(GameObject root) {
            var controllers = new List<RuntimeAnimatorController>();
            var d = root.GetComponent<VRCAvatarDescriptor>();
            if (d != null)
                controllers.AddRange(d.baseAnimationLayers.Concat(d.specialAnimationLayers)
                    .Where(l => !l.isDefault && l.animatorController != null).Select(l => l.animatorController));
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null) controllers.Add(animator.runtimeAnimatorController);

            var animated = new Dictionary<string, List<(Type, string)>>();
            foreach (var clip in controllers.SelectMany(c => c.animationClips).Where(c => c != null).Distinct())
            foreach (var b in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip))) {
                if (!animated.TryGetValue(b.path, out var list)) animated[b.path] = list = new List<(Type, string)>();
                list.Add((b.type, b.propertyName));
            }
            return animated;
        }

        /// <summary>Skinned meshes another component points at (the avatar descriptor's face mesh, ...): merging would cut that link.
        /// Editor-only components don't count, they are gone before upload.</summary>
        private static HashSet<SkinnedMeshRenderer> Referenced(GameObject root) {
            var referenced = new HashSet<SkinnedMeshRenderer>();
            foreach (var k in root.GetComponentsInChildren<Component>(true)) {
                if (k == null || k is Transform || k is Renderer || k is IEditorOnly) continue;
                var it = new SerializedObject(k).GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is SkinnedMeshRenderer s) referenced.Add(s);
            }
            return referenced;
        }

        /// <summary>
        /// The group key of a mesh that can be merged, null if it has to stay as it is. Meshes with the same key are shown and hidden
        /// by the same animated objects and render with the same settings. <paramref name="shapesAnimated"/>: animations change its
        /// blendshapes, which only works on the mesh the others are merged into (its path and blendshape names stay).
        /// </summary>
        private static string Key(SkinnedMeshRenderer r, Transform root, Dictionary<string, List<(Type type, string property)>> animated,
            HashSet<SkinnedMeshRenderer> skip, out bool shapesAnimated) {
            shapesAnimated = false;
            var m = r.sharedMesh;
            if (skip.Contains(r) || m == null || !r.enabled || r.sharedMaterials.Length != m.subMeshCount
                || r.GetComponent<Cloth>() != null
                || Enumerable.Range(0, m.subMeshCount).Any(i => m.GetTopology(i) != MeshTopology.Triangles)
                || r.sharedMaterials.Any(mat => mat != null && mat.HasProperty("_SPS_Bake")) // VRCFury SPS reads its baked data by vertex index
                || r.GetComponentsInParent<Animator>(true).Any(a => a.transform != root)) // animated by its own animator
                return null;

            if (animated.TryGetValue(AnimationUtility.CalculateTransformPath(r.transform, root), out var own)) {
                foreach (var (type, property) in own.Where(p => typeof(Renderer).IsAssignableFrom(p.type))) {
                    if (!property.StartsWith("blendShape.", StringComparison.Ordinal)) return null; // materials, enabled, ...: they would hit every merged mesh
                    shapesAnimated = true;
                }
            }

            var shownBy = new List<int>(); // the objects whose animated active state decides if it shows
            for (var t = r.transform; t != root; t = t.parent) {
                if (t.CompareTag("EditorOnly")) return null; // removed before upload
                var toggled = animated.TryGetValue(AnimationUtility.CalculateTransformPath(t, root), out var list)
                    && list.Any(p => p.type == typeof(GameObject) && p.property == "m_IsActive");
                if (toggled) shownBy.Add(t.GetInstanceID());
                else if (!t.gameObject.activeSelf) return null; // never shown
            }

            var anchor = r.probeAnchor != null ? r.probeAnchor.GetInstanceID() : 0;
            var volume = r.lightProbeProxyVolumeOverride != null ? r.lightProbeProxyVolumeOverride.GetInstanceID() : 0;
            return string.Join(",", shownBy) + $"|{r.shadowCastingMode}|{r.receiveShadows}|{r.lightProbeUsage}|{r.reflectionProbeUsage}|{anchor}|{volume}"
                + $"|{r.quality}|{r.updateWhenOffscreen}|{r.skinnedMotionVectors}|{r.allowOcclusionWhenDynamic}|{r.motionVectorGenerationMode}"
                + $"|{r.gameObject.layer}|{r.sortingLayerID}|{r.sortingOrder}|{r.rendererPriority}|{r.renderingLayerMask}"
                + $"|{m.HasVertexAttribute(VertexAttribute.Normal)}{m.HasVertexAttribute(VertexAttribute.Tangent)}{m.HasVertexAttribute(VertexAttribute.Color)}";
        }

        /// <summary>The groups of meshes the build merges, the mesh they go into first.</summary>
        public static List<List<SkinnedMeshRenderer>> Groups(SkinnedMeshMerger c) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c).transform;
            var animated = Animated(root.gameObject);
            var skip = Referenced(root.gameObject);
            skip.UnionWith(c.exclude.Where(r => r != null));
            var groups = new Dictionary<string, List<SkinnedMeshRenderer>>();
            var hosts = new Dictionary<string, SkinnedMeshRenderer>();
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var key = Key(r, root, animated, skip, out var shapesAnimated);
                if (key == null) continue;
                if (shapesAnimated) {
                    if (hosts.ContainsKey(key)) continue; // only one mesh per group can keep animated blendshapes
                    hosts[key] = r;
                }
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<SkinnedMeshRenderer>();
                list.Add(r);
            }
            foreach (var kv in hosts) { // the mesh with animated blendshapes is the one the others go into
                groups[kv.Key].Remove(kv.Value);
                groups[kv.Key].Insert(0, kv.Value);
            }
            return groups.Values.Where(g => g.Count > 1).ToList();
        }

        /// <summary>
        /// Merges the meshes into the first one, which keeps its object, its blendshape names and its place in animations. Each mesh
        /// keeps its own bones and bind poses, so meshes from differently posed or scaled armatures still skin right. Blendshapes of
        /// the others whose name is taken get the mesh name added.
        /// </summary>
        private static void Merge(List<SkinnedMeshRenderer> group, bool sameMaterials) {
            var host = group[0];
            var meshes = group.Select(r => r.sharedMesh).ToList();
            var total = meshes.Sum(m => m.vertexCount);
            var hasNormals = meshes[0].HasVertexAttribute(VertexAttribute.Normal);
            var hasTangents = meshes[0].HasVertexAttribute(VertexAttribute.Tangent);
            var hasColors = meshes[0].HasVertexAttribute(VertexAttribute.Color);
            var uvDims = Enumerable.Range(0, 8).Select(ch => meshes.Max(m => m.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + ch))).ToArray();

            var verts = new List<Vector3>(total);
            var normals = new List<Vector3>(); var tangents = new List<Vector4>(); var colors = new List<Color>();
            var uvs = uvDims.Select(_ => new List<Vector4>()).ToArray();
            var perVertex = new List<byte>(total); var weights = new List<BoneWeight1>();
            var bones = new List<Transform>(); var bindposes = new List<Matrix4x4>();
            var boneIndex = new Dictionary<(Transform, Matrix4x4), int>();
            int Bone(Transform t, Matrix4x4 bind) {
                if (boneIndex.TryGetValue((t, bind), out var i)) return i;
                bones.Add(t); bindposes.Add(bind);
                return boneIndex[(t, bind)] = bones.Count - 1;
            }
            var materials = new List<Material>(); var slots = new List<List<int>>();
            var slotOf = new Dictionary<Material, int>();
            var shapes = new List<(string name, int mesh, int shape, float weight)>();
            var names = new HashSet<string>();
            var offsets = new int[group.Count];
            var bounds = new List<Bounds>(group.Select(r => r.bounds)); // before anything changes

            for (int i = 0, offset = 0; i < group.Count; offset += meshes[i].vertexCount, i++) {
                var r = group[i]; var m = meshes[i];
                offsets[i] = offset;
                verts.AddRange(m.vertices);
                if (hasNormals) normals.AddRange(m.normals);
                if (hasTangents) tangents.AddRange(m.tangents);
                if (hasColors) colors.AddRange(m.colors);
                for (var ch = 0; ch < 8; ch++) {
                    if (uvDims[ch] == 0) continue;
                    var uv = new List<Vector4>();
                    m.GetUVs(ch, uv);
                    uvs[ch].AddRange(uv.Count == m.vertexCount ? uv : new List<Vector4>(new Vector4[m.vertexCount]));
                }

                var rb = r.bones; var bp = m.bindposes;
                var pv = m.GetBonesPerVertex();
                if (rb.Length == 0 || bp.Length == 0 || pv.Length == 0) { // not skinned: its own object moves it
                    var b = Bone(r.transform, Matrix4x4.identity);
                    for (var v = 0; v < m.vertexCount; v++) { perVertex.Add(1); weights.Add(new BoneWeight1 { boneIndex = b, weight = 1 }); }
                } else {
                    var map = Enumerable.Range(0, Mathf.Min(rb.Length, bp.Length)).Select(k => Bone(rb[k], bp[k])).ToArray();
                    perVertex.AddRange(pv);
                    foreach (var w in m.GetAllBoneWeights())
                        weights.Add(new BoneWeight1 { boneIndex = w.boneIndex < map.Length ? map[w.boneIndex] : 0, weight = w.weight });
                }

                var mats = r.sharedMaterials;
                for (var s = 0; s < m.subMeshCount; s++) {
                    var mat = mats[s];
                    if (!sameMaterials || mat == null || !slotOf.TryGetValue(mat, out var slot)) {
                        slot = slots.Count;
                        slots.Add(new List<int>());
                        materials.Add(mat);
                        if (sameMaterials && mat != null) slotOf[mat] = slot;
                    }
                    var o = offset;
                    slots[slot].AddRange(m.GetIndices(s).Select(x => x + o));
                }

                for (var s = 0; s < m.blendShapeCount; s++) {
                    var name = m.GetBlendShapeName(s);
                    for (var n = 2; !names.Add(name); n++) name = n == 2 ? $"{m.GetBlendShapeName(s)} ({r.name})" : $"{m.GetBlendShapeName(s)} ({r.name} {n})";
                    shapes.Add((name, i, s, r.GetBlendShapeWeight(s)));
                }
            }

            var merged = new Mesh { name = meshes[0].name + " (merged)", indexFormat = total > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            merged.SetVertices(verts);
            if (hasNormals) merged.SetNormals(normals);
            if (hasTangents) merged.SetTangents(tangents);
            if (hasColors) merged.SetColors(colors);
            for (var ch = 0; ch < 8; ch++) {
                if (uvDims[ch] == 2) merged.SetUVs(ch, uvs[ch].Select(v => (Vector2)v).ToList());
                else if (uvDims[ch] == 3) merged.SetUVs(ch, uvs[ch].Select(v => (Vector3)v).ToList());
                else if (uvDims[ch] > 0) merged.SetUVs(ch, uvs[ch]);
            }
            merged.subMeshCount = slots.Count;
            for (var s = 0; s < slots.Count; s++) merged.SetIndices(slots[s], MeshTopology.Triangles, s, false);
            using (var pvNative = new NativeArray<byte>(perVertex.ToArray(), Allocator.Temp))
            using (var wNative = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                merged.SetBoneWeights(pvNative, wNative);
            merged.bindposes = bindposes.ToArray();

            Vector3[] fv = new Vector3[total], fn = new Vector3[total], ft = new Vector3[total];
            foreach (var (name, i, s, _) in shapes) {
                var m = meshes[i]; var n = m.vertexCount;
                Vector3[] dv = new Vector3[n], dn = new Vector3[n], dt = new Vector3[n];
                for (var f = 0; f < m.GetBlendShapeFrameCount(s); f++) {
                    m.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    Array.Copy(dv, 0, fv, offsets[i], n); Array.Copy(dn, 0, fn, offsets[i], n); Array.Copy(dt, 0, ft, offsets[i], n);
                    merged.AddBlendShapeFrame(name, m.GetBlendShapeFrameWeight(s, f), fv, fn, ft);
                }
                Array.Clear(fv, offsets[i], n); Array.Clear(fn, offsets[i], n); Array.Clear(ft, offsets[i], n);
            }
            merged.RecalculateBounds();
            AutoQuestCopyGenerator.MakeFolder(Folder);
            AssetDatabase.CreateAsset(merged, AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{AutoQuestCopyGenerator.FileName(merged.name)}.asset"));

            host.sharedMesh = merged;
            host.bones = bones.ToArray();
            host.sharedMaterials = materials.ToArray();
            for (var k = 0; k < shapes.Count; k++) host.SetBlendShapeWeight(k, shapes[k].weight);
            // the bounds are in the root bone's space: all the merged meshes' bounds as they were
            var space = host.rootBone != null ? host.rootBone : host.transform;
            var local = new Bounds(space.InverseTransformPoint(bounds[0].center), Vector3.zero);
            foreach (var b in bounds)
                for (var corner = 0; corner < 8; corner++)
                    local.Encapsulate(space.InverseTransformPoint(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
            host.localBounds = local;
            foreach (var r in group.Skip(1)) Object.DestroyImmediate(r);
        }

        public static void Apply(SkinnedMeshMerger c) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c);
            var before = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            var groups = Groups(c);
            if (groups.Count == 0) return;
            var lines = new List<string>();
            foreach (var g in groups) {
                var names = string.Join(", ", g.Select(r => r.name)); // before the merge destroys them
                try {
                    Merge(g, c.mergeSameMaterials);
                    lines.Add(names);
                } catch (Exception e) { // one odd group must not stop the upload: its meshes stay as they are
                    Debug.LogWarning($"[VRCFuryAutoTools] Could not merge {names}, they stay as they are: {e.Message}", c);
                }
            }
            var after = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            Debug.Log($"[VRCFuryAutoTools] '{c.name}' merged skinned meshes: {before} > {after}.\n" + string.Join("\n", lines.Select(l => "into one: " + l)), c);
        }
    }

    /// <summary>
    /// Runs after VRCFury (-10000), so its toggles are in the animator controllers, and after the decimator's second pass (-2000),
    /// so merged meshes are already reduced. Before NDMF's optimizing phase (-1025) and the SDK removing editor-only objects (-1024).
    /// </summary>
    internal class SkinnedMeshMergerHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -1500;

        public bool OnPreprocessAvatar(GameObject avatar) {
            var c = avatar.GetComponentInChildren<SkinnedMeshMerger>(true);
            if (c == null) return true;
            AssetDatabase.DeleteAsset(SkinnedMeshMergerBuild.Folder); // the meshes of the build before
            SkinnedMeshMergerBuild.Apply(c);
            return true;
        }
    }
}
