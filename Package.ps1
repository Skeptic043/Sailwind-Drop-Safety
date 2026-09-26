param(
    [string]$SailwindManaged = $env:SAILWIND_MANAGED,
    [string]$BepInExCore = $env:BEPINEX_CORE_DIR
)

# Builds the plugin and writes a verified Thunderstore package ZIP and checksum to artifacts/release.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Drawing

$buildArgs = @('build', (Join-Path $PSScriptRoot 'src'), '-c', 'Release', '--nologo')
if ($SailwindManaged) { $buildArgs += "-p:SailwindManaged=$SailwindManaged" }
if ($BepInExCore) { $buildArgs += "-p:BepInExCore=$BepInExCore" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.name -cnotmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Invalid package name.' }
if ($manifest.version_number -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
if ([string]::IsNullOrWhiteSpace($manifest.description) -or $manifest.description.Length -gt 250) {
    throw 'Package description must contain 1-250 characters.'
}
if ($manifest.website_url -notmatch '^https?://') { throw 'Invalid website URL.' }
if (@($manifest.dependencies).Count -ne 1 -or $manifest.dependencies[0] -ne 'BepInEx-BepInExPack-5.4.2305') {
    throw 'The package must depend on Sailwind BepInExPack.'
}

$pluginSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src/Plugin.cs') -Raw
$pluginVersion = [regex]::Match($pluginSource, 'Version\s*=\s*"([^"]+)"').Groups[1].Value
if ($pluginVersion -ne $manifest.version_number) { throw 'Plugin and package versions differ.' }

$icon = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'icon.png'))
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256 -or
        $icon.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) {
        throw 'Icon must be a 256x256 PNG.'
    }
}
finally { $icon.Dispose() }

$files = [ordered]@{
    'manifest.json' = (Join-Path $PSScriptRoot 'manifest.json')
    'README.md' = (Join-Path $PSScriptRoot 'README.md')
    'CHANGELOG.md' = (Join-Path $PSScriptRoot 'CHANGELOG.md')
    'LICENSE' = (Join-Path $PSScriptRoot 'LICENSE')
    'icon.png' = (Join-Path $PSScriptRoot 'icon.png')
    'BepInEx/plugins/DropSafety/DropSafety.dll' = (Join-Path $PSScriptRoot 'src/bin/Release/netstandard2.0/DropSafety.dll')
}
foreach ($source in $files.Values) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release file: $source" }
}

$outputDirectory = Join-Path $PSScriptRoot 'artifacts/release'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$zipPath = Join-Path $outputDirectory "$($manifest.name)-$($manifest.version_number).zip"
$temporary = "$zipPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $files.GetEnumerator()) {
            $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $entry.Value, $entry.Key, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $archive.Dispose() }

    $archive = [IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        if ($archive.Entries.Count -ne $files.Count) { throw 'Package entry count differs.' }
        foreach ($entry in $files.GetEnumerator()) {
            $packed = $archive.GetEntry($entry.Key)
            if ($null -eq $packed) { throw "Missing package entry: $($entry.Key)" }
            $stream = $packed.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash) {
                throw "Package hash mismatch: $($entry.Key)"
            }
        }
    }
    finally { $archive.Dispose() }

    Move-Item -LiteralPath $temporary -Destination $zipPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}

$checksumPath = Join-Path $outputDirectory "SHA256SUMS-$($manifest.version_number).txt"
$checksum = "$( (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash )  $([IO.Path]::GetFileName($zipPath))"
[IO.File]::WriteAllText($checksumPath, "$checksum`n", [Text.UTF8Encoding]::new($false))
Write-Host "Verified $($files.Count) files in $zipPath"
Write-Host $checksum
