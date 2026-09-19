using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Put on an avatar root or outfit root. At avatar build (after VRCFury), bones under this object
    /// that no mesh in the avatar weights to are deleted.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Zero Weight Bone Remover")]
    public class ZeroWeightBoneRemover : MonoBehaviour, IEditorOnly {
        [Tooltip("Weights at or below this count as zero")]
        public float weightThreshold = 0.0001f;
        [Tooltip("Bones whose name contains any of these (case-insensitive) are never removed")]
        public string[] keepNames = new string[0];
        [Tooltip("Log every removed bone at build")]
        public bool logRemoved = true;
    }
}
