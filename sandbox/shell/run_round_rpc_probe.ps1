[CmdletBinding()]
param(
    [string]$Label = "local",
    [switch]$ExpectMissingRpc,
    [string]$Godot = "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = Join-Path $repo "artifacts/round_reuse_rpc_investigation/$Label"
if (Test-Path -LiteralPath $evidence) { throw "Use a new label; evidence already exists: $evidence" }
if (-not (Test-Path -LiteralPath $Godot)) { throw 'Godot executable is unavailable.' }
if (Get-NetUDPEndpoint -LocalPort 24871 -ErrorAction SilentlyContinue) { throw 'Diagnostic port is already occupied.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$owned = @()
$result = 'FAIL'
try {
    $hostArgs = @('--headless','--path',$repo,'--log-file',"$evidence/host.engine.log",'--','--run=round-rpc','--rpc-host','--netfox-lifecycle-trace')
    $hostGame = Start-Process -FilePath $Godot -ArgumentList $hostArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence/host.stdout.log" -RedirectStandardError "$evidence/host.stderr.log"
    $owned += $hostGame
    $deadline = (Get-Date).AddSeconds(10)
    do {
        if ($hostGame.HasExited) { throw 'Host exited before listener readiness.' }
        $listener = Get-NetUDPEndpoint -LocalPort 24871 -ErrorAction SilentlyContinue
        if ($listener) { break }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    if (-not $listener) { throw 'Host listener did not open within 10 seconds.' }
    $clientArgs = @('--headless','--path',$repo,'--log-file',"$evidence/client.engine.log",'--','--run=round-rpc','--netfox-lifecycle-trace')
    $clientGame = Start-Process -FilePath $Godot -ArgumentList $clientArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence/client.stdout.log" -RedirectStandardError "$evidence/client.stderr.log"
    $owned += $clientGame
    foreach ($game in $owned) {
        if (-not $game.WaitForExit(30000)) { throw "Diagnostic process $($game.Id) timed out." }
        $game.Refresh()
        if ($game.ExitCode -notin @(0,2)) { throw "Diagnostic process exited with $($game.ExitCode)." }
    }
    $missing = @(Select-String -Path "$evidence/host.stdout.log","$evidence/client.stdout.log" -Pattern '"target_exists":false')
    $rounds = @(Select-String -Path "$evidence/host.stdout.log" -Pattern 'RPC_ROUND_CLEARED')
    if ($rounds.Count -ne 3) { throw "Expected three cleared rounds, got $($rounds.Count)." }
    if ($ExpectMissingRpc -and $missing.Count -eq 0) { throw 'Expected defect did not reproduce.' }
    if (-not $ExpectMissingRpc -and $missing.Count -gt 0) { throw "$($missing.Count) RPCs targeted removed nodes." }
    $result = 'PASS'
    Write-Output "RPC_PROBE_RESULT=$result missing_targets=$($missing.Count) rounds=$($rounds.Count) evidence=$evidence"
} finally {
    foreach ($game in $owned) {
        if (Get-Process -Id $game.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $game.Id -Force }
    }
    $remaining = @($owned | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue }).Count
    @{ result = $result; expect_missing_rpc = [bool]$ExpectMissingRpc; cleanup_processes = $remaining; pids = @($owned.Id) } |
        ConvertTo-Json | Set-Content -LiteralPath "$evidence/result.json"
    Write-Output "RPC_PROBE_CLEANUP_LIVE=$remaining"
}
