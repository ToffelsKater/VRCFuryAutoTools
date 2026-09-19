# VRCFury Auto Tools

Addon for [VRCFury](https://vrcfury.com): an automatic outfit toggle creator and a zero-weight bone remover.
Uses only VRCFury's public API (`com.vrcfury.api`); nothing in VRCFury is modified or redistributed.

Built from [vrchat-community/template-package](https://github.com/vrchat-community/template-package).
Requires Unity 2022.3, VRChat Avatars SDK >= 3.7 and VRCFury.

## Components (Add Component > VRCFury Auto Tools)

### Automatic Outfit Toggle Creator
Add to an outfit prefab root. At build, every mesh (Skinned/MeshRenderer) under it gets a VRCFury Toggle at
`Outfits/<OutfitName>/<MeshObjectName>`, e.g. `Outfits/OutfitXY/Top`, `Outfits/OutfitXY/Jeans`.
The inspector previews the paths. Runs before VRCFury (callback order -20000).

### Automatic Zero Weight Bone Remover
Add to an avatar root or outfit root. At build (order -9000, after VRCFury) bones under it that no mesh in the
avatar weights to are deleted and removed from each mesh's bone list, bindposes and weights (cloned mesh, original assets untouched).
Never removed: humanoid bones, anything referenced by a component, animated paths, whole PhysBone/DynamicBone chains,
objects with extra components, names listed in `keepNames`. Use "Preview bones to remove" first.

## Repo layout / publishing
* `Packages/com.damien.vrcfury-autotools` - the package.
* `Website/` - landing page, a [Scriban](https://github.com/scriban/scriban) template (`index.html`) filled with listing data by the `Build Repo Listing` action. `app.js` and `styles.css` are static.
* Setup: repo variable `PACKAGE_NAME` = `com.damien.vrcfury-autotools`; Settings > Pages > Source = GitHub Actions.
* Release: run the `Build Release` action (version comes from the package's `package.json`).
* First open the repo in Unity once and commit the generated `.meta` files under `Packages/com.damien.vrcfury-autotools`.

## License note
VRCFury's license forbids commercial redistribution of patches/plugins that modify it. This package only calls the public API,
but read VRCFury's `LICENSE.md` before selling or distributing.
