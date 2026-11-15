using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    public enum DigitigradeLegMode { FinalIK, RotationConstraints }

    /// <summary>
    /// Put on a humanoid avatar that has a second, fake pair of legs the mesh is weighted to. At avatar build
    /// (after VRCFury), the fake legs are made to follow the humanoid legs VRChat's IK drives.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Digitigrade Legs")]
    public class AutoDigitigradeLegs : MonoBehaviour, IEditorOnly {
        [Tooltip("Final IK: the fake foot lands exactly on the real foot. Needs Final IK in the project and only works on PC.\n"
            + "Rotation Constraints: VRChat constraints only, works everywhere, but the foot can drift a little from the real one.")]
        public DigitigradeLegMode mode = DigitigradeLegMode.FinalIK;
        [Tooltip("Final IK only: hinge limits so the knee and hock only bend the way they are modelled and never fold backwards")]
        public bool rotationLimits = true;

        [Header("Fake legs. Leave empty to find them automatically")]
        [Tooltip("First bone of the fake left leg")]
        public Transform leftUpperLeg;
        [Tooltip("Fake bone that should sit on the real left foot. Everything between it and the upper leg is the leg.")]
        public Transform leftFoot;
        [Tooltip("First bone of the fake right leg")]
        public Transform rightUpperLeg;
        [Tooltip("Fake bone that should sit on the real right foot. Everything between it and the upper leg is the leg.")]
        public Transform rightFoot;
    }
}
