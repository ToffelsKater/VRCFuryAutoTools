# VRCFury Auto Tools

Four small helpers for [VRCFury](https://vrcfury.com) that take care of boring avatar chores for you.
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

### Automatic PhysBone Collider Creator
Put this on your avatar and every humanoid bone gets a PhysBone collider sized to the bone's length and the mesh
around it. Put it on any other bone, like a tail or an ear, and that bone and all its children get one. The
colliders are created right when you add the component, not at upload, so your PhysBones and other tools can
use them straight away. Change the radius scale and hit **Regenerate colliders** to tune the fit.

## Install

1. Open the [landing page](https://toffelskater.github.io/VRCFuryAutoTools/) and click **Add to VCC**
   (or add `https://toffelskater.github.io/VRCFuryAutoTools/index.json` under VCC Settings → Packages → Add Repository).
2. Add **VRCFury Auto Tools** to your avatar project. VRCFury needs to be installed too.
3. In Unity, choose **Add Component → VRCFury Auto Tools** and pick the tool you want.

## Good to know

* Works with the VRChat Avatars SDK 3.7 or newer and Unity 2022.3.
* This is an unofficial add-on and isn't made by VRChat or VRCFury.
* It only uses the parts of VRCFury that are open for add-ons. If you plan to sell or share it, take a look at VRCFury's license first.
