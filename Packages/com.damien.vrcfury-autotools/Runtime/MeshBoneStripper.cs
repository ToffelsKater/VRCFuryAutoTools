using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// List meshes. At avatar build (before VRCFury), every bone those meshes list but none of them
    /// is weighted to is deleted, except the spine bones ticked below.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Mesh Bone Stripper")]
    public class MeshBoneStripper : MonoBehaviour, IEditorOnly {
        [Tooltip("Only bones these meshes are weighted to survive")]
        public SkinnedMeshRenderer[] meshes = new SkinnedMeshRenderer[0];
        [Tooltip("Weights at or below this count as zero")]
        public float weightThreshold = 0.0001f;
        [Header("Keep even without weights")]
        public bool keepHips = true;
        public bool keepSpine = true;
        public bool keepChest = true;
        public bool keepUpperChest = true;
        public bool keepNeck = true;
        public bool keepHead = true;
        [Tooltip("Bones whose name contains any of these (case-insensitive) are never removed")]
        public string[] keepNames = new string[0];
    }
}
