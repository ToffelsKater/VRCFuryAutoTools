using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Put on an avatar. At avatar build its meshes get fewer triangles until the whole avatar is at about the target.
    /// Every reduced mesh keeps the same share of its triangles. The mesh files in the project are never changed.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Mesh Decimator")]
    public class MeshDecimator : MonoBehaviour, IEditorOnly {
        [Tooltip("The avatar's triangles after the build, about. VRChat PC ranks: Excellent 32000, Good to Poor 70000. "
            + "Quest: Excellent 7500, Good 10000, Medium 15000, Poor 20000")]
        public int targetTriangles = 70000;
        [Tooltip("Leave the meshes with visemes and eyelid blendshapes (from the avatar descriptor) as they are. "
            + "Reduced faces lose detail where expressions move")]
        public bool keepFace = true;
        [Tooltip("Meshes left as they are")]
        public Renderer[] exclude = new Renderer[0];
        [Tooltip("Keep open mesh edges (sleeves, collars, holes) in place, so the reduced mesh shows no gaps")]
        public bool preserveBorders = true;
        [Tooltip("Keep UV seams in place, so textures do not smear. Off reduces further")]
        public bool preserveUvSeams = true;
    }
}
