using UnityEngine;

namespace VRCFuryAutoTools {
    [AddComponentMenu("VRCFury Auto Tools/Poiyomi Contact Shadows")]
    public class PoiyomiContactShadows : PoiyomiFeature {
        [Tooltip("Shadow intensity at 100% on the radial (Poiyomi allows up to 1)")]
        public float maxIntensity = 1;
        [Range(0, 1), Tooltip("Radial position when the avatar loads")]
        public float defaultIntensity = 0.5f;
        public override string Label => "Contact Shadows";
        public override string ToggleProperty => "_ContactShadowsAnimationToggle";
        public override string EnableProperty => "_ContactShadowsEnabled";
        public override PoiyomiSlider[] Sliders => new[] {
            new PoiyomiSlider("Intensity", maxIntensity, defaultIntensity, "_ContactShadowsIntensity"),
        };
    }
}
