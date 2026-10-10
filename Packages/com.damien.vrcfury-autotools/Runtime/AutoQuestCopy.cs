using UnityEngine;
using VRC.SDKBase;

namespace VRCFuryAutoTools {
    /// <summary>
    /// Put on an avatar: a copy of it is made next to it and changed until the SDK lets it be uploaded for Quest/Android.
    /// The copy keeps the avatar's blueprint ID, so uploading it for Android adds the Quest version to the same avatar.
    /// </summary>
    [AddComponentMenu("VRCFury Auto Tools/Automatic Quest Copy Creator")]
    public class AutoQuestCopy : MonoBehaviour, IEditorOnly {
        [Tooltip("Textures Android/iOS would import bigger than this are copied, and the copy is capped to this size. The original textures are never changed")]
        public int maxTextureSize = 1024;
        [Tooltip("Objects of the avatar left out of the copy, with all their children (regenerate to apply)")]
        public GameObject[] blacklist = new GameObject[0];
        [Tooltip("The copy this component made. Regenerating replaces it, removing the component deletes it")]
        public GameObject questCopy;
        [Tooltip("Folder with the copy's materials and textures. Removing the component moves it to the trash")]
        public Object copiesFolder;
    }
}
