using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace VRCFuryAutoTools {
    public static class AutoDigitigradeLegsBuild {
        /// A joint may fold this far from straight
        private const float MaxBend = 150;

        public class Leg {
            public string side;
            public Transform realUpper, realLower, realFoot;
            public readonly List<Transform> chain = new List<Transform>(); // fake upper leg ... fake foot
            public string error;

            /// A fake leg built like the real one (upper leg, lower leg, foot) copies its rotations in both modes.
            /// IK only pins the foot, so its knee could swing around the hip-to-foot line and the leg looks twisted.
            public bool Humanoid => chain.Count == 3;
        }

        /// <summary>Final IK is an Asset Store asset without an assembly definition, so it can only be reached by name.</summary>
        public static Type FinalIK(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("RootMotion.FinalIK." + name)).FirstOrDefault(t => t != null);

        /// <summary>VRChat only runs Final IK on PC, so every other build gets the constraints.</summary>
        public static bool UsesFinalIK(AutoDigitigradeLegs c) => c.mode == DigitigradeLegMode.FinalIK
            && FinalIK("FABRIK") != null && FinalIK("RotationLimitHinge") != null
            && EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64;

        /// <summary>Both legs with their real and fake bones. Empty when the avatar is not humanoid.</summary>
        public static List<Leg> Plan(AutoDigitigradeLegs c) {
            var legs = new List<Leg>();
            var animator = ZeroWeightBoneAnalysis.AvatarRoot(c).GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return legs;
            var humanoid = new HashSet<Transform>(Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                .Select(i => animator.GetBoneTransform((HumanBodyBones)i)).Where(t => t != null));

            foreach (var left in new[] { true, false }) {
                var leg = new Leg {
                    side = left ? "Left" : "Right",
                    realUpper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg),
                    realLower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg),
                    realFoot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot),
                };
                legs.Add(leg);
                if (leg.realUpper == null || leg.realLower == null || leg.realFoot == null) {
                    leg.error = "the avatar has no humanoid leg on this side";
                    continue;
                }

                var upper = left ? c.leftUpperLeg : c.rightUpperLeg;
                var foot = left ? c.leftFoot : c.rightFoot;
                if (upper == null) upper = FindUpper(animator.transform, leg, humanoid);
                if (upper == null) {
                    leg.error = "no fake leg found, assign its bones";
                    continue;
                }
                if (foot == null) foot = FindFoot(upper, leg.realFoot);
                if (foot == null || foot == upper || foot.parent == upper || !foot.IsChildOf(upper)) {
                    leg.error = $"the foot has to be below '{upper.name}' with at least a lower leg in between";
                    continue;
                }

                for (var t = foot; t != upper; t = t.parent) leg.chain.Insert(0, t);
                leg.chain.Insert(0, upper);
                if (!upper.IsChildOf(animator.transform))
                    leg.error = $"'{upper.name}' is not part of this avatar";
                else if (leg.chain.Any(humanoid.Contains))
                    leg.error = "these are the humanoid legs, assign the fake ones";
                else if (leg.chain.Any(b => b.GetComponents<Component>().Any(k => k is IConstraint || k is VRCConstraintBase
                             || (k != null && k.GetType().Namespace == "RootMotion.FinalIK"))))
                    leg.error = "already has constraints or IK, left as it is";
            }
            return legs;
        }

        // ponytail: takes the bone nearest the real hip joint whose chain reaches below the real knee, which a long tail
        // or skirt on an avatar without fake legs also satisfies. Match bone names if that ever picks wrong; the fields override it.
        private static Transform FindUpper(Transform root, Leg leg, HashSet<Transform> humanoid) {
            var hip = leg.realUpper.position;
            var knee = leg.realLower.position.y;
            var reach = Vector3.Distance(hip, leg.realLower.position) * 0.5f;
            return root.GetComponentsInChildren<Transform>(true)
                .Where(t => !humanoid.Contains(t) && Vector3.Distance(t.position, hip) < reach)
                .Where(t => {
                    var below = t.GetComponentsInChildren<Transform>(true);
                    return !below.Any(humanoid.Contains) && below.Any(d => d.position.y < knee);
                })
                .OrderBy(t => Vector3.Distance(t.position, hip)).FirstOrDefault();
        }

        /// Follows the leg down to its lowest tip and takes the joint nearest the real foot: on a rig made for this,
        /// that is where the two legs meet.
        private static Transform FindFoot(Transform upper, Transform realFoot) {
            float Lowest(Transform t) => t.GetComponentsInChildren<Transform>(true).Min(d => d.position.y);
            var chain = new List<Transform>();
            for (var t = upper; t != null; t = t.Cast<Transform>().OrderBy(Lowest).FirstOrDefault()) chain.Add(t);
            return chain.Skip(2).OrderBy(t => Vector3.Distance(t.position, realFoot.position)).FirstOrDefault();
        }

        public static void Apply(AutoDigitigradeLegs c) {
            var ik = UsesFinalIK(c);
            if (c.mode == DigitigradeLegMode.FinalIK && !ik)
                Debug.LogWarning($"[VRCFuryAutoTools] '{c.name}': Final IK is not in the project or this is not a PC build, "
                    + "the digitigrade legs use rotation constraints instead", c);

            var root = ZeroWeightBoneAnalysis.AvatarRoot(c).transform;
            foreach (var leg in Plan(c)) {
                if (leg.error != null) {
                    Debug.LogWarning($"[VRCFuryAutoTools] '{c.name}': {leg.side.ToLower()} digitigrade leg skipped, {leg.error}", c);
                    continue;
                }
                var last = leg.chain.Count - 1;
                Follow(leg.chain[last], leg.realFoot); // in both modes: FABRIK places the foot but leaves its rotation alone
                if (ik && !leg.Humanoid) {
                    SetupIK(leg, root.right, c.rotationLimits);
                    continue;
                }
                // every second bone turns with the real upper leg, so the hock stays parallel to the thigh.
                // On a humanoid fake leg that is just upper > upper leg and lower > lower leg.
                for (var i = 0; i < last; i++) Follow(leg.chain[i], i % 2 == 0 ? leg.realUpper : leg.realLower);
            }
        }

        /// <summary>Rotation constraint that keeps the bone's current rotation relative to its source.</summary>
        private static void Follow(Transform bone, Transform source) {
            var constraint = bone.gameObject.AddComponent<VRCRotationConstraint>();
            constraint.Sources.Add(new VRCConstraintSource(source, 1, Vector3.zero, Vector3.zero));
            constraint.RotationAtRest = bone.localEulerAngles;
            constraint.RotationOffset = (Quaternion.Inverse(source.rotation) * bone.rotation).eulerAngles;
            constraint.Locked = true;
            constraint.IsActive = true;
        }

        private static void SetupIK(Leg leg, Vector3 avatarRight, bool limits) {
            var chain = leg.chain;
            var foot = chain[chain.Count - 1];
            var target = new GameObject(foot.name + " IK Target").transform;
            target.SetParent(leg.realFoot, false);
            target.SetPositionAndRotation(foot.position, foot.rotation);

            // serialized properties instead of the solver's own methods, so nothing of Final IK runs in the editor
            var solver = new SerializedObject(chain[0].gameObject.AddComponent(FinalIK("FABRIK")));
            solver.FindProperty("solver.target").objectReferenceValue = target;
            var bones = solver.FindProperty("solver.bones");
            bones.arraySize = chain.Count;
            for (var i = 0; i < chain.Count; i++) {
                var bone = bones.GetArrayElementAtIndex(i);
                bone.FindPropertyRelative("transform").objectReferenceValue = chain[i];
                bone.FindPropertyRelative("weight").floatValue = 1;
            }
            solver.ApplyModifiedPropertiesWithoutUndo();
            if (!limits) return;

            // the leg bends in one plane: its normal comes from the most bent joint and always points to the avatar's right
            Vector3 Bend(int i) => Vector3.Cross((chain[i].position - chain[i - 1].position).normalized,
                (chain[i + 1].position - chain[i].position).normalized);
            var normal = avatarRight;
            var sharpest = 0.01f; // straighter than this says nothing about the plane
            for (var i = 1; i < chain.Count - 1; i++) {
                var bend = Bend(i);
                if (bend.magnitude <= sharpest) continue;
                sharpest = bend.magnitude;
                normal = bend.normalized * Mathf.Sign(Vector3.Dot(bend, avatarRight));
            }

            for (var i = 1; i < chain.Count - 1; i++) {
                // 0 = straight, the sign is the side the joint is modelled bent to
                var bend = Vector3.SignedAngle(chain[i].position - chain[i - 1].position, chain[i + 1].position - chain[i].position, normal);
                var hinge = new SerializedObject(chain[i].gameObject.AddComponent(FinalIK("RotationLimitHinge")));
                hinge.FindProperty("axis").vector3Value = Quaternion.Inverse(chain[i].rotation) * normal;
                // a joint modelled straight does not show which way it bends, so it only gets the axis
                hinge.FindProperty("useLimits").boolValue = Mathf.Abs(bend) > 5;
                hinge.FindProperty("min").floatValue = bend > 0 ? -bend : -MaxBend - bend;
                hinge.FindProperty("max").floatValue = bend > 0 ? MaxBend - bend : -bend;
                hinge.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    /// <summary>Runs just after VRCFury (-10000) so legs that came in with a merged outfit or base are in their final place.</summary>
    internal class AutoDigitigradeLegsHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -9500;

        public bool OnPreprocessAvatar(GameObject avatar) {
            foreach (var c in avatar.GetComponentsInChildren<AutoDigitigradeLegs>(true)) AutoDigitigradeLegsBuild.Apply(c);
            return true;
        }
    }
}
