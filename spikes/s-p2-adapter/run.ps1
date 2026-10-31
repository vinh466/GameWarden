# Spike S-P2 (docs/14-palworld.md): the Palworld adapter's values against the real server.
# Installs Palworld with SteamCMD once, then starts it with several rendered PalWorldSettings.ini variants and reads
# back GET /v1/api/settings: the enum values, the quoted technology list, and the crossplay list. Nothing is shipped.
# Runs on a GitHub windows-2025 runner (.github/workflows/spike-palworld-adapter.yml).
param(
    [Parameter(Mandatory)] [string] $Root,
    [int] $ReadySeconds = 600
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$server = Join-Path $Root 'server'
$downloads = Join-Path $Root 'downloads'
$out = Join-Path $Root 'out'
New-Item -ItemType Directory -Force $server, $downloads, $out | Out-Null
function Write-Step([string] $text) { Write-Host "::group::$text" }
function End-Step { Write-Host '::endgroup::' }

Write-Step 'SteamCMD: app 2394010'
$steamCmdZip = Join-Path $downloads 'steamcmd.zip'
Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $steamCmdZip
Expand-Archive $steamCmdZip (Join-Path $Root 'steamcmd') -Force
$steamCmd = Join-Path $Root 'steamcmd\steamcmd.exe'
$exe = Join-Path $server 'Pal\Binaries\Win64\PalServer-Win64-Shipping-Cmd.exe'
foreach ($attempt in 1..3) {
    & $steamCmd +force_install_dir $server +login anonymous +app_update 2394010 validate +quit | Select-Object -Last 10 | Out-Host
    if (Test-Path $exe) { break }
    Write-Host "SteamCMD attempt $attempt exited with $LASTEXITCODE and no Shipping-Cmd exe; trying again."
}
if (-not (Test-Path $exe)) { throw 'SteamCMD did not install PalServer-Win64-Shipping-Cmd.exe.' }
$size = (Get-ChildItem $server -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Installed: {0:N1} GB" -f ($size / 1GB))
$template = Join-Path $server 'DefaultPalWorldSettings.ini'
Copy-Item $template (Join-Path $out 'DefaultPalWorldSettings.ini')
End-Step

$adminPassword = 'sp2-admin-2026'
$auth = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:$adminPassword")) }
$api = 'http://127.0.0.1:8212/v1/api'
$config = Join-Path $server 'Pal\Saved\Config\WindowsServer'
New-Item -ItemType Directory -Force $config | Out-Null

function Render-Config([string] $name, [hashtable] $overrides) {
    $text = Get-Content $template -Raw
    $line = ([regex]::Match($text, '(?m)^OptionSettings=\((.*)\)\s*$')).Groups[1].Value
    if (-not $line) { throw 'The template has no OptionSettings=(...) line.' }
    $fixed = [ordered]@{
        RESTAPIEnabled   = 'True'
        RESTAPIPort      = '8212'
        AdminPassword    = '"' + $adminPassword + '"'
        ServerPassword   = '""'
        PublicPort       = '8211'
        RCONEnabled      = 'False'
    }
    $values = [ordered]@{}
    foreach ($key in $fixed.Keys) { $values[$key] = $fixed[$key] }
    foreach ($key in $overrides.Keys) { $values[$key] = $overrides[$key] }
    foreach ($key in $values.Keys) {
        $pattern = '(?<=(^|,))' + [regex]::Escape($key) + '=(\([^)]*\)|"[^"]*"|[^,]*)'
        $match = [regex]::Match($line, $pattern)
        if ($match.Success) {
            $line = $line.Remove($match.Index, $match.Length).Insert($match.Index, "$key=$($values[$key])")
        } else {
            Write-Host "Key $key is not in the template; appended."
            $line = "$line,$key=$($values[$key])"
        }
    }
    $rendered = "[/Script/Pal.PalGameWorldSettings]`r`nOptionSettings=($line)`r`n"
    $path = Join-Path $config 'PalWorldSettings.ini'
    [IO.File]::WriteAllText($path, $rendered, [Text.UTF8Encoding]::new($false))
    Copy-Item $path (Join-Path $out "$name.rendered.ini")
}

function Config-Value([string] $name, [string] $key) {
    $path = Join-Path $out "$name.after.ini"
    if (-not (Test-Path $path)) { return $null }
    $text = Get-Content $path -Raw
    $line = ([regex]::Match($text, '(?m)^OptionSettings=\((.*)\)\s*$')).Groups[1].Value
    $match = [regex]::Match($line, '(?<=(^|,))' + [regex]::Escape($key) + '=(\([^)]*\)|"[^"]*"|[^,]*)')
    if ($match.Success) { return $match.Value.Substring($key.Length + 1) }
    return $null
}

function Run-Variant([string] $name, [hashtable] $overrides) {
    Write-Step "Variant $name"
    Render-Config $name $overrides
    $stdout = Join-Path $out "$name.stdout.log"
    $stderr = Join-Path $out "$name.stderr.log"
    $process = Start-Process $exe -ArgumentList @('-port=8211', '-players=16', '-logformat=text') -WorkingDirectory (Split-Path $exe) `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $started = Get-Date
    $info = $null
    while (((Get-Date) - $started).TotalSeconds -lt $ReadySeconds -and -not $process.HasExited) {
        try { $info = Invoke-RestMethod "$api/info" -Headers $auth -TimeoutSec 5; break } catch { Start-Sleep -Seconds 2 }
    }
    $readyAfter = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    $result = [ordered]@{
        name = $name
        ready = $null -ne $info
        readyAfterSeconds = $readyAfter
        exitEarly = $process.HasExited
        settings = $null
        config = [ordered]@{}
    }
    if ($info) {
        try {
            $settings = Invoke-RestMethod "$api/settings" -Headers $auth -TimeoutSec 30
            $settings | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out "$name.settings.json")
            $result.settings = [ordered]@{
                Difficulty = $settings.Difficulty
                DeathPenalty = $settings.DeathPenalty
                RandomizerType = $settings.RandomizerType
                LogFormatType = $settings.LogFormatType
                CrossplayPlatforms = @($settings.CrossplayPlatforms)
                DenyTechnologyList = @($settings.DenyTechnologyList)
                ServerName = $settings.ServerName
                ExpRate = $settings.ExpRate
                ServerPlayerMaxNum = $settings.ServerPlayerMaxNum
                bIsPvP = $settings.bIsPvP
            }
        } catch {
            $result.settings = "GET /settings failed: $($_.Exception.Message)"
        }
        try {
            Invoke-RestMethod "$api/save" -Method Post -Headers $auth -TimeoutSec 120 | Out-Null
            Invoke-RestMethod "$api/shutdown" -Method Post -Headers $auth -ContentType 'application/json' `
                -Body '{"waittime": 1, "message": "S-P2 stops"}' -TimeoutSec 30 | Out-Null
        } catch {
            Write-Host "Stop request failed: $($_.Exception.Message)"
        }
        $exited = $process.WaitForExit(120000)
        $result.exitedAfterStop = $exited
        if ($exited) { $result.exitCode = $process.ExitCode }
    }
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        Get-Process | Where-Object Path -like "$server*" | Stop-Process -Force
    }
    Copy-Item (Join-Path $config 'PalWorldSettings.ini') (Join-Path $out "$name.after.ini")
    foreach ($key in @('Difficulty', 'DeathPenalty', 'RandomizerType', 'LogFormatType', 'CrossplayPlatforms', 'DenyTechnologyList', 'ServerName', 'ExpRate', 'ServerPlayerMaxNum', 'RESTAPIEnabled', 'RESTAPIPort', 'PublicPort', 'RCONEnabled')) {
        $result.config[$key] = Config-Value $name $key
    }
    $result.config.AdminPasswordSet = (Config-Value $name 'AdminPassword') -match '^".+"$'
    $result.config.ServerPasswordEmpty = (Config-Value $name 'ServerPassword') -eq '""'
    End-Step
    return [pscustomobject]$result
}

$variants = @(
    [pscustomobject]@{ name = 'baseline'; overrides = @{
        Difficulty = 'None'; DeathPenalty = 'Item'; RandomizerType = 'None'; LogFormatType = 'Text';
        CrossplayPlatforms = '(Steam)'; ServerName = '"S-P2 baseline"'; ExpRate = '1.000000'; ServerPlayerMaxNum = '16'
    } },
    [pscustomobject]@{ name = 'hard-pvp-all'; overrides = @{
        Difficulty = 'Hard'; DeathPenalty = 'All'; RandomizerType = 'Region'; LogFormatType = 'Text';
        CrossplayPlatforms = '(Steam)'; DenyTechnologyList = '("RepairBench")'; ServerName = '"S-P2 hard"';
        ExpRate = '2.000000'; ServerPlayerMaxNum = '16'; bIsPvP = 'True'
    } },
    [pscustomobject]@{ name = 'normal-itemequipment'; overrides = @{
        Difficulty = 'Normal'; DeathPenalty = 'ItemAndEquipment'; RandomizerType = 'All'; LogFormatType = 'Text';
        CrossplayPlatforms = '(Steam)'; DenyTechnologyList = '("RepairBench")'; ServerName = '"S-P2 normal"';
        ExpRate = '3.000000'; ServerPlayerMaxNum = '32'
    } }
)

$results = @()
foreach ($variant in $variants) {
    $results += Run-Variant $variant.name $variant.overrides
}

$results | ConvertTo-Json -Depth 10 | Tee-Object (Join-Path $out 'results.json') | Out-Host
if (-not ($results | Where-Object { $_.name -eq 'baseline' -and $_.ready })) {
    throw 'S-P2: the baseline server never became ready.'
}
