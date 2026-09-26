# Building from source

Install the .NET SDK (the tests use .NET 10), Sailwind, and BepInEx 5. The project compiles against the game's and BepInEx's own DLLs and never copies them into the repository or the build output.

Two MSBuild properties point at those folders:

- `SailwindManaged` is the game's `Sailwind_Data\Managed` folder. Default: `C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed`
- `BepInExCore` is the `BepInEx\core` folder. Default: `%LOCALAPPDATA%\SailwindModSynchronizer\packs\default\instance\BepInEx\core`

Run the following from this repository, using the directories for your installations:

```powershell
dotnet build src -c Release -p:SailwindManaged='C:\path\to\Sailwind\Sailwind_Data\Managed' -p:BepInExCore='C:\path\to\BepInEx\core'
```

The DLL is written to `src/bin/Release/netstandard2.0/DropSafety.dll`. The build does not change the game installation.

To run the tests, with the same property overrides:

```powershell
dotnet run --project tests -c Release
```

They check the drop rule and the `ModifierKey` parser, confirm the patch still finds exactly what it expects in your installed `Assembly-CSharp.dll`, and check the built plugin's identity, config bindings and references.

To build the DLL and create a Thunderstore package ZIP:

```powershell
./Package.ps1 -SailwindManaged 'C:\path\to\Sailwind\Sailwind_Data\Managed' -BepInExCore 'C:\path\to\BepInEx\core'
```

The package and its SHA-256 checksum are written to `artifacts/release/`. The ZIP contains only the package metadata, public documentation, license, icon, and mod DLL. It does not contain game or BepInEx assemblies.

Run `./tools/Render-Icon.ps1` to regenerate the 256×256 icon from its editable shapes.
