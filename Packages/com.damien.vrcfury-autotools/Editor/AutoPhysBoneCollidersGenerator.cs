using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;
using B = UnityEngine.HumanBodyBones;

namespace VRCFuryAutoTools {
    public static class AutoPhysBoneCollidersGenerator {
        // a capsule points at the next bone of its chain that exists. Fingers are handled by index.
        private static readonly B[][] Chains = {
            new[] { B.Hips, B.Spine, B.Chest, B.UpperChest, B.Neck, B.Head },
            new[] { B.LeftUpperLeg, B.LeftLowerLeg, B.LeftFoot, B.LeftToes },
            new[] { B.RightUpperLeg, B.RightLowerLeg, B.RightFoot, B.RightToes },
            new[] { B.LeftShoulder, B.LeftUpperArm, B.LeftLowerArm, B.LeftHand, B.LeftMiddleProximal },
            new[] { B.RightShoulder, B.RightUpperArm, B.RightLowerArm, B.RightHand, B.RightMiddleProximal }
        };

        /// Runs right when the component is added, not at build, so the colliders exist for others to reference.
        [InitializeOnLoadMethod]
        private static void Init() {
            ObjectFactory.componentWasAdded += c => { if (c is AutoPhysBoneColliders a) Generate(a); };
        }

        private static bool Ignored(AutoPhysBoneColliders c, B bone) =>
            (c.ignoreFingers && bone >= B.LeftThumbProximal && bone <= B.RightLittleDistal)
            || (c.ignoreToes && (bone == B.LeftToes || bone == B.RightToes))
            || (c.ignoreEyes && (bone == B.LeftEye || bone == B.RightEye));

        private static Transform Next(Animator animator, B bone) {
            // each finger is three bones in a row: proximal, intermediate, distal
            if (bone >= B.LeftThumbProximal && bone <= B.RightLittleDistal)
                return (bone - B.LeftThumbProximal) % 3 < 2 ? animator.GetBoneTransform(bone + 1) : null;
            foreach (var chain in Chains) {
                for (var k = Array.IndexOf(chain, bone) + 1; k > 0 && k < chain.Length; k++) {
                    var t = animator.GetBoneTransform(chain[k]);
                    if (t != null) return t;
                }
            }
            return null;
        }

        /// <summary>Every weighted vertex in its bone's local space (xyz) with its weight (w). Taken from the bind pose, so the current pose does not matter.</summary>
        private static Dictionary<Transform, List<Vector4>> Points(GameObject root) {
            var points = new Dictionary<Transform, List<Vector4>>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var bones = smr.bones;
                var bind = mesh.bindposes;
                var vertices = mesh.vertices;
                var perVertex = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                var i = 0;
                for (var v = 0; v < perVertex.Length; v++) {
                    for (var k = 0; k < perVertex[v]; k++) {
                        var w = weights[i + k];
                        var b = w.boneIndex;
                        if (w.weight <= 0 || b >= bones.Length || b >= bind.Length || bones[b] == null) continue;
                        if (!points.TryGetValue(bones[b], out var list)) points[bones[b]] = list = new List<Vector4>();
                        var p = bind[b].MultiplyPoint3x4(vertices[v]);
                        list.Add(new Vector4(p.x, p.y, p.z, w.weight));
                    }
                    i += perVertex[v];
                }
            }
            return points;
        }

        /// <summary>
        /// Capsule from the bone to <paramref name="end"/> (bone-local), or a sphere around the mesh when the bone has no length.
        /// The radius is the weighted average distance of the mesh from the bone, so the collider sits on the surface.
        /// </summary>
        private static void Fit(VRCPhysBoneColliderBase col, Vector3 end, List<Vector4> points, float scale) {
            // ponytail: average radius, so mesh that is not round around the bone pokes out a little. Use a high percentile if that matters.
            var total = points.Sum(p => p.w);
            var length = end.magnitude;
            var radius = 0f;
            if (length > 0.0001f) {
                var dir = end / length;
                foreach (var p in points) radius += p.w * Vector3.Cross(dir, p).magnitude;
                col.shapeType = VRCPhysBoneColliderBase.ShapeType.Capsule;
                col.position = end / 2;
                col.rotation = Quaternion.FromToRotation(Vector3.up, dir);
                col.height = length;
            } else {
                var center = Vector3.zero;
                foreach (var p in points) center += (Vector3)p * p.w;
                center /= total;
                foreach (var p in points) radius += p.w * ((Vector3)p - center).magnitude;
                col.shapeType = VRCPhysBoneColliderBase.ShapeType.Sphere;
                col.position = center;
            }
            col.radius = radius / total * scale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(col);
        }

        public static void Generate(AutoPhysBoneColliders c) {
            const string undo = "Generate PhysBone colliders";
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c);
            var points = Points(root);

            // humanoid bone -> the bone its capsule points at
            var human = new Dictionary<Transform, Transform>();
            var ignored = new HashSet<Transform>(); // still a capsule's end point, just no collider of their own
            var animator = root.GetComponent<Animator>();
            if (animator != null && animator.isHuman) {
                for (var i = 0; i < (int)B.LastBone; i++) {
                    var t = animator.GetBoneTransform((B)i);
                    if (t == null) continue;
                    human[t] = Next(animator, (B)i);
                    if (Ignored(c, (B)i)) ignored.Add(t);
                }
            }

            // on the avatar or a humanoid bone: the humanoid bones below it. Anywhere else: that bone and all its children as a chain
            var chain = human.Count == 0 || (c.transform != root.transform && !human.ContainsKey(c.transform));
            var bones = chain ? c.GetComponentsInChildren<Transform>(true) : human.Keys.Where(t => t.IsChildOf(c.transform));

            // colliders from an earlier run are updated in place, so references to them survive
            var old = new Dictionary<Transform, VRCPhysBoneColliderBase>();
            foreach (var k in c.colliders) if (k != null) old[k.transform] = k;
            Undo.RecordObject(c, undo);
            c.colliders.Clear();

            foreach (var bone in bones) {
                if (ignored.Contains(bone)) continue;
                if (!points.TryGetValue(bone, out var list)) continue; // moves no mesh, nothing to collide with
                if (!human.TryGetValue(bone, out var next))
                    next = bone.Cast<Transform>().FirstOrDefault(points.ContainsKey) ?? (bone.childCount > 0 ? bone.GetChild(0) : null);
                if (old.TryGetValue(bone, out var col)) {
                    old.Remove(bone);
                    Undo.RecordObject(col, undo);
                } else col = Undo.AddComponent<VRCPhysBoneCollider>(bone.gameObject);
                Fit(col, next != null ? bone.InverseTransformPoint(next.position) : Vector3.zero, list, c.radiusScale);
                c.colliders.Add(col);
            }
            foreach (var k in old.Values) Undo.DestroyObjectImmediate(k); // bones that no longer get one
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
    }
}
