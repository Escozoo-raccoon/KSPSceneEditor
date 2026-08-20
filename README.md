# KSP Scene Editor

**Version 1.0.0**

KSP Scene Editor is an in-game editor for customizing and saving the main-menu scenes of **Kerbal Space Program 1**.

## Download & Links

- **SpaceDock:** https://spacedock.info/mod/4495/KSP%20Scene%20Editor
- **Source code:** https://github.com/Escozoo-raccoon/KSPSceneEditor
- **Forum:** coming soon

## Features

- Edit the different native main-menu states independently.
- Directly select and move scene elements.
- Position, depth, rotation and scale controls.
- Move Kerbals while preserving their live KSP animations.
- Edit the stock logo and compatible visual elements.
- Add free images and custom text.
- Edit stock menu text where supported.
- Add `.craft` files as visual scene objects.
- Add celestial bodies detected from KSP / installed planet packs.
- Add and configure lights.
- Apply user-provided skyboxes.
- Save multiple compositions per scene/state.
- Apply, rename and delete saved compositions.
- Keep the selected composition after returning from gameplay.
- Keep the active saved composition after restarting KSP.
- Restore the original KSP state at any time.

## Installation

1. Download the player release ZIP from SpaceDock or GitHub Releases.
2. Open it and copy the included `GameData` folder into the root of your Kerbal Space Program installation.
3. Confirm that this file exists:

   `GameData/KSPSceneEditor/Plugins/KSPSceneEditor.dll`

4. Start KSP.
5. Use the **Scene Editor** toolbar button from the main menu.

## User content

KSP Scene Editor intentionally does not redistribute KSP crafts, planet textures, logos or skyboxes.

You can add your own content here:

- Crafts: `GameData/KSPSceneEditor/Crafts/`
- Images: `GameData/KSPSceneEditor/PluginData/Images/`
- Skyboxes: `GameData/KSPSceneEditor/PluginData/Skyboxes/`

Use only content you have permission to use.

## Skybox format

Create one folder per skybox pack and provide six faces:

- `GalaxyTex_PositiveX.png`
- `GalaxyTex_NegativeX.png`
- `GalaxyTex_PositiveY.png`
- `GalaxyTex_NegativeY.png`
- `GalaxyTex_PositiveZ.png`
- `GalaxyTex_NegativeZ.png`

`Skybox_` prefixed filenames are also supported. PNG, JPG and JPEG are accepted.

## Compatibility

Designed for **KSP 1.x**, with development and testing focused on the KSP 1.12.x environment.

Planet packs are discovered from loaded `CelestialBody` instances. Exact visual compatibility can vary depending on how a pack builds its ScaledSpace objects and materials.

## Dependencies

No third-party mod dependency is required.

## Source

The complete source code is available in this repository and is intended to compile against the KSP/Unity assemblies referenced by the included Visual Studio project.

## License

KSP Scene Editor is released under the **MIT License**. See `LICENSE.txt`.

## Disclaimer

KSP Scene Editor is an unofficial community project and is not affiliated with, endorsed by, or sponsored by the owners or publishers of Kerbal Space Program.
