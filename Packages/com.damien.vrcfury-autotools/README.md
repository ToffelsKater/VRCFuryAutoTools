# VRCFury Auto Tools

Two small helpers for [VRCFury](https://vrcfury.com) that take care of boring avatar chores for you.
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

## Install

1. Open the [landing page](https://toffelskater.github.io/VRCFuryAutoTools/) and click **Add to VCC**
   (or add `https://toffelskater.github.io/VRCFuryAutoTools/index.json` under VCC Settings → Packages → Add Repository).
2. Add **VRCFury Auto Tools** to your avatar project. VRCFury needs to be installed too.
3. In Unity, choose **Add Component → VRCFury Auto Tools** and pick the tool you want.

## Good to know

* Works with the VRChat Avatars SDK 3.7 or newer and Unity 2022.3.
* This is an unofficial add-on and isn't made by VRChat or VRCFury.
* It only uses the parts of VRCFury that are open for add-ons. If you plan to sell or share it, take a look at VRCFury's license first.
