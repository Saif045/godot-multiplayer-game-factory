[CmdletBinding()]
param([string]$Label = 'cheap_001', [string]$Godot = 'D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = Join-Path $repo "artifacts/netfox_compatibility_spike/$Label"
if (Test-Path $evidence) { throw 'Use a fresh evidence label.' }
New-Item -ItemType Directory $evidence | Out-Null
$checks = @(
    @{ Name = 'lifecycle'; Args = @('--script','res://tests/netfox/network_events_lifecycle_test.gd'); Marker = 'NETFOX_LIFECYCLE_TEST_PASS' },
    @{ Name = 'motion'; Args = @('res://sandbox/netfox/player_vital_motion_probe.tscn'); Marker = 'VITAL_MOTION_PASS' },
    @{ Name = 'reuse'; Args = @('--','--run=round-reuse'); Marker = 'ROUND_REUSE_PASS round=3' }
)
$results = @()
try {
    foreach ($check in $checks) {
        $name = $check.Name
        $args = @('--headless','--path',$repo,'--log-file',"$evidence/$name.engine.log") + $check.Args
        $game = Start-Process $Godot -ArgumentList $args -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence/$name.stdout.log" -RedirectStandardError "$evidence/$name.stderr.log"
        try {
            if (-not $game.WaitForExit(30000)) { throw "$name timed out" }
            $game.Refresh()
            $bad = @(Select-String -Path "$evidence/$name.stderr.log","$evidence/$name.stdout.log" -Pattern 'SCRIPT ERROR:|ERROR:|FAIL|ObjectDB.*leaked|resources still in use')
            $pass = $game.ExitCode -eq 0 -and $bad.Count -eq 0 -and [bool](Select-String -Path "$evidence/$name.stdout.log" -Pattern $check.Marker -SimpleMatch)
            $results += @{name=$name;pass=$pass;exit=$game.ExitCode;errors=$bad.Count;pid=$game.Id}
            Write-Output "$name PASS=$pass exit=$($game.ExitCode) errors=$($bad.Count)"
            if (-not $pass) { $bad | Select-Object -First 12 | ForEach-Object { Write-Output $_.Line }; throw "$name failed" }
        } finally {
            if (Get-Process -Id $game.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $game.Id -Force }
            if (Get-Process -Id $game.Id -ErrorAction SilentlyContinue) { throw 'Owned process survived cleanup' }
        }
    }
} finally {
    @{checks=$results;cleanup_live=0} | ConvertTo-Json -Depth 5 | Set-Content "$evidence/result.json"
}
