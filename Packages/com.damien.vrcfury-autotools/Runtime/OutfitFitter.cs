using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Put on an outfit made for another avatar. At avatar build (before VRCFury), its bones are moved onto the avatar's,
    /// its meshes are pushed out of the avatar's body and small gaps are tightened, then the edits painted in the
    /// inspector are added on top.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Outfit Fitter")]
    public class OutfitFitter : MonoBehaviour, IEditorOnly {
        [Tooltip("The avatar's body meshes the outfit is fitted against. Empty = every mesh on the avatar with \"Body\" in its name")]
        public SkinnedMeshRenderer[] bodies = new SkinnedMeshRenderer[0];
        [Tooltip("Space kept between the outfit and the body, in meters")]
        public float gap = 0.002f;
        [Tooltip("Gaps smaller than this are closed so the outfit follows the body, in meters. 0 = only push clipping parts out")]
        public float tightenBelow = 0.01f;
        [Tooltip("Multiplies how much wider or thinner the outfit gets from the size difference between the avatars")]
        public float widthScale = 1;
        [Tooltip("Smoothing passes on the automatic fit. More is softer, less follows the body closer")]
        [Range(0, 30)] public int smoothing = 8;
        [Tooltip("At the end, push anything still inside the body back out, painted edits included")]
        public bool fixClipping = true;
        [HideInInspector] public List<PaintedEdits> edits = new List<PaintedEdits>();

        /// <summary>Per-vertex paint for one mesh. Dropped when the mesh's vertex count changes.</summary>
        [Serializable]
        public class PaintedEdits {
            public SkinnedMeshRenderer renderer;
            public int vertexCount;
            public float[] offset; // meters along the body normal
            public float[] smooth; // 0..1
            public float[] tight;  // -1 loose .. 1 tight
        }
    }
}
