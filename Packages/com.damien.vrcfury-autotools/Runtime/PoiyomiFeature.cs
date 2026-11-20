using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>One radial of a feature: 0-100% drives the properties from 0 to max.</summary>
    public struct PoiyomiSlider {
        public string label;
        public float max;
        public float defaultValue; // 0-1, radial position at avatar load
        public string[] properties;

        public PoiyomiSlider(string label, float max, float defaultValue, params string[] properties) {
            this.label = label;
            this.max = max;
            this.defaultValue = defaultValue;
            this.properties = properties;
        }
    }

    /// <summary>
    /// Base of the Poiyomi feature components. At avatar build it adds a menu folder "menuRoot/Feature" with a Toggle (if the
    /// feature has one) and radials that change the feature on every material of the avatar that has it.
    /// </summary>
    public abstract class PoiyomiFeature : MonoBehaviour, IEditorOnly {
        [Tooltip("Top-level menu folder. The controls go in a folder named after the feature inside it.")]
        public string menuRoot = "Shader";
        [Tooltip("Feature is switched on by default when the avatar loads. Unused by features without a Toggle.")]
        public bool defaultOn = false;
        [Tooltip("Remember the toggle and radial values between avatar loads")]
        public bool saved = true;

        public abstract string Label { get; }
        /// <summary>Poiyomi's animatable on/off property of the feature. Null when the feature has none: then there is no Toggle, only the radials.</summary>
        public abstract string ToggleProperty { get; }
        /// <summary>The feature's checkbox on the material. It must be on when the material is locked, or the feature's code is left out.</summary>
        public abstract string EnableProperty { get; }
        public abstract PoiyomiSlider[] Sliders { get; }
    }
}
