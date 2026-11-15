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
                    off.SetCurve(AnimationUtility.CalculateTransformPath(a.obj.transform, avatar), typeof(GameObject),
                        "m_IsActive", new AnimationCurve(new Keyframe(0, 0)));
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
