using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace VRCFuryAutoTools {
    public static class OutfitFitterBuild {
        public class Session {
            public string error;
            public OutfitBonePlan plan;
            public readonly List<OutfitFitMesh> meshes = new List<OutfitFitMesh>();
        }

        public static List<SkinnedMeshRenderer> Bodies(OutfitFitter c) {
            var listed = c.bodies.Where(b => b != null && b.sharedMesh != null).Distinct().ToList();
            if (listed.Count > 0) return listed;
            return ZeroWeightBoneAnalysis.AvatarRoot(c).GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(s => s.sharedMesh != null && !s.transform.IsChildOf(c.transform)
                    && s.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        /// <summary>Bone plan, body and bone-fitted meshes. Reads the scene only, so the inspector can preview with it.</summary>
        public static Session Prepare(OutfitFitter c) {
            var s = new Session();
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c);
            var animator = root.GetComponent<Animator>();
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            var bodies = Bodies(c);
            if (root == c.gameObject) s.error = "Put this on the outfit, not on the avatar.";
            else if (hips == null) s.error = "The avatar needs a humanoid rig.";
            else if (bodies.Count == 0) s.error = "No body mesh found. Drag the avatar's body into Bodies.";
            if (s.error != null) return s;

            s.plan = OutfitBonePlan.Create(c.transform, hips, animator.GetBoneTransform(HumanBodyBones.Neck), c.widthScale);
            if (s.plan.outfitHips == null) s.error = "No Hips bone found in the outfit.";
            else if (s.plan.match.Count < 2) s.error = "None of the outfit's bone names match the avatar's.";
            if (s.error != null) return s;

            var body = BodySurface.Create(bodies);
            foreach (var smr in c.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if (smr.sharedMesh == null || bodies.Contains(smr) || !smr.bones.Any(b => b != null && b.IsChildOf(s.plan.outfitHips))) continue;
                s.meshes.Add(OutfitFitMesh.Create(smr, s.plan, body));
            }
            return s;
        }

        /// <summary>The paint for a mesh, or null if it has none or its vertex count changed since. Create replaces stale paint.</summary>
        public static OutfitFitter.PaintedEdits Edits(OutfitFitter c, OutfitFitMesh f, bool create) {
            var e = c.edits.FirstOrDefault(x => x.renderer == f.renderer);
            var n = f.VertexCount;
            if (e != null && e.vertexCount == n && e.offset?.Length == n && e.smooth?.Length == n && e.tight?.Length == n) return e;
            if (!create) return null;
            if (e == null) c.edits.Add(e = new OutfitFitter.PaintedEdits { renderer = f.renderer });
            e.vertexCount = n;
            e.offset = new float[n];
            e.smooth = new float[n];
            e.tight = new float[n];
            return e;
        }

        public static void Solve(OutfitFitter c, OutfitFitMesh f) {
            var e = Edits(c, f, false);
            f.Solve(c.gap, c.tightenBelow, c.smoothing, c.fixClipping, e?.offset, e?.smooth, e?.tight);
        }

        public static void Apply(OutfitFitter c) {
            var s = Prepare(c);
            if (s.error != null) {
                Debug.LogWarning($"[VRCFuryAutoTools] '{c.name}' was not fitted: {s.error}", c);
                return;
            }
            // every mesh is solved against the original poses before any bone moves
            foreach (var f in s.meshes) Solve(c, f);
            s.plan.MoveBones();
            foreach (var f in s.meshes) f.Write();
            Debug.Log($"[VRCFuryAutoTools] '{c.name}' fitted {s.meshes.Count} meshes, {s.plan.match.Count} bones matched to the avatar");
        }
    }

    /// <summary>Runs before the mesh bone stripper (-15000) and VRCFury (-10000), while the outfit still has its own armature.</summary>
    internal class OutfitFitterHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -16000;

        public bool OnPreprocessAvatar(GameObject avatar) {
            foreach (var c in avatar.GetComponentsInChildren<OutfitFitter>(true)) OutfitFitterBuild.Apply(c);
            return true;
        }
    }
}
