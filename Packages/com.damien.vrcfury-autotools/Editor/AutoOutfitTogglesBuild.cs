using System;
using System.Collections.Generic;
using System.Linq;
using com.vrcfury.api;
using com.vrcfury.api.Actions;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace VRCFuryAutoTools {
    public static class AutoOutfitTogglesPlan {
        /// <summary>Renderer + menu path pairs this component will turn into VRCFury toggles.</summary>
        public static List<(Renderer renderer, string path)> Plan(AutoOutfitToggles c) {
            var outfit = string.IsNullOrWhiteSpace(c.outfitName) ? c.name.Replace("(Clone)", "").Trim() : c.outfitName.Trim();
            var seen = new Dictionary<string, int>();
            var result = new List<(Renderer, string)>();
            foreach (var r in c.GetComponentsInChildren<Renderer>(c.includeInactive)) {
                if (!HasMesh(r)) continue;
                var piece = r.gameObject.name;
                if (c.excludeNames.Any(n => !string.IsNullOrEmpty(n) && piece.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                piece = Clean(piece);
                seen[piece] = seen.TryGetValue(piece, out var count) ? count + 1 : 1;
                if (seen[piece] > 1) piece += " " + seen[piece];
                result.Add((r, $"{Clean(c.menuRoot)}/{Clean(outfit)}/{piece}".TrimStart('/')));
            }
            return result;
        }

        private static string Clean(string s) => (s ?? "").Replace('/', '-').Trim();

        private static bool HasMesh(Renderer r) {
            if (r is SkinnedMeshRenderer s) return s.sharedMesh != null;
            var f = r.GetComponent<MeshFilter>();
            return r is MeshRenderer && f != null && f.sharedMesh != null;
        }
    }

    /// <summary>Runs before VRCFury (-10000) so the toggles we add get built with everything else.</summary>
    internal class AutoOutfitTogglesHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => -20000;

        public bool OnPreprocessAvatar(GameObject avatar) {
            foreach (var c in avatar.GetComponentsInChildren<AutoOutfitToggles>(true)) {
                foreach (var (renderer, path) in AutoOutfitTogglesPlan.Plan(c)) {
                    var toggle = FuryComponents.CreateToggle(renderer.gameObject);
                    toggle.SetMenuPath(path);
                    if (c.defaultOn) toggle.SetDefaultOn();
                    if (c.saved) toggle.SetSaved();
                    var actions = toggle.GetActions();
                    actions.AddTurnOn(renderer.gameObject);
                    foreach (var a in c.pieceActions.Where(p => p.piece == renderer).SelectMany(p => p.actions))
                        Add(actions, a, avatar.transform);
                }
            }
            return true;
        }

        /// <summary>One constant key on a property of an object under the avatar.</summary>
        private static void Set(AnimationClip clip, Transform target, Transform avatar, Type type, string property, float value) =>
            clip.SetCurve(AnimationUtility.CalculateTransformPath(target, avatar), type, property, new AnimationCurve(new Keyframe(0, value)));

        private static void Add(FuryActionSet actions, OutfitAction a, Transform avatar) {
            switch (a.type) {
                case OutfitActionType.TurnOn:
                    if (a.obj != null) actions.AddTurnOn(a.obj);
                    break;
                case OutfitActionType.TurnOff:
                    // VRCFury's public API can only turn objects on, so turning off goes through a one-key clip.
                    // VRCFury loads clips that are not assets (VFClip.Load) and resolves their paths from the avatar root.
                    if (a.obj == null || !a.obj.transform.IsChildOf(avatar)) break;
                    var off = new AnimationClip { name = "Turn Off " + a.obj.name };
                    Set(off, a.obj.transform, avatar, typeof(GameObject), "m_IsActive", 0);
                    actions.AddAnimationClip(off);
                    break;
                case OutfitActionType.MaterialSwap:
                    // not in the public API either: same one-key clip, on the binding VRCFury's own Material Swap uses
                    if (a.renderer == null || a.material == null || !a.renderer.transform.IsChildOf(avatar)) break;
                    var swap = new AnimationClip { name = "Material Swap " + a.renderer.name };
                    AnimationUtility.SetObjectReferenceCurve(swap,
                        EditorCurveBinding.PPtrCurve(AnimationUtility.CalculateTransformPath(a.renderer.transform, avatar),
                            a.renderer.GetType(), $"m_Materials.Array.data[{a.materialIndex}]"),
                        new[] { new ObjectReferenceKeyframe { time = 0, value = a.material } });
                    actions.AddAnimationClip(swap);
                    break;
                case OutfitActionType.MaterialProperty:
                    // like VRCFury's Material Property: "material.<name>" curves, one per channel. A name with a dot would
                    // animate one channel of a bundle, which breaks it.
                    if (string.IsNullOrEmpty(a.name) || a.name.Contains(".")) break;
                    var meshes = a.renderer != null
                        ? new[] { a.renderer }
                        : avatar.GetComponentsInChildren<Renderer>(true).Where(x => x.sharedMaterials.Any(m => m != null && m.HasProperty(a.name))).ToArray();
                    var property = new AnimationClip { name = "Material Property " + a.name };
                    foreach (var mesh in meshes) {
                        if (!mesh.transform.IsChildOf(avatar)) continue;
                        void Channel(string suffix, float v) => Set(property, mesh.transform, avatar, mesh.GetType(), $"material.{a.name}{suffix}", v);
                        switch (a.propertyType) {
                            case OutfitPropertyType.Float:
                                Channel("", a.value);
                                break;
                            case OutfitPropertyType.Color:
                                Channel(".r", a.color.r); Channel(".g", a.color.g); Channel(".b", a.color.b); Channel(".a", a.color.a);
                                break;
                            case OutfitPropertyType.Vector:
                                Channel(".x", a.vector.x); Channel(".y", a.vector.y); Channel(".z", a.vector.z); Channel(".w", a.vector.w);
                                break;
                        }
                    }
                    actions.AddAnimationClip(property);
                    break;
                case OutfitActionType.Scale:
                    // like VRCFury's Scale: the object's current scale times the multiplier
                    if (a.obj == null || !a.obj.transform.IsChildOf(avatar)) break;
                    var scale = new AnimationClip { name = "Scale " + a.obj.name };
                    var to = a.obj.transform.localScale * a.value;
                    Set(scale, a.obj.transform, avatar, typeof(Transform), "m_LocalScale.x", to.x);
                    Set(scale, a.obj.transform, avatar, typeof(Transform), "m_LocalScale.y", to.y);
                    Set(scale, a.obj.transform, avatar, typeof(Transform), "m_LocalScale.z", to.z);
                    actions.AddAnimationClip(scale);
                    break;
                case OutfitActionType.BlendShape:
                    if (!string.IsNullOrEmpty(a.name)) actions.AddBlendshape(a.name, a.value, a.renderer);
                    break;
                case OutfitActionType.AnimationClip:
                    if (a.clip != null) actions.AddAnimationClip(a.clip);
                    break;
                case OutfitActionType.FxFloat:
                    if (!string.IsNullOrEmpty(a.name)) actions.AddAap(a.name, a.value);
                    break;
            }
        }
    }
}
