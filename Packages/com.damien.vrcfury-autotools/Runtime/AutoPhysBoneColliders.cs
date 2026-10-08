using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Put on an avatar root: every humanoid bone gets a PhysBone collider sized to the bone's length and the
    /// mesh it moves. Put on any other bone (tail, ear, ...): that bone and all its children get one.
    /// The colliders are created as soon as the component is added, so physbones and other scripts can reference them.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic PhysBone Collider Creator")]
    public class AutoPhysBoneColliders : MonoBehaviour, IEditorOnly {
        [Tooltip("Multiplies every collider radius. Lower it if colliders stick out of the mesh, then regenerate")]
        public float radiusScale = 1f;
        [Tooltip("Colliders this component created. Regenerating updates them in place")]
        public List<VRCPhysBoneColliderBase> colliders = new List<VRCPhysBoneColliderBase>();
    }
}
