using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Avatars;
using VRC.SDKBase.Editor.BuildPipeline;
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

        /// lilToon's blend modes (Normal, Add, Screen, Multiply) as Toon Standard detail modes. It has no Screen, Additive is closest.
        private static readonly int[] DetailModes = { 0, 1, 1, 2 };

        /// <summary>
        /// The texture property of <paramref name="src"/> that it reads from UV1, with the Toon Standard detail mode that shows it
        /// the same, or null. Toon Standard reads its main texture from UV0 only: the detail texture is its one color slot with a
        /// UV choice (UV0 or UV1), so that is where this texture has to go.
        /// </summary>
        // ponytail: one slot, so only the first UV1 texture is kept, without its tint, mask or rotation, and UV2/UV3 have no slot at all.
        // Bake the textures into one on UV0 if that matters.
        private static (string property, int mode) Uv1Texture(Material src) {
            // Poiyomi's main texture. Multiplied over the white main texture it looks the same.
            if (src.HasProperty("_MainTexUV") && src.GetFloat("_MainTexUV") == 1) return ("_MainTex", 2);
            // lilToon's 2nd and 3rd main texture, blended over the main one
            foreach (var n in new[] { "2nd", "3rd" }) {
                var p = $"_Main{n}Tex";
                if (src.HasProperty(p) && src.GetTexture(p) != null && src.GetFloat($"_UseMain{n}Tex") != 0 && src.GetFloat(p + "_UVMode") == 1)
                    return (p, DetailModes[Mathf.Clamp((int)src.GetFloat(p + "BlendMode"), 0, 3)]);
            }
            return (null, 0);
        }

        /// Main texture, color and one texture on UV1 on a mobile shader. Everything else (normal maps, emission, transparency) is dropped.
        private static Material Convert(Material src, bool particle) {
            var shader = Shader.Find(particle ? "VRChat/Mobile/Particles/Additive" : "VRChat/Mobile/Toon Standard");
            if (shader == null) shader = Shader.Find("VRChat/Mobile/Toon Lit"); // SDKs before 3.8.1 have no Toon Standard
            var m = new Material(shader) { name = src.name, enableInstancing = true };
            var (detail, mode) = m.HasProperty("_DetailAlbedoMap") ? Uv1Texture(src) : (null, 0);
            if (detail != null) {
                m.SetTexture("_DetailAlbedoMap", src.GetTexture(detail));
                m.SetTextureOffset("_DetailAlbedoMap", src.GetTextureOffset(detail));
                m.SetTextureScale("_DetailAlbedoMap", src.GetTextureScale(detail));
                m.EnableKeyword("USE_DETAIL_MAPS");
                m.SetFloat("_DetailMode", mode);
                m.SetFloat("_DetailUV", 1);
            }
            if (detail != "_MainTex" && src.HasProperty("_MainTex")) {
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

        /// Holds one folder per component with its material and texture copies. Nothing outside it is ever deleted.
        private const string CopiesRoot = "Assets/VRCFuryAutoTools/Quest Copies";

        /// <summary>The component's folder for its copies, made (with a name no other avatar has) if it has none yet.</summary>
        private static string CopiesFolder(AutoQuestCopy c, GameObject root) {
            var path = AssetDatabase.GetAssetPath(c.copiesFolder);
            if (AssetDatabase.IsValidFolder(path)) return path;
            var parent = "Assets";
            foreach (var p in CopiesRoot.Split('/').Skip(1)) {
                if (!AssetDatabase.IsValidFolder($"{parent}/{p}")) AssetDatabase.CreateFolder(parent, p);
                parent += "/" + p;
            }
            var name = Path.GetFileName(AssetDatabase.GenerateUniqueAssetPath($"{CopiesRoot}/{FileName(root.name)}"));
            path = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder(CopiesRoot, name));
            c.copiesFolder = AssetDatabase.LoadAssetAtPath<Object>(path);
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

        /// The longer side of the texture as Android/iOS import it (the bigger of the two). 0 for what is no standalone texture
        /// file (one inside a model, a render texture): those can't be capped.
        private static int MobileSize(Texture t) {
            if (!(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) is TextureImporter importer) || !AssetDatabase.IsMainAsset(t)) return 0;
            importer.GetSourceTextureWidthAndHeight(out var w, out var h);
            return MobilePlatforms.Max(p => Mathf.Min(Mathf.Max(w, h), MaxSize(importer, p)));
        }

        /// <summary>
        /// Textures of the materials that Android or iOS would import bigger than <paramref name="size"/> gives for them get that
        /// smaller Android/iOS size. Never the original files: those are copied into the folder first and the materials use the copy.
        /// A copy of an earlier call just shrinks. <paramref name="used"/> are the file names no new copy may take.
        /// Returns how many textures were capped.
        /// </summary>
        private static int CapTextures(List<Material> materials, string folder, System.Func<Texture, int> size, HashSet<string> used) {
            var textures = materials.SelectMany(m => m.GetTexturePropertyNames().Select(n => m.GetTexture(n))).Where(t => t != null).Distinct();
            var copies = new Dictionary<Texture, string>();
            var caps = new Dictionary<string, int>(); // by file in the folder
            // not inside StartAssetEditing: there the copies are not always imported by the time their importers are needed below
            foreach (var t in textures) {
                var max = size(t);
                if (MobileSize(t) <= max) continue; // small enough as it is
                var path = AssetDatabase.GetAssetPath(t);
                if (path.StartsWith(folder + "/")) { // a copy already
                    caps[path] = max;
                    continue;
                }
                var name = Path.GetFileNameWithoutExtension(path);
                var ext = Path.GetExtension(path);
                var file = name + ext;
                for (var n = 2; !used.Add(file); n++) file = $"{name} {n}{ext}";
                var dest = $"{folder}/{file}";
                // ponytail: copied fresh on every regenerate (and imported twice) so edits to the original come along. Reuse the copy if that gets slow.
                AssetDatabase.DeleteAsset(dest);
                if (!AssetDatabase.CopyAsset(path, dest)) continue; // the copy keeps the original's import settings
                copies[t] = dest;
                caps[dest] = max;
            }

            var capped = 0;
            AssetDatabase.StartAssetEditing();
            try {
                foreach (var cap in caps) {
                    if (!(AssetImporter.GetAtPath(cap.Key) is TextureImporter importer)) continue;
                    capped++;
                    foreach (var platform in MobilePlatforms) {
                        var s = importer.GetPlatformTextureSettings(platform);
                        s.maxTextureSize = Mathf.Min(MaxSize(importer, platform), cap.Value); // never bigger than the original had it
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
            return capped;
        }

        /// What Android and iOS allow an avatar to be as download: the size of the asset bundle the SDK builds, checked when it uploads.
        private const long DownloadLimit = 10 * 1024 * 1024;

        /// Fitting never makes a texture smaller than this.
        private const int SmallestFit = 64;

        /// ponytail: guess of the bundle bytes one pixel less saves (ASTC 6x6 with mipmaps is 0.6 before the bundle's compression).
        /// Only decides how many textures shrink between two measurements: lower shrinks more at once, for fewer builds.
        private const float BytesPerPixel = 0.5f;

        /// <summary>
        /// For the avatar the SDK is about to build for Android/iOS: if it is a Quest copy and over the download limit, the biggest
        /// textures of its materials are halved until it fits. Measured with a build like the SDK's own (same options, same
        /// platform), so with the size the SDK is going to check.
        /// </summary>
        public static void Fit(GameObject avatar) {
            var target = EditorUserBuildSettings.activeBuildTarget;
            // not in play mode: tools run the SDK's build hooks there to test an avatar
            if (EditorApplication.isPlayingOrWillChangePlaymode || (target != BuildTarget.Android && target != BuildTarget.iOS)) return;
            // a Quest copy is known by its materials: they are in its folder of copies
            var folder = avatar.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null)
                .Select(m => AssetDatabase.GetAssetPath(m)).Where(p => p.StartsWith(CopiesRoot + "/"))
                .Select(p => p.Substring(0, p.LastIndexOf('/'))).FirstOrDefault();
            if (folder == null) return;

            const string output = "Temp/VRCFuryAutoTools";
            var prefab = $"{folder}/Size check.prefab";
            var build = new AssetBundleBuild { assetBundleName = "questcopy", assetNames = new[] { prefab } };
            long first = 0, bytes = 0;
            var halved = 0;
            try {
                PrefabUtility.SaveAsPrefabAsset(avatar, prefab);
                Directory.CreateDirectory(output);
                var used = new HashSet<string>(Directory.GetFiles(folder).Select(Path.GetFileName), System.StringComparer.OrdinalIgnoreCase);
                for (;;) {
                    if (BuildPipeline.BuildAssetBundles(output, new[] { build }, BuildAssetBundleOptions.None, target) == null) return; // the SDK's build reports why
                    bytes = new FileInfo($"{output}/{build.assetBundleName}").Length;
                    if (first == 0) first = bytes;
                    var over = bytes - DownloadLimit;
                    if (over <= 0) break;

                    // only what the bundle holds, and only through the copy's own materials (not menu icons)
                    var materials = AssetDatabase.GetDependencies(prefab).Where(p => p.StartsWith(folder + "/"))
                        .Select(p => AssetDatabase.LoadAssetAtPath<Material>(p)).Where(m => m != null).ToList();
                    var sizes = new Dictionary<Texture, int>();
                    foreach (var t in materials.SelectMany(m => m.GetTexturePropertyNames().Select(n => m.GetTexture(n))).Where(t => t != null)
                                 .Distinct().OrderByDescending(MobileSize)) {
                        var s = MobileSize(t);
                        if (s <= SmallestFit || over <= 0) break;
                        sizes[t] = Mathf.NextPowerOfTwo(s) / 2;
                        over -= (long)(0.75f * s * s * BytesPerPixel); // halving leaves a quarter of the pixels
                    }
                    var capped = CapTextures(materials, folder, t => sizes.TryGetValue(t, out var cap) ? cap : int.MaxValue, used);
                    if (capped == 0) { // nothing left to shrink (or a texture that can't be copied): stop instead of building forever
                        Debug.LogWarning($"[VRCFuryAutoTools] '{avatar.name}' is {bytes / 1048576f:0.00} MB, over the {DownloadLimit / 1048576} MB download limit, "
                            + $"and its textures can't shrink further (smallest: {SmallestFit}). Remove meshes or blendshapes, or blacklist objects.", avatar);
                        return;
                    }
                    halved += capped;
                }
            } finally {
                AssetDatabase.DeleteAsset(prefab);
            }
            if (halved > 0)
                Debug.Log($"[VRCFuryAutoTools] '{avatar.name}' was {first / 1048576f:0.00} MB, over the {DownloadLimit / 1048576} MB download limit: "
                    + $"halved its biggest textures {halved} times, now {bytes / 1048576f:0.00} MB.", avatar);
        }

        /// <summary>
        /// What a blacklist entry is in <paramref name="copy"/>: a GameObject (or Transform) gives the object at the same place, found by
        /// sibling index since names can repeat; any other component gives the same component there, found by its index among the
        /// components of its type on that object. Null for the avatar root object itself and for anything outside the avatar.
        /// </summary>
        private static Object Counterpart(Object entry, Transform root, Transform copy) {
            var k = entry as Component;
            var wholeObject = k == null || k is Transform;
            var original = entry is GameObject g ? g.transform : k != null ? k.transform : null;
            if (original == null || (wholeObject && original == root)) return null;
            var path = new Stack<int>();
            for (var t = original; t != root; t = t.parent) {
                if (t == null) return null;
                path.Push(t.GetSiblingIndex());
            }
            foreach (var i in path) copy = copy.GetChild(i);
            if (wholeObject) return copy.gameObject;
            var type = k.GetType();
            return copy.GetComponents(type)[System.Array.IndexOf(original.GetComponents(type), k)];
        }

        /// <summary>
        /// Deletes the copy, but never the avatar <paramref name="owner"/> (the component's object, null once deleted) sits on,
        /// in case that got dragged into the field. With a <paramref name="folder"/> its material and texture copies go to the
        /// trash too (only a folder inside <see cref="CopiesRoot"/>). Undo brings the copy back, but not those files.
        /// </summary>
        public static void Remove(GameObject copy, Transform owner, Object folder = null) {
            if (copy != null && (owner == null || !owner.IsChildOf(copy.transform))) Undo.DestroyObjectImmediate(copy);
            var path = folder != null ? AssetDatabase.GetAssetPath(folder) : "";
            if (path.StartsWith(CopiesRoot + "/") && AssetDatabase.IsValidFolder(path)) AssetDatabase.MoveAssetToTrash(path);
        }

        public static void Generate(AutoQuestCopy c) {
            var root = ZeroWeightBoneAnalysis.AvatarRoot(c);
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(c, "Create Quest copy");
            Remove(c.questCopy, c.transform); // the folder stays: its files are overwritten in place below

            var copy = Object.Instantiate(root, root.transform.parent);
            Undo.RegisterCreatedObjectUndo(copy, "Create Quest copy");
            copy.name = root.name + " (Quest)";
            copy.transform.SetSiblingIndex(root.transform.GetSiblingIndex() + 1);
            copy.transform.position += root.transform.right; // beside the original instead of inside it

            // all looked up before anything in the copy is deleted, deleting shifts sibling and component indices
            var blacklisted = c.blacklist.Select(o => Counterpart(o, root.transform, copy.transform)).Where(o => o != null).ToList();
            foreach (var k in copy.GetComponentsInChildren<AutoQuestCopy>(true)) Undo.DestroyObjectImmediate(k);
            foreach (var o in blacklisted) if (o != null) Undo.DestroyObjectImmediate(o); // null: went with a blacklisted parent

            // joints, spatial audio and flare layers go before the rigidbody, audio source and camera they require
            var removed = copy.GetComponentsInChildren<Component>(true).Where(k => k != null && PcOnly(k))
                .OrderBy(k => k is Rigidbody || k is AudioSource || k is Camera).ToList();
            foreach (var k in removed) Undo.DestroyObjectImmediate(k);

            // ponytail: animations of these constraints are not rebound, the clips are shared with the PC avatar. Duplicate the clips if that matters.
            AvatarDynamicsSetup.DoConvertUnityConstraints(copy.GetComponentsInChildren<IConstraint>(true), null, false);

            // every material reference gets its own copy, so editing the Quest materials never touches the PC ones.
            // All references, not just renderers, so VRCFury material swaps get the mobile version too.
            var folder = CopiesFolder(c, root);
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

            var max = Mathf.ClosestPowerOfTwo(Mathf.Clamp(c.maxTextureSize, 32, 8192));
            var capped = CapTextures(copies.Values.Distinct().ToList(), folder, t => max, new HashSet<string>());

            c.questCopy = copy;
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            Undo.CollapseUndoOperations(group);
            Debug.Log($"[VRCFuryAutoTools] Made '{copy.name}': {copies.Count} materials copied to {folder} ({converted} switched to mobile shaders), "
                + $"{blacklisted.Count} blacklisted objects/components left out, {removed.Count} PC-only components removed, "
                + $"{capped} textures copied and capped for Android/iOS. Switch the SDK to Android and upload it.", copy);
        }
    }

    /// <summary>Runs after everything else in the SDK's avatar build, so on the avatar as it gets uploaded.</summary>
    internal class AutoQuestCopyFitHook : IVRCSDKPreprocessAvatarCallback {
        public int callbackOrder => int.MaxValue;

        public bool OnPreprocessAvatar(GameObject avatar) {
            AutoQuestCopyGenerator.Fit(avatar);
            return true;
        }
    }
}
