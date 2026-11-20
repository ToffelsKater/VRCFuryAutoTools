using UnityEngine;

namespace VRCFuryAutoTools {
    [AddComponentMenu("VRCFury Auto Tools/Poiyomi SSAO")]
    public class PoiyomiSSAO : PoiyomiFeature {
        [Tooltip("AO intensity at 100% on the radial (Poiyomi allows up to 5)")]
        public float maxIntensity = 2;
        [Range(0, 1), Tooltip("Radial position when the avatar loads")]
        public float defaultIntensity = 0.5f;
        public override string Label => "SSAO";
        public override string ToggleProperty => "_SSAOAnimationToggle";
        public override string EnableProperty => "_SSAOEnabled";
        public override PoiyomiSlider[] Sliders => new[] {
            new PoiyomiSlider("Intensity", maxIntensity, defaultIntensity, "_SSAOIntensity"),
        };
    }
}
