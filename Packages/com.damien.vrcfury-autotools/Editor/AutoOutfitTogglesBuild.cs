using System;
using System.Collections.Generic;
using System.Linq;
using com.vrcfury.api;
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
                    toggle.GetActions().AddTurnOn(renderer.gameObject);
                }
            }
            return true;
        }
    }
}
