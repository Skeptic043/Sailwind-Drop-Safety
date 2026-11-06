# Building Sailwind Drop Safety

## Requirements

- Windows with PowerShell 7 and the .NET 10 SDK (the tests target .NET 10).
- A local Sailwind installation with BepInEx 5.

Game and loader assemblies are build references only. They are not included in this repository or its packages.

Two MSBuild properties point at those folders:

- `SailwindManaged` is the game's `Sailwind_Data\Managed` folder. Default: `C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed`
- `BepInExCore` is the `BepInEx\core` folder. Default: `%LOCALAPPDATA%\SailwindModSynchronizer\packs\default\instance\BepInEx\core`

## Build

From the repository folder:

```powershell
dotnet build src -c Release -p:SailwindManaged='C:\Games\Sailwind\Sailwind_Data\Managed' -p:BepInExCore='C:\Games\Sailwind\BepInEx\core'
```

Use the paths for your own installation or mod profile. The output is `src/bin/Release/netstandard2.0/DropSafety.dll`.

## Test

```powershell
dotnet run --project tests -c Release -p:SailwindManaged='C:\Games\Sailwind\Sailwind_Data\Managed' -p:BepInExCore='C:\Games\Sailwind\BepInEx\core'
```

The checks cover the drop rule and the `ModifierKey` parser, confirm the patch still finds exactly what it expects in your installed `Assembly-CSharp.dll`, and check the built plugin's identity, config bindings and references. Any failure makes the command fail.

## Package

```powershell
pwsh -File ./Package.ps1 -SailwindManaged 'C:\Games\Sailwind\Sailwind_Data\Managed' -BepInExCore 'C:\Games\Sailwind\BepInEx\core'
```

This builds the mod and writes `artifacts/release/DropSafety-<version>.zip` with `BepInEx/plugins/DropSafety/`, `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md` and `LICENSE`.

Run `./tools/Render-Icon.ps1` to regenerate the 256×256 icon from its editable shapes.
