using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Put on an outfit prefab root. At avatar build, every mesh under it gets a VRCFury Toggle
    /// at "menuRoot/OutfitName/PieceName".
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Outfit Toggle Creator")]
    public class AutoOutfitToggles : MonoBehaviour, IEditorOnly {
        [Tooltip("Top-level menu folder")]
        public string menuRoot = "Outfits";
        [Tooltip("Menu name for this outfit. Empty = this GameObject's name.")]
        public string outfitName = "";
        [Tooltip("Remember toggle state between avatar loads")]
        public bool saved = true;
        [Tooltip("Also create toggles for meshes on disabled GameObjects")]
        public bool includeInactive = true;
        [Tooltip("Meshes whose GameObject name contains any of these (case-insensitive) are skipped")]
        public string[] excludeNames = new string[0];
    }
}
