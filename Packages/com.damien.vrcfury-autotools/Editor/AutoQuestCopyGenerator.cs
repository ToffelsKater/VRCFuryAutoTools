using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    public static class AutoQuestCopyGenerator {
        /// Runs right when the component is added, like the collider creator, so the copy is there to upload.
        [InitializeOnLoadMethod]
        private static void Init() {
            ObjectFactory.componentWasAdded += c => { if (c is AutoQuestCopy q) Generate(q); };
        }

        private static bool Mobile(Material m) =>
            m == null || m.shader == null || VRC.SDKBase.Validation.AvatarValidation.ShaderWhiteList.Contains(m.shader.name);

        /// The PC-only entries of the SDK's avatar component whitelist: the SDK refuses a mobile upload while they exist.
        private static bool PcOnly(Component k) {
            var name = k.GetType().Name;
            return k is Cloth || k is Light || k is Collider || k is Rigidbody || k is Joint || k is Camera || k is FlareLayer
                || k is AudioSource || name == "ONSPAudioSource" || name == "VRCSpatialAudioSource"
                || (k.GetType().Namespace ?? "").StartsWith("RootMotion.FinalIK");
        }

        /// Main texture and color on a mobile shader. Everything else (normal maps, emission, transparency) is dropped.
        private static Material Convert(Material src, bool particle) {
            var shader = Shader.Find(particle ? "VRChat/Mobile/Particles/Additive" : "VRChat/Mobile/Toon Standard");
            if (shader == null) shader = Shader.Find("VRChat/Mobile/Toon Lit"); // SDKs before 3.8.1 have no Toon Standard
            var m = new Material(shader) { name = src.name, enableInstancing = true };
            if (src.HasProperty("_MainTex")) {
                m.mainTexture = src.mainTexture;
                m.mainTextureOffset = src.mainTextureOffset;
                m.mainTextureScale = src.mainTextureScale;
            }
            if (src.HasProperty("_Color") && m.HasProperty("_Color")) m.color = src.color;
            return m;
        }

        private static string FileName(string s) {
            var name = string.Join("_", s.Split(Path.GetInvalidFileNameChars())).Trim();
            return name == "" ? "Unnamed" : name;
        }

        /// Assets/parts..., creating the folders that are missing.
        private static string Folder(params string[] parts) {
            var path = "Assets";
            foreach (var p in parts) {
                if (!AssetDatabase.IsValidFolder($"{path}/{p}")) AssetDatabase.CreateFolder(path, p);
                path += "/" + p;
            }
            return path;
        }

        /// <summary>Saves the material as an asset. On regenerate the file from last time is overwritten in place, so its GUID stays
        /// and an undone regenerate still finds its materials.</summary>
        private static Material Save(Material m, string folder, HashSet<string> used) {
            var name = FileName(m.name);
            for (var n = 2; !used.Add(name); n++) name = $"{FileName(m.name)} {n}"; // two source materials with one name
            m.name = name;
            var path = $"{folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null) {
                AssetDatabase.CreateAsset(m, path);
                return m;
            }
            EditorUtility.CopySerialized(m, existing);
            Object.DestroyImmediate(m);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }

        private static readonly string[] MobilePlatforms = { "Android", "iPhone" };

        /// The max size a platform imports the texture at: its own override, else the default.
        private static int MaxSize(TextureImporter importer, string platform) {
            var s = importer.GetPlatformTextureSettings(platform);
            return s.overridden ? s.maxTextureSize : importer.maxTextureSize;
        }

        /// <summary>
        /// Textures of the materials that Android or iOS would import bigger than <paramref name="size"/> are copied into
        /// the folder, the copy gets the smaller Android/iOS size and the materials use the copy. The originals are never changed.
        /// Returns how many were copied.
        /// </summary>
        private static int CapTextures(List<Material> materials, string folder, int size) {
            size = Mathf.ClosestPowerOfTwo(Mathf.Clamp(size, 32, 8192));
            var textures = materials.SelectMany(m => m.GetTexturePropertyNames().Select(n => m.GetTexture(n))).Where(t => t != null).Distinct();
            var copies = new Dictionary<Texture, string>();
            var used = new HashSet<string>();
            AssetDatabase.StartAssetEditing();
            try {
                foreach (var t in textures) {
                    var path = AssetDatabase.GetAssetPath(t);
                    // only standalone texture files (not ones inside a model), and never a file of this folder: it gets deleted below
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || !AssetDatabase.IsMainAsset(t) || path.StartsWith(folder + "/")) continue;
                    importer.GetSourceTextureWidthAndHeight(out var w, out var h);
                    if (MobilePlatforms.All(p => Mathf.Min(Mathf.Max(w, h), MaxSize(importer, p)) <= size)) continue; // small enough as it is
                    var name = Path.GetFileNameWithoutExtension(path);
                    var ext = Path.GetExtension(path);
                    var file = name + ext;
                    for (var n = 2; !used.Add(file); n++) file = $"{name} {n}{ext}";
                    var dest = $"{folder}/{file}";
                    // ponytail: copied fresh on every regenerate (and imported twice) so edits to the original come along. Reuse the copy if that gets slow.
                    AssetDatabase.DeleteAsset(dest);
                    if (AssetDatabase.CopyAsset(path, dest)) copies[t] = dest; // the copy keeps the original's import settings
                }
            } finally {
                AssetDatabase.StopAssetEditing(); // imports the copies, so their importers exist below
            }

            AssetDatabase.StartAssetEditing();
            try {
                foreach (var dest in copies.Values) {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(dest);
                    foreach (var platform in MobilePlatforms) {
                        var s = importer.GetPlatformTextureSettings(platform);
                        s.maxTextureSize = Mathf.Min(MaxSize(importer, platform), size); // never bigger than the original had it
                        s.overridden = true;
                        importer.SetPlatformTextureSettings(s);
                    }
                    importer.SaveAndReimport();
                }
            } finally {
                AssetDatabase.StopAssetEditing();
            }

            var loaded = copies.ToDictionary(kv => kv.Key, kv => AssetDatabase.LoadAssetAtPath<Texture>(kv.Value));
            foreach (var m in materials) {
                foreach (var n in m.GetTexturePropertyNames())
                    if (m.GetTexture(n) is Texture t && loaded.TryGetValue(t, out var copy)) m.SetTexture(n, copy);
                EditorUtility.SetDirty(m);
                AssetDatabase.SaveAssetIfDirty(m);
            }
            return copies.Count;
        }

        /// <summary>
        /// Deletes the copy, but never the avatar <paramref name="owner"/> (the component's object, null once deleted) sits on,
        /// in case that got dragged into the field. The material and texture copies stay, so undo can bring the copy back intact.
        /// </summary>
        public static void Remove(GameObject copy, Transform owner) {
            if (copy != null && (owner == null || !owner.IsChildOf(copy.transform))) Undo.DestroyObjectImmediate(copy);
        }

        public static void Generate(AutoQuestCopy c) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c);
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(c, "Create Quest copy");
            Remove(c.questCopy, c.transform);

            var copy = Object.Instantiate(root, root.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Create Quest copy");
            copy.name = root.name + " (Quest)";
            copy.transform.SetSiblingIndex(root.transform.GetSiblingIndex() + 1);
            copy.transform.position += root.transform.right; // beside the original instead of inside it
            foreach (var k in copy.GetComponentsInChildren<AutoQuestCopy>(true)) Undo.DestroyObjectImmediate(k);

            // joints, spatial audio and flare layers go before the rigidbody, audio source and camera they require
            var removed = copy.GetComponentsInChildren<Component>(true).Where(k => k != null && PcOnly(k))
                .OrderBy(k => k is Rigidbody || k is AudioSource || k is Camera).ToList();
            foreach (var k in removed) Undo.DestroyObjectImmediate(k);

            // ponytail: animations of these constraints are not rebound, the clips are shared with the PC avatar. Duplicate the clips if that matters.
            AvatarDynamicsSetup.DoConvertUnityConstraints(copy.GetComponentsInChildren<IConstraint>(true), null, false);

            // every material reference gets its own copy, so editing the Quest materials never touches the PC ones.
            // All references, not just renderers, so VRCFury material swaps get the mobile version too.
            // ponytail: the folder is named after the avatar, so two avatars with the same name share it and overwrite each other's copies.
            var folder = Folder("VRCFuryAutoTools", "Quest Copies", FileName(root.name));
            var copies = new Dictionary<Material, Material>();
            var used = new HashSet<string>();
            var converted = 0;
            foreach (var k in copy.GetComponentsInChildren<Component>(true)) {
                if (k == null || k is Transform) continue;
                var particle = k is ParticleSystemRenderer || k is TrailRenderer || k is LineRenderer;
                var so = new SerializedObject(k);
                var it = so.GetIterator();
                while (it.Next(true)) {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || !(it.objectReferenceValue is Material m)) continue;
                    if (!copies.TryGetValue(m, out var q)) {
                        var mobile = Mobile(m);
                        if (!mobile) converted++;
                        copies[m] = q = Save(mobile ? new Material(m) { name = m.name } : Convert(m, particle), folder, used);
                    }
                    it.objectReferenceValue = q;
                }
                so.ApplyModifiedProperties();
            }

            var capped = CapTextures(copies.Values.Distinct().ToList(), folder, c.maxTextureSize);

            c.questCopy = copy;
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            Undo.CollapseUndoOperations(group);
            Debug.Log($"[VRCFuryAutoTools] Made '{copy.name}': {copies.Count} materials copied to {folder} ({converted} switched to mobile shaders), "
                + $"{removed.Count} PC-only components removed, "
                + $"{capped} textures copied and capped for Android/iOS. Switch the SDK to Android and upload it.", copy);
        }
    }
}
