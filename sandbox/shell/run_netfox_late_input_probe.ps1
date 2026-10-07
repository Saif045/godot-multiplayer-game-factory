[CmdletBinding()]
param([string]$Label = 'late_001', [switch]$VerboseEngine, [string]$Godot = 'D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = Join-Path $repo "artifacts/netfox_compatibility_spike/$Label"
if (Test-Path $evidence) { throw 'Use a fresh evidence label.' }
if (Get-NetUDPEndpoint -LocalPort 24872 -ErrorAction SilentlyContinue) { throw 'Probe port already occupied.' }
if (-not (Test-Path $Godot)) { throw 'Godot unavailable.' }
New-Item -ItemType Directory $evidence | Out-Null
$owned = @()
$result = 'FAIL'
try {
    foreach ($role in @('host','client')) {
        $args = @('--headless','--path',$repo,'--log-file',"$evidence/$role.engine.log",'res://sandbox/shell/netfox_late_input_probe.tscn')
        if ($VerboseEngine) { $args = @('--verbose') + $args }
        if ($role -eq 'host') { $args += @('--','--rpc-host') }
        $game = Start-Process $Godot -ArgumentList $args -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence/$role.stdout.log" -RedirectStandardError "$evidence/$role.stderr.log"
        $owned += $game
        if ($role -eq 'host') {
            $deadline = (Get-Date).AddSeconds(10)
            do {
                if ($game.HasExited) { throw 'Host exited before listener readiness' }
                $listener = Get-NetUDPEndpoint -LocalPort 24872 -ErrorAction SilentlyContinue
                if ($listener) { break }
                Start-Sleep -Milliseconds 100
            } while ((Get-Date) -lt $deadline)
            if (-not $listener) { throw 'Listener timeout' }
        }
    }
    foreach ($game in $owned) {
        if (-not $game.WaitForExit(30000)) { throw 'Probe process timeout' }
        $game.Refresh()
        if ($game.ExitCode -ne 0) { throw "Probe exit $($game.ExitCode)" }
    }
    $bad = @(Select-String -Path "$evidence/*.stdout.log","$evidence/*.stderr.log" -Pattern 'SCRIPT ERROR:|ERROR:|FAIL|ObjectDB.*leaked|resources still in use')
    if ($bad.Count -gt 0) { $bad | Select-Object -First 12 | ForEach-Object { Write-Output $_.Line }; throw 'Engine/probe errors' }
    foreach ($marker in @('LATE_PACKET_HELD','LATE_PACKET_RELEASE','LATE_INPUT_OBSERVED','LATE_INPUT_PASS')) {
        if (-not (Select-String -Path "$evidence/host.stdout.log" -Pattern $marker -SimpleMatch)) { throw "Missing $marker" }
    }
    if (-not (Select-String -Path "$evidence/client.stdout.log" -Pattern 'LATE_INPUT_CLIENT_PASS' -SimpleMatch)) { throw 'Client did not pass' }
    $held = (Select-String -Path "$evidence/host.stdout.log" -Pattern '^LATE_PACKET_HELD ').Line.Substring(17) | ConvertFrom-Json
    $released = (Select-String -Path "$evidence/host.stdout.log" -Pattern '^LATE_PACKET_RELEASE ').Line.Substring(20) | ConvertFrom-Json
    if ($held.sha256 -ne $released.sha256 -or $held.mode -ne 0 -or $released.mode -ne 0 -or $held.receiver -ne $released.receiver) { throw 'Packet bytes/mode/receiver changed' }
    $observed = (Select-String -Path "$evidence/host.stdout.log" -Pattern '^LATE_INPUT_OBSERVED ').Line.Substring(20) | ConvertFrom-Json
    if (-not $observed.safe -or -not $observed.snapshots_empty -or -not $observed.old_identity_removed) { throw 'Unsafe stale input' }
    $result = 'PASS'
    Write-Output "LATE_INPUT_RESULT=$result evidence=$evidence"
} finally {
    foreach ($game in $owned) {
        if (Get-Process -Id $game.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $game.Id -Force }
    }
    $remaining = @($owned | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue }).Count
    @{result=$result;cleanup_live=$remaining;pids=@($owned.Id);port_occupied=[bool](Get-NetUDPEndpoint -LocalPort 24872 -ErrorAction SilentlyContinue)} | ConvertTo-Json | Set-Content "$evidence/result.json"
    Write-Output "LATE_INPUT_CLEANUP=$remaining"
}
