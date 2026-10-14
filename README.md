# Selected Pawn Portrait Overlay

Mod that renders the currently selected pawn as a front-facing full-body portrait in a movable overlay at the top-left of the HUD.

[Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3685326018) / [Portfolio walkthrough (Korean)](https://haelime.github.io/posts/rimworld-portrait-overlay/)

## Code entry points

- [SelectedPawnPortraitOverlayComponent.cs](Source/SelectedPawnPortraitOverlayComponent.cs): selected-pawn HUD and panel interaction
- [PawnFullBodyPortraitRenderer.cs](Source/PawnFullBodyPortraitRenderer.cs): full-body framing and portrait rendering
- [PortraitRenderCache.cs](Source/PortraitRenderCache.cs): invalidating cached portraits around rendering
- [ModCompatibility.cs](Source/ModCompatibility.cs): temporarily applying portrait preferences and restoring them on exit

## Features

- Separate top-left overlay panel for the selected pawn
- Full-body portrait framing using RimWorld's portrait renderer
- Automatic body-size normalization so very large and very small pawns fit the panel more consistently
- Optional live portrait refresh for movement, rotation, sleeping, and other pose changes
- Detailed settings for panel size, position, background, zoom, face emphasis, and supported pawn filters
- Korean, English, Japanese, Simplified Chinese, and Russian localization
- Graceful fallback when Facial Animation is not installed

## Verified Compatibility

- RimWorld 1.6
- RimWorld 1.7

## Build

Each game version gets its own assembly, selected at load time by `LoadFolders.xml`:

- `1.7/Assemblies` - RimWorld 1.7 (Waylith engine, .NET 10), the default build
- `1.6/Assemblies` - RimWorld 1.6 (Unity, .NET Framework 4.7.2)

Building requires the .NET 10 SDK. Point the build at your RimWorld install for the targeted version.

PowerShell example (RimWorld 1.7):

```powershell
$env:RIMWORLD_17_DIR="C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
dotnet build .\Source\SelectedPawnPortraitOverlay.csproj -c Release
```

RimWorld 1.6:

```powershell
$env:RIMWORLD_16_DIR="C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
dotnet build .\Source\SelectedPawnPortraitOverlay.csproj -c Release /p:GameVersion=1.6
```

You can also pass the install directory explicitly with `/p:RimWorldDir="..."`.
