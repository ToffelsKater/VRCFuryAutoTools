using UnityEngine;

namespace VRCFuryAutoTools {
    [AddComponentMenu("VRCFury Auto Tools/Poiyomi LTCGI")]
    public class PoiyomiLTCGI : PoiyomiFeature {
        [Tooltip("Diffuse and specular multiplier at 100% on the radial (Poiyomi allows up to 2)")]
        public float maxIntensity = 2;
        [Range(0, 1), Tooltip("Radial position when the avatar loads")]
        public float defaultIntensity = 0.5f;
        public override string Label => "LTCGI";
        public override string ToggleProperty => "_LTCGI_AnimToggle";
        public override string EnableProperty => "_LTCGIEnabled";
        public override PoiyomiSlider[] Sliders => new[] {
            new PoiyomiSlider("Intensity", maxIntensity, defaultIntensity, "_LTCGI_DiffuseMultiplier", "_LTCGI_SpecularMultiplier"),
        };
    }
}
