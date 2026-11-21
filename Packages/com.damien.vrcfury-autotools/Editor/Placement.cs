using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace VRCFuryAutoTools {
    /// <summary>The line at the top of every inspector that says where the component belongs, with a warning when it is elsewhere.</summary>
    internal static class Placement {
        private enum Where { AvatarRoot, OutfitRoot, AvatarOrOutfitRoot, AvatarRootOrBone }

        // a subclass (each Poiyomi feature) uses its base class's entry
        private static readonly Dictionary<Type, Where> Places = new Dictionary<Type, Where> {
            { typeof(AutoOutfitToggles), Where.OutfitRoot },
            { typeof(OutfitFitter), Where.OutfitRoot },
            { typeof(ZeroWeightBoneRemover), Where.AvatarOrOutfitRoot },
            { typeof(MeshBoneStripper), Where.AvatarOrOutfitRoot },
            { typeof(AutoPhysBoneColliders), Where.AvatarRootOrBone },
            { typeof(AutoQuestCopy), Where.AvatarRoot },
            { typeof(AutoDigitigradeLegs), Where.AvatarRoot },
            { typeof(MeshDecimator), Where.AvatarRoot },
            { typeof(SkinnedMeshMerger), Where.AvatarRoot },
            { typeof(PoiyomiFeature), Where.AvatarRoot },
        };

        public static void Draw(Object target) {
            if (!(target is Component c)) return;
            var type = c.GetType();
            while (type != null && !Places.ContainsKey(type)) type = type.BaseType;
            if (type == null) return;
            var onAvatarRoot = c.GetComponent<VRC_AvatarDescriptor>() != null;
            switch (Places[type]) {
                case Where.AvatarRoot:
                    Box("Put this on the avatar root (the object with the VRC Avatar Descriptor).",
                        onAvatarRoot ? null : "This object is not the avatar root.");
                    break;
                case Where.OutfitRoot:
                    Box("Put this on the outfit root (the outfit's top object, with its meshes and armature under it).",
                        onAvatarRoot ? "This object is the avatar root, not an outfit." : null);
                    break;
                case Where.AvatarOrOutfitRoot:
                    Box("Put this on the avatar root or an outfit root.", null);
                    break;
                case Where.AvatarRootOrBone:
                    Box("Put this on the avatar root for every humanoid bone, or on another bone (tail, ear, ...) for that bone and its children.", null);
                    break;
            }
        }

        private static void Box(string where, string problem) =>
            EditorGUILayout.HelpBox(problem == null ? where : $"{problem} {where}", problem == null ? MessageType.Info : MessageType.Warning);
    }
}
