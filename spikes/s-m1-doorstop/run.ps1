# Spike S-M1 (docs/13-mods.md §5): does valheim_server.exe load BepInEx from a folder outside its own through Unity
# Doorstop's launch arguments, as r2modman does, and stay vanilla when Doorstop is off?
# Runs on a GitHub windows-2025 runner (.github/workflows/spike-mods-doorstop.yml); nothing here is shipped.
param(
    [Parameter(Mandatory)] [string] $Root,
    [int] $WaitSeconds = 240
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$userAgent = 'GameWarden-spike (+https://github.com/vinh466/GameWarden)'

$server = Join-Path $Root 'server'
$set = Join-Path $Root 'mods\sets\1'
$data = Join-Path $Root 'data'
$downloads = Join-Path $Root 'downloads'
$logs = Join-Path $Root 'logs'
New-Item -ItemType Directory -Force $server, $set, $data, $downloads, $logs | Out-Null

function Write-Step([string] $text) { Write-Host "::group::$text" }
function End-Step { Write-Host '::endgroup::' }

# 1. Valheim's dedicated server through SteamCMD, anonymously, as GameWarden installs it.
Write-Step 'SteamCMD: app 896660'
$steamCmdZip = Join-Path $downloads 'steamcmd.zip'
Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $steamCmdZip
Expand-Archive $steamCmdZip (Join-Path $Root 'steamcmd') -Force
$steamCmd = Join-Path $Root 'steamcmd\steamcmd.exe'
foreach ($attempt in 1..3) {
    & $steamCmd +force_install_dir $server +login anonymous +app_update 896660 validate +quit | Out-Host
    if (Test-Path (Join-Path $server 'valheim_server.exe')) { break }
    Write-Host "SteamCMD attempt $attempt exited with $LASTEXITCODE and no valheim_server.exe; trying again."
}
if (-not (Test-Path (Join-Path $server 'valheim_server.exe'))) { throw 'SteamCMD did not install valheim_server.exe.' }
End-Step

# 2. Packages from Thunderstore, laid out as docs/13 §4.3 proposes.
function Get-Package([string] $namespace, [string] $name, [string] $version) {
    $zip = Join-Path $downloads "$namespace-$name-$version.zip"
    Invoke-WebRequest "https://thunderstore.io/package/download/$namespace/$name/$version/" -OutFile $zip -UserAgent $userAgent
    $folder = Join-Path $downloads "$namespace-$name"
    Expand-Archive $zip $folder -Force
    Write-Host "$namespace-$name-$version sha256 $((Get-FileHash $zip -Algorithm SHA256).Hash)"
    return $folder
}

Write-Step 'Packages'
$pack = Join-Path (Get-Package 'denikson' 'BepInExPack_Valheim' '5.4.2360') 'BepInExPack_Valheim'
Copy-Item (Join-Path $pack 'BepInEx') (Join-Path $set 'BepInEx') -Recurse -Force
Copy-Item (Join-Path $pack 'winhttp.dll') $server -Force
(Get-Content (Join-Path $pack 'doorstop_config.ini')) -replace '^\s*enabled\s*=.*$', 'enabled = false' |
    Set-Content (Join-Path $server 'doorstop_config.ini')
Write-Host "server\doorstop_config.ini:"; Get-Content (Join-Path $server 'doorstop_config.ini') | Out-Host

# Epic Loot and its dependencies (13 §3), each into plugins\<Namespace-Name>\: a package's plugins folder if it has one,
# otherwise every file but the Thunderstore metadata (r2modman's fallback, to be confirmed by spike S-M2).
$mods = @(
    @('ValheimModding', 'JsonDotNET', '13.0.4'),
    @('ValheimModding', 'Jotunn', '2.30.2'),
    @('RandyKnapp', 'EpicLoot', '0.15.4')
)
foreach ($mod in $mods) {
    $folder = Get-Package @mod
    $target = Join-Path $set "BepInEx\plugins\$($mod[0])-$($mod[1])"
    New-Item -ItemType Directory -Force $target | Out-Null
    $plugins = Get-ChildItem $folder -Directory | Where-Object Name -eq 'plugins' | Select-Object -First 1
    if ($plugins) {
        Copy-Item (Join-Path $plugins.FullName '*') $target -Recurse -Force
    } else {
        Get-ChildItem $folder | Where-Object Name -notin 'manifest.json', 'icon.png', 'README.md', 'CHANGELOG.md' |
            Copy-Item -Destination $target -Recurse -Force
    }
    Write-Host "$($mod[0])-$($mod[1]):"; Get-ChildItem $target -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($set.Length) } | Out-Host
}
End-Step

# 3. Start the server three ways and see where, and whether, BepInEx writes its log.
$env:SteamAppId = '892970'
$bepInExLog = Join-Path $set 'BepInEx\LogOutput.log'
$strayLog = Join-Path $server 'BepInEx\LogOutput.log'

function Start-Valheim([string] $case, [string[]] $doorstop, [int] $seconds, [string] $until) {
    Remove-Item $bepInExLog, $strayLog -Force -ErrorAction SilentlyContinue
    $gameLog = Join-Path $logs "valheim-$case.log"
    $arguments = @('-nographics', '-batchmode', '-name', 'GWspike', '-port', '2456', '-world', 'spike', '-password', 'spike-2026',
        '-savedir', $data, '-public', '0', '-logFile', $gameLog) + $doorstop
    Write-Host "valheim_server.exe $($arguments -join ' ')"
    $process = Start-Process (Join-Path $server 'valheim_server.exe') -ArgumentList $arguments -WorkingDirectory $server -PassThru
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
        if ($until -and (Test-Path $bepInExLog) -and (Select-String -Path $bepInExLog -Pattern $until -Quiet)) { break }
        Start-Sleep -Seconds 5
    }
    $exited = $process.HasExited
    if (-not $exited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Seconds 3
    foreach ($file in @($bepInExLog, $strayLog)) {
        if (Test-Path $file) { Copy-Item $file (Join-Path $logs "$case-$(Split-Path (Split-Path $file -Parent) -Parent | Split-Path -Leaf)-LogOutput.log") -Force }
    }
    return [pscustomobject]@{
        Case = $case
        Exited = $exited
        SetLog = Test-Path $bepInExLog
        StrayLog = Test-Path $strayLog
        Chainloader = (Test-Path $bepInExLog) -and (Select-String -Path $bepInExLog -Pattern 'Chainloader startup complete' -Quiet)
        EpicLoot = (Test-Path $bepInExLog) -and (Select-String -Path $bepInExLog -Pattern 'Loading \[[^\]]*Epic ?Loot' -Quiet)
        GameLog = (Test-Path $gameLog) -and ((Get-Item $gameLog).Length -gt 0)
    }
}

Write-Step 'Case on: --doorstop-enabled true, target in the set'
$preloader = Join-Path $set 'BepInEx\core\BepInEx.Preloader.dll'
$on = Start-Valheim 'on' @('--doorstop-enabled', 'true', '--doorstop-target-assembly', $preloader) $WaitSeconds 'Chainloader startup complete'
if (Test-Path $bepInExLog) { Select-String -Path $bepInExLog -Pattern 'BepInEx|Loading \[|Chainloader|Error|Exception' | Select-Object -First 60 | Out-Host }
End-Step

Write-Step 'Case off: --doorstop-enabled false'
$off = Start-Valheim 'off' @('--doorstop-enabled', 'false') 90 $null
End-Step

Write-Step 'Case none: no Doorstop arguments (doorstop_config.ini says enabled = false)'
$none = Start-Valheim 'none' @() 90 $null
End-Step

$results = @($on, $off, $none)
$results | Format-Table -AutoSize | Out-String | Write-Host
$passed = $on.Chainloader -and $on.EpicLoot -and -not $on.StrayLog -and -not $off.SetLog -and -not $off.StrayLog -and -not $none.SetLog -and -not $none.StrayLog
if ($env:GITHUB_STEP_SUMMARY) {
    @('## Spike S-M1', '', '| Case | Set log | Log in server\ | Chainloader | Epic Loot | Game log | Exited early |', '|---|---|---|---|---|---|---|') +
        ($results | ForEach-Object { "| $($_.Case) | $($_.SetLog) | $($_.StrayLog) | $($_.Chainloader) | $($_.EpicLoot) | $($_.GameLog) | $($_.Exited) |" }) +
        @('', "**Result:** $(if ($passed) { 'passed' } else { 'failed' })") | Add-Content $env:GITHUB_STEP_SUMMARY
}
if (-not $passed) { throw 'S-M1 failed: see the table and the logs artifact.' }
Write-Host 'S-M1 passed.'
