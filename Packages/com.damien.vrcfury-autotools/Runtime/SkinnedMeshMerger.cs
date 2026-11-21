using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Put on an avatar root. At avatar build (after VRCFury), skinned meshes that are always shown and hidden together are
    /// merged into one, for a better skinned mesh and material slot rank. Meshes toggled on their own stay separate.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Skinned Mesh Merger")]
    public class SkinnedMeshMerger : MonoBehaviour, IEditorOnly {
        [Tooltip("Meshes left as they are")]
        public SkinnedMeshRenderer[] exclude = new SkinnedMeshRenderer[0];
        [Tooltip("Parts of different meshes that use the same material become one material slot. Transparent parts that share a "
            + "material can draw in a different order")]
        public bool mergeSameMaterials = true;
    }
}
