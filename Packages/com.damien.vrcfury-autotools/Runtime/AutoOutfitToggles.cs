using System;
using System.Collections.Generic;
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
        [Tooltip("Outfit is switched on by default when the avatar loads")]
        public bool defaultOn = false;
        [Tooltip("Remember toggle state between avatar loads")]
        public bool saved = true;
        [Tooltip("Also create toggles for meshes on disabled GameObjects")]
        public bool includeInactive = true;
        [Tooltip("Meshes whose GameObject name contains any of these (case-insensitive) are skipped")]
        public string[] excludeNames = new string[0];
        [HideInInspector] // drawn per piece by the custom editor
        public List<OutfitPieceActions> pieceActions = new List<OutfitPieceActions>();
    }

    public enum OutfitActionType { TurnOn, TurnOff, BlendShape, AnimationClip, FxFloat }

    /// <summary>One extra action of a piece's toggle: applied while the toggle is on, back to the resting state when off.</summary>
    [Serializable]
    public class OutfitAction {
        public OutfitActionType type;
        public GameObject obj;        // TurnOn, TurnOff
        public string name = "";      // BlendShape, FxFloat
        public float value;           // BlendShape (0-100), FxFloat
        public Renderer renderer;     // BlendShape; none = every mesh that has it
        public AnimationClip clip;    // AnimationClip
    }

    [Serializable]
    public class OutfitPieceActions {
        public Renderer piece;
        public List<OutfitAction> actions = new List<OutfitAction>();
    }
}
