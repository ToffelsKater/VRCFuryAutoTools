using UnityEngine;

namespace VRCFuryAutoTools {
    [AddComponentMenu("VRCFury Auto Tools/Poiyomi Light Data")]
    public class PoiyomiLightData : PoiyomiFeature {
        [Range(0, 1), Tooltip("Min Brightness when the avatar loads")]
        public float defaultMinBrightness = 0.1f;
        [Range(0, 1), Tooltip("Grayscale Lighting when the avatar loads")]
        public float defaultGrayscale = 0f;
        public override string Label => "Light Data";
        public override string ToggleProperty => null; // radials only, no Toggle
        public override string EnableProperty => null;
        public override PoiyomiSlider[] Sliders => new[] {
            new PoiyomiSlider("Min Brightness", 1, defaultMinBrightness, "_LightingMinLightBrightness"),
            new PoiyomiSlider("Grayscale Lighting", 1, defaultGrayscale, "_LightingMonochromatic"),
        };
    }
}
