# Spike S-P1 (docs/14-palworld.md): run Palworld's dedicated server the way a GameWarden adapter would: SteamCMD
# install, a rendered PalWorldSettings.ini, the Shipping-Cmd exe started directly, readiness through the REST API,
# then a save and a graceful shutdown through it. Also dumps DefaultPalWorldSettings.ini (the key list and defaults).
# Runs on a GitHub windows-2025 runner (.github/workflows/spike-palworld.yml); nothing here is shipped.
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

Write-Step 'Disks'
Get-PSDrive -PSProvider FileSystem | Format-Table Name, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}}, @{n='UsedGB';e={[math]::Round($_.Used/1GB,1)}} -AutoSize | Out-Host
End-Step

# 1. The server through SteamCMD, anonymously (docs: steamcmd +login anonymous +app_update 2394010 validate +quit).
Write-Step 'SteamCMD: app 2394010'
$steamCmdZip = Join-Path $downloads 'steamcmd.zip'
Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $steamCmdZip
Expand-Archive $steamCmdZip (Join-Path $Root 'steamcmd') -Force
$steamCmd = Join-Path $Root 'steamcmd\steamcmd.exe'
$exe = Join-Path $server 'Pal\Binaries\Win64\PalServer-Win64-Shipping-Cmd.exe'
foreach ($attempt in 1..3) {
    & $steamCmd +force_install_dir $server +login anonymous +app_update 2394010 validate +quit | Select-Object -Last 15 | Out-Host
    if (Test-Path $exe) { break }
    Write-Host "SteamCMD attempt $attempt exited with $LASTEXITCODE and no Shipping-Cmd exe; trying again."
}
if (-not (Test-Path $exe)) { throw 'SteamCMD did not install PalServer-Win64-Shipping-Cmd.exe.' }
$size = (Get-ChildItem $server -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Installed: {0:N1} GB" -f ($size / 1GB))
Get-ChildItem $server -Name | Out-Host
Get-ChildItem (Join-Path $server 'Pal\Binaries\Win64') -Name | Out-Host
End-Step

# 2. The template: every key and its default for this version.
Write-Step 'DefaultPalWorldSettings.ini'
$template = Join-Path $server 'DefaultPalWorldSettings.ini'
Copy-Item $template (Join-Path $out 'DefaultPalWorldSettings.ini')
Get-Content $template | Out-Host
End-Step

# 3. A rendered PalWorldSettings.ini: the template's OptionSettings with a few fields replaced, as the renderer would.
Write-Step 'Render PalWorldSettings.ini'
$adminPassword = 'spike-admin-2026'
$text = Get-Content $template -Raw
$line = ([regex]::Match($text, '(?m)^OptionSettings=\((.*)\)\s*$')).Groups[1].Value
if (-not $line) { throw 'The template has no OptionSettings=(...) line.' }
$overrides = [ordered]@{
    ServerName = '"GameWarden spike"'
    ServerDescription = '"S-P1"'
    AdminPassword = '"' + $adminPassword + '"'
    ServerPlayerMaxNum = '8'
    RESTAPIEnabled = 'True'
    RESTAPIPort = '8212'
    ExpRate = '2.000000'
    CrossplayPlatforms = '(Steam)'
}
foreach ($key in $overrides.Keys) {
    $pattern = '(?<=(^|,))' + [regex]::Escape($key) + '=(\([^)]*\)|"[^"]*"|[^,]*)'
    if ([regex]::IsMatch($line, $pattern)) {
        $line = [regex]::Replace($line, $pattern, "$key=$($overrides[$key])")
    } else {
        Write-Host "Key $key is not in the template; appended."
        $line = "$line,$key=$($overrides[$key])"
    }
}
$config = Join-Path $server 'Pal\Saved\Config\WindowsServer'
New-Item -ItemType Directory -Force $config | Out-Null
$rendered = "[/Script/Pal.PalGameWorldSettings]`r`nOptionSettings=($line)`r`n"
Set-Content (Join-Path $config 'PalWorldSettings.ini') $rendered -NoNewline -Encoding utf8
Copy-Item (Join-Path $config 'PalWorldSettings.ini') (Join-Path $out 'PalWorldSettings.rendered.ini')
End-Step

# 4. Start the real server process directly, its console captured.
Write-Step 'Start'
$stdout = Join-Path $out 'server-stdout.log'
$stderr = Join-Path $out 'server-stderr.log'
$process = Start-Process $exe -ArgumentList @('-port=8211', '-players=8', '-logformat=text') -WorkingDirectory (Split-Path $exe) `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
Write-Host "PID $($process.Id)"
$auth = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("admin:$adminPassword")) }
$api = 'http://127.0.0.1:8212/v1/api'
$started = Get-Date
$ready = $null
while (((Get-Date) - $started).TotalSeconds -lt $ReadySeconds -and -not $process.HasExited) {
    try { $ready = Invoke-RestMethod "$api/info" -Headers $auth -TimeoutSec 5; break } catch { Start-Sleep -Seconds 5 }
}
$readyAfter = [math]::Round(((Get-Date) - $started).TotalSeconds)
Write-Host "Ready: $($null -ne $ready) after $readyAfter s; exited early: $($process.HasExited)"
if ($ready) { $ready | ConvertTo-Json -Depth 5 | Tee-Object (Join-Path $out 'info.json') | Out-Host }
End-Step

$results = [ordered]@{ ReadyAfterSeconds = $readyAfter; Ready = $null -ne $ready }
if ($ready) {
    Write-Step 'REST: settings, players, metrics, wrong password'
    $settings = Invoke-RestMethod "$api/settings" -Headers $auth
    $settings | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'settings.json')
    $results.SettingsServerName = $settings.ServerName
    $results.SettingsExpRate = $settings.ExpRate
    $results.SettingsPlayers = $settings.ServerPlayerMaxNum
    try { Invoke-RestMethod "$api/players" -Headers $auth | ConvertTo-Json -Depth 5 | Out-Host; $results.Players = 'ok' } catch { $results.Players = $_.Exception.Message }
    try { Invoke-RestMethod "$api/metrics" -Headers $auth | ConvertTo-Json -Depth 5 | Tee-Object (Join-Path $out 'metrics.json') | Out-Host } catch { Write-Host "metrics: $($_.Exception.Message)" }
    $bad = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('admin:wrong')) }
    try { Invoke-RestMethod "$api/info" -Headers $bad | Out-Null; $results.WrongPassword = 'accepted!' } catch { $results.WrongPassword = [int]$_.Exception.Response.StatusCode }
    End-Step

    Write-Step 'REST: save, then shutdown'
    $saveStart = Get-Date
    try { Invoke-RestMethod "$api/save" -Method Post -Headers $auth -TimeoutSec 120 | Out-Null; $results.Save = 'ok' } catch { $results.Save = $_.Exception.Message }
    $results.SaveSeconds = [math]::Round(((Get-Date) - $saveStart).TotalSeconds, 1)
    $body = '{"waittime": 1, "message": "GameWarden spike stops"}'
    try { Invoke-RestMethod "$api/shutdown" -Method Post -Headers $auth -ContentType 'application/json' -Body $body | Out-Null; $results.Shutdown = 'ok' } catch { $results.Shutdown = $_.Exception.Message }
    $stopStart = Get-Date
    $exited = $process.WaitForExit(120000)
    $results.ExitedAfterShutdown = $exited
    $results.ExitSeconds = [math]::Round(((Get-Date) - $stopStart).TotalSeconds, 1)
    if ($exited) { $results.ExitCode = $process.ExitCode }
    End-Step
}
if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; Get-Process | Where-Object Path -like "$server*" | Stop-Process -Force }

Write-Step 'Saves and config after the run'
Get-ChildItem (Join-Path $server 'Pal\Saved') -Recurse -Depth 3 | Select-Object -First 60 | ForEach-Object { $_.FullName.Substring($server.Length) } | Out-Host
$results.SaveGames = (Test-Path (Join-Path $server 'Pal\Saved\SaveGames'))
$results.ConfigAfterRun = (Get-Content (Join-Path $config 'PalWorldSettings.ini') -Raw) -eq $rendered
Copy-Item (Join-Path $config 'PalWorldSettings.ini') (Join-Path $out 'PalWorldSettings.after.ini')
Get-ChildItem $config -Name | Out-Host
Write-Host '--- server stdout (last 60 lines) ---'
Get-Content $stdout -Tail 60 | Out-Host
End-Step

$results.GetEnumerator() | ForEach-Object { "{0} = {1}" -f $_.Key, $_.Value } | Tee-Object (Join-Path $out 'results.txt') | Out-Host
if ($env:GITHUB_STEP_SUMMARY) {
    @('## Spike S-P1 (Palworld)', '', '| Check | Result |', '|---|---|') + ($results.GetEnumerator() | ForEach-Object { "| $($_.Key) | $($_.Value) |" }) |
        Add-Content $env:GITHUB_STEP_SUMMARY
}
if (-not $results.Ready) { throw 'S-P1: the REST API never answered.' }
