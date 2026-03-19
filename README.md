# Selected Pawn Portrait Overlay

Mod that renders the currently selected pawn as a front-facing full-body portrait in a movable overlay at the top-left of the HUD.

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

## Build

Set your RimWorld install path before building.

PowerShell example:

```powershell
$env:RIMWORLD_16_DIR="C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
dotnet build .\Source\SelectedPawnPortraitOverlay.csproj -c Release
```

You can also pass the install directory explicitly:

```powershell
dotnet build .\Source\SelectedPawnPortraitOverlay.csproj -c Release /p:RimWorldDir="C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
```
