# VRCFury Auto Tools

Nine small helpers for [VRCFury](https://vrcfury.com) that take care of boring avatar chores for you.
Add a component, upload your avatar, and it's done.

## What's inside

### Automatic Outfit Toggle Creator
Setting up a menu toggle for every piece of clothing gets old fast. Put this on an outfit and it makes a toggle
for each piece automatically, neatly sorted in your menu like:

```
Outfits
└── OutfitXY
    ├── Top
    └── Jeans
```

### Automatic Zero Weight Bone Remover
Some bones in an avatar or outfit don't actually move anything. They just add to your bone count and hurt your
performance rating. Put this on your avatar or an outfit and those unused bones are removed when you upload.
Bones that are still needed (like physbones, constraints or animated parts) are left alone, and your original
files are never changed. You can preview what would be removed before you upload.

### Automatic Mesh Bone Stripper
Only using a few meshes from an outfit or another avatar base? Add them to the list and every bone in their
armature that they aren't weighted to is removed when you upload. Hips, Spine, Chest, Upper Chest, Neck and Head
are kept by default, and each one can be switched off. This tool is strict: a mesh that isn't in the list doesn't
protect its bones, so use the preview to check before you upload.

### Automatic Mesh Decimator
Too many triangles for the rank you want? Put this on your avatar, set a target triangle count, and when you upload,
every mesh gets fewer triangles until the whole avatar is at about that target. Each reduced mesh keeps the same share
of its triangles, and the inspector shows how big that share is before you upload. The face mesh (the one with your
visemes and eyelid blendshapes) is left alone by default, because a reduced face loses detail where expressions move.
You can leave out other meshes as well. Open mesh edges and UV seams stay in place so you don't get gaps or smeared
textures, and you can switch that off to reduce further. Your mesh files are never changed: the reduced meshes only
exist in the uploaded avatar.

### Automatic PhysBone Collider Creator
Put this on your avatar and every humanoid bone gets a PhysBone collider sized to the bone's length and the mesh
around it. Put it on any other bone, like a tail or an ear, and that bone and all its children get one. The
colliders are created right when you add the component, not at upload, so your PhysBones and other tools can
use them straight away. Fingers, toes and eyes can be left out. Change the radius scale and hit **Regenerate colliders** to tune the fit.
Regenerating replaces the old colliders, and removing the component deletes them again.

### Automatic Quest Copy Creator
Put this on your avatar and a copy of it appears next to it, changed just enough for the SDK to let you upload it
for Quest and Android. Every material is copied into its own folder in `Assets/VRCFuryAutoTools/Quest Copies`, so your PC
materials are never changed, and copies with PC shaders get a VRChat mobile shader that keeps the main texture and color.
A texture the PC material reads from another UV map (Poiyomi's main texture, lilToon's 2nd or 3rd texture) becomes the
detail texture, and if that UV map is UV2 or UV3 the copy gets its own mesh with it moved to UV1, the only other one mobile shaders read.
The parts Quest doesn't allow (lights, cloth, audio, physics colliders and rigidbodies, cameras) are removed, Unity
constraints become VRChat constraints, and textures bigger than 1024 (adjustable) are copied into the same folder with
the copy capped for Android and iOS. Your original materials and textures are never changed. If the copy is still over
the 10 MB download limit when you upload it, its biggest textures are halved (again only in copies) until it fits. The copy keeps your
avatar's blueprint ID, so upload the PC version first, then switch the SDK to Android and upload the copy.
**Regenerate Quest copy** replaces it, and removing the component deletes it again, with its folder of material and
texture copies (moved to the trash). Anything you add to the **Blacklist** is left out of the copy: an object together with
all its children, or a single component (drag in its header) while its object stays. Tick **Reduce Triangles** to put a Mesh Decimator on the copy too, with a Quest target (20,000 by default, the Poor limit). Otherwise it isn't optimized: expect a Very Poor rank until you trim it down.

### Automatic Outfit Fitter
Got an outfit that was made for a different avatar? Put this on the outfit and when you upload, its bones are moved
onto your avatar's, anything that pokes into your body is pushed out, and small gaps are closed so tight clothes stay
tight. Loose parts like skirts keep their shape. You don't need the avatar the outfit was made for, only your own.
Click **Edit fit** to see the result in the Scene view and paint fixes on top: **Push / Pull** moves the outfit away
from or closer to the body, **Smooth** evens out dents, and **Tight / Loose** tells the fitter which parts should hug
the body and which should keep their shape. Painting is mirrored left and right unless you switch it off. If your
avatar is a lot slimmer than the one the outfit was made for, paint the baggy parts tight. Your paint is saved on the
component and your original meshes are never changed. Link the outfit with a VRCFury Armature Link as usual, the
fitter runs before it. The outfit's bone names need to match your avatar's (differences in case, `_`, `.` and
left/right spelling are fine).

### Automatic Digitigrade Legs
Many avatars with digitigrade legs have two pairs of legs in the armature: the humanoid legs that VRChat's full body
IK moves, and a fake pair the mesh is weighted to. Put this on your avatar and the fake legs are hooked up to the real
ones when you upload. With **Final IK** each fake leg gets a FABRIK solver that plants its foot exactly on the real
foot, plus optional hinge limits so the knee and hock only bend the way they are modelled. Don't own Final IK? Pick
**Rotation Constraints** and the legs follow with VRChat rotation constraints only, with the hock kept parallel to the
thigh. With Final IK, fake legs built like normal ones (upper leg, lower leg, foot) get a LimbIK solver instead
of FABRIK, so their knees aim like the real ones. The fake legs are found automatically and the component shows which
bones it picked. If it got them wrong, drag in the upper leg and the foot yourself. Final IK only runs on PC, so Quest
and Android uploads use the constraints by themselves. Your original avatar is never changed.

### Poiyomi Shader Feature Toggles
Four components, one per Poiyomi feature: **Poiyomi LTCGI**, **Poiyomi Contact Shadows**, **Poiyomi SSAO** and
**Poiyomi Light Data**. Put one on your avatar and at upload it adds a menu folder `Shader/<Feature>` with a **Toggle**
and an **Intensity** radial. Light Data has no Toggle, only **Min Brightness** and **Grayscale Lighting** radials. They change the
feature on every material of the avatar that has it. Set the highest intensity and the starting values on the
component. Poiyomi bakes properties in when it locks a material, so the component warns you about every property that
isn't marked **Animated (when locked)** and every material that has the feature switched off.

### Performance Rank After Build
**Tools > VRCFury Auto Tools > Performance Rank After Build**. The SDK ranks the avatar as it is in the scene, before
VRCFury, NDMF and the other build tools change it. This window copies the avatar, runs every avatar build hook on the
copy like an upload does, and shows the rank of the result for PC and Quest side by side, then deletes the copy. With an
Automatic Quest Copy Creator on the avatar, the Quest column uses its copy. The hooks run for the current build target,
so steps a tool only does for the other platform are left out.

## Install

1. Open the [landing page](https://toffelskater.github.io/VRCFuryAutoTools/) and click **Add to VCC**
   (or add `https://toffelskater.github.io/VRCFuryAutoTools/index.json` under VCC Settings → Packages → Add Repository).
2. Add **VRCFury Auto Tools** to your avatar project. VRCFury needs to be installed too.
3. In Unity, choose **Add Component → VRCFury Auto Tools** and pick the tool you want.

## Good to know

* Works with the VRChat Avatars SDK 3.7 or newer and Unity 2022.3.
* The Mesh Decimator uses [UnityMeshSimplifier](https://github.com/Whinarn/UnityMeshSimplifier) by Mattias Edlund (MIT license), included in the package.
* This is an unofficial add-on and isn't made by VRChat or VRCFury.
* It only uses the parts of VRCFury that are open for add-ons. If you plan to sell or share it, take a look at VRCFury's license first.
