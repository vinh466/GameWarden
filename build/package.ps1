# Builds the release assets (docs/10-build-test-release.md §4, ADR-0013):
#   GameWarden-<v>-win-x64.zip     Core, Runner, CLI and Setup, self-contained, one flat folder sharing one runtime
#   GameWarden-Setup-<v>.exe       single-file Setup engine with that zip inside (first install on the VM)
#   SHA256SUMS                     hashes of both, signed afterwards by build/sign-manifest.ps1
#
#   ./build/package.ps1 -Version 0.1.0-rc.1      a release
#   ./build/package.ps1 -Suffix dev.42           <VersionPrefix>-dev.42 (CI dev build)
param(
    [string]$Version,
    [string]$Suffix = "dev",
    [string]$Output = "artifacts",
    [string]$Dotnet = "dotnet",
    [string]$Npm = "npm"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Version) {
    $prefix = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
    $Version = "$prefix-$Suffix"
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "'$Version' is not a version like 1.2.3 or 1.2.3-rc.1" }

$out = Join-Path $root $Output
$payload = Join-Path $out "payload"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force $payload | Out-Null

function Publish([string]$project, [string[]]$extra) {
    & $Dotnet publish (Join-Path $root "src\$project\$project.csproj") -c Release -r win-x64 --self-contained true "-p:Version=$Version" @extra
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed" }
}

# Core first: it carries a framework-dependent copy of the Runner, which the Runner's own publish then replaces.
foreach ($project in "GameWarden.Core", "GameWarden.Runner", "GameWarden.Cli", "GameWarden.Setup") {
    Publish $project @("-o", $payload)
}
# The web panel (docs/11 §12): Core serves it from wwwroot next to it. Source maps stay out of the release.
$panel = Join-Path $root "panel"
Push-Location $panel
try {
    & $Npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    & $Npm run build
    if ($LASTEXITCODE -ne 0) { throw "the panel build failed" }
}
finally {
    Pop-Location
}
$wwwroot = Join-Path $payload "wwwroot"
Copy-Item -Recurse (Join-Path $panel "dist") $wwwroot
Get-ChildItem $wwwroot -Recurse -Filter *.map | Remove-Item -Force

Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $payload "web.config")
Get-ChildItem $payload -Filter *.pdb | Remove-Item -Force
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $payload "adapters-builtin\fakegame") # development only
[IO.File]::WriteAllText((Join-Path $payload "version.txt"), $Version)

$zip = Join-Path $out "GameWarden-$Version-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Entry names with '/', whichever PowerShell runs this (Windows PowerShell's CreateFromDirectory writes '\').
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $payload -Recurse -File | ForEach-Object {
        $name = $_.FullName.Substring($payload.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$setupOut = Join-Path $out "setup"
Publish "GameWarden.Setup" @("-o", $setupOut, "-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:PayloadZip=$zip")
$setup = Join-Path $out "GameWarden-Setup-$Version.exe"
Copy-Item (Join-Path $setupOut "GameWarden.Setup.exe") $setup

$lines = foreach ($file in @($setup, $zip)) {
    "{0}  {1}" -f (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $file -Leaf)
}
[IO.File]::WriteAllText((Join-Path $out "SHA256SUMS"), (($lines -join "`n") + "`n"))

Write-Host "Version $Version"
Get-ChildItem $out -File | ForEach-Object { Write-Host ("  {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
