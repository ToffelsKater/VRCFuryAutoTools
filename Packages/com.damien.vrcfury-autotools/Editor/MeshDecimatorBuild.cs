using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using Simplifier = VRCFuryAutoTools.MeshSimplification.MeshSimplifier;
using SimplificationOptions = VRCFuryAutoTools.MeshSimplification.SimplificationOptions;

namespace VRCFuryAutoTools {
    public static class MeshDecimatorBuild {
        /// <summary>Holds the reduced meshes of the last build. The SDK saves the avatar as a prefab before it uploads it,
        /// and a mesh that is not an asset would be lost there. Emptied at the start of every build with a decimator.</summary>
        internal const string Folder = "Assets/VRCFuryAutoTools/Decimated Meshes";

        private static Mesh MeshOf(Renderer r) =>
            r is SkinnedMeshRenderer s ? s.sharedMesh
            : r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;

        private static void SetMesh(Renderer r, Mesh m) {
            if (r is SkinnedMeshRenderer s) s.sharedMesh = m;
            else r.GetComponent<MeshFilter>().sharedMesh = m;
        }

        public static int Triangles(Mesh m) {
            var n = 0;
            for (var i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i) / 3;
            return n;
        }

        public class Plan {
            public readonly List<Renderer> reduced = new List<Renderer>();
            public int kept, reducible; // triangles
            /// <summary>The share of its triangles each reduced mesh keeps.</summary>
            public float quality = 1;
            public int Total => kept + reducible;
        }

        public static Plan MakePlan(MeshDecimator c) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c);
            var keep = new HashSet<Renderer>(c.exclude.Where(r => r != null));
            var d = root.GetComponent<VRCAvatarDescriptor>();
            if (c.keepFace && d != null) {
                if (d.VisemeSkinnedMesh != null) keep.Add(d.VisemeSkinnedMesh);
                if (d.customEyeLookSettings.eyelidsSkinnedMesh != null) keep.Add(d.customEyeLookSettings.eyelidsSkinnedMesh);
            }
            var plan = new Plan();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) {
                var m = MeshOf(r);
                if (m == null) continue;
                var n = Triangles(m);
                // lines and points can't be simplified
                var triangles = Enumerable.Range(0, m.subMeshCount).All(i => m.GetTopology(i) == MeshTopology.Triangles);
                if (keep.Contains(r) || !triangles) plan.kept += n;
                else {
                    plan.reduced.Add(r);
                    plan.reducible += n;
                }
            }
            if (plan.reducible > 0) plan.quality = Mathf.Clamp01((float)(c.targetTriangles - plan.kept) / plan.reducible);
            return plan;
        }

        private static Mesh Reduce(Mesh mesh, float quality, SimplificationOptions options) {
            var s = new Simplifier { SimplificationOptions = options };
            s.Initialize(mesh);
            s.SimplifyMesh(quality);
            var reduced = s.ToMesh();
            reduced.name = mesh.name;
            AutoQuestCopyGenerator.MakeFolder(Folder);
            AssetDatabase.CreateAsset(reduced, AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{AutoQuestCopyGenerator.FileName(mesh.name)}.asset"));
            return reduced;
        }

        public static void Apply(MeshDecimator c) {
            var plan = MakePlan(c);
            if (plan.Total <= c.targetTriangles) return;
            if (plan.quality <= 0) {
                Debug.LogWarning($"[VRCFuryAutoTools] '{c.name}': the kept meshes alone have {plan.kept} triangles, more than the target of "
                    + $"{c.targetTriangles}. Nothing was reduced.", c);
                return;
            }
            var options = SimplificationOptions.Default;
            options.PreserveBorderEdges = c.preserveBorders;
            options.PreserveUVSeamEdges = c.preserveUvSeams;
            var done = new Dictionary<Mesh, Mesh>(); // a mesh several renderers share is reduced once
            foreach (var r in plan.reduced) {
                var mesh = MeshOf(r);
                if (!done.TryGetValue(mesh, out var reduced)) {
                    try {
                        reduced = Reduce(mesh, plan.quality, options);
                    } catch (System.Exception e) { // one odd mesh must not stop the upload: it stays as it is
                        Debug.LogWarning($"[VRCFuryAutoTools] '{r.name}' could not be reduced and stays as it is: {e.Message}", r);
                        reduced = mesh;
                    }
                    done[mesh] = reduced;
                }
                var smr = r as SkinnedMeshRenderer;
                var weights = smr != null ? Enumerable.Range(0, mesh.blendShapeCount).Select(smr.GetBlendShapeWeight).ToList() : null;
                SetMesh(r, reduced);
                if (smr != null) for (var i = 0; i < weights.Count; i++) smr.SetBlendShapeWeight(i, weights[i]);
            }
            var after = plan.kept + done.Values.Sum(Triangles);
            var before = plan.kept + done.Keys.Sum(Triangles);
            Debug.Log($"[VRCFuryAutoTools] '{c.name}' reduced {done.Count} meshes to {plan.quality:P0} of their triangles: "
                + $"{before} > {after} (target {c.targetTriangles}).", c);
            if (after > c.targetTriangles * 1.1f)
                Debug.LogWarning($"[VRCFuryAutoTools] '{c.name}' is still at {after} triangles, over the target of {c.targetTriangles}. "
                    + "Turn off Preserve Borders or Preserve Uv Seams to reduce further.", c);
        }
    }

    /// <summary>Runs before VRCFury (-10000), so VRCFury's own meshes (SPS) are never reduced, and after the Mesh Bone Stripper.</summary>
    internal class MeshDecimatorHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -14000;

        public bool OnPreprocessAvatar(GameObject avatar) {
            var all = avatar.GetComponentsInChildren<MeshDecimator>(true);
            if (all.Length == 0) return true;
            AssetDatabase.DeleteAsset(MeshDecimatorBuild.Folder); // the meshes of the build before
            foreach (var c in all) MeshDecimatorBuild.Apply(c);
            return true;
        }
    }
}
