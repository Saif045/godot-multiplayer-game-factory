<#
Runs the existing two-account Steam gameplay slice as a host-PC -> VM acceptance test.

Prerequisites: Steam is logged in on both machines, `ssh gamefactory-vm` works without a
password, and the VM's GameFactoryClient scheduled task launches C:\GameFactoryAgent\run_client.ps1
inside the logged-in desktop session. The task is intentionally the only way this script starts
the VM game: launching it directly through SSH puts Steam in the wrong Windows session.
#>
[CmdletBinding()]
param(
    [ValidateSet("Health", "Launch", "Verify", "Retry", "Stop")]
    [string]$Mode = "Launch",
    [string]$Godot = "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe",
    [string]$OutputDirectory,
    [string]$VmAlias = "gamefactory-vm",
    [string]$VmBuildRoot = "C:/GameFactoryBuilds",
    [string]$VmConfigPath = "C:/GameFactoryAgent/client_config.json",
    [string]$VmStatusPath = "C:/GameFactoryAgent/client_status.json",
    [string]$VmRunnerPath = "C:/GameFactoryAgent/run_client.ps1",
    [ValidateSet("steam_basic", "netfox_time_sync", "netfox_gameplay", "netfox_player_3d")]
    [string]$Scenario = "steam_basic",
    [int]$HostTimeoutSeconds = 120,
    [int]$ScenarioTimeoutSeconds = 120,
    [ValidateRange(30, 1800)]
    [int]$BuildStageTimeoutSeconds = 300,
    [switch]$RecoverVm,
    [switch]$FreshTransport,
    [string]$VmName = "Game-Testing-VM",
    [ValidateRange(30, 600)]
    [int]$VmRecoveryTimeoutSeconds = 180,
    [switch]$SkipExport,
    [switch]$ForceExport,
    [switch]$SkipBuildParity,
    [switch]$ForceFullVmParity,
    [string]$ExpectedManifestSha256,
    [string]$RunId,
    [ValidateRange(1, 9999)]
    [int]$Attempt,
    [string]$ArtifactRoot,
    [switch]$VerifyBuildOnly,
    [switch]$ShowHostConsole
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "powershell\hash_utils.ps1")
. (Join-Path (Split-Path -Parent $PSScriptRoot) "powershell\process_utils.ps1")
$sshOptions = @("-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=5", "-o", "ServerAliveCountMax=2")
$externalCommandTimeoutSeconds = 30
$openSshDirectory = Join-Path $env:WINDIR "System32\OpenSSH"
$sshExecutable = Join-Path $openSshDirectory "ssh.exe"
$scpExecutable = Join-Path $openSshDirectory "scp.exe"
if (-not (Test-Path -LiteralPath $sshExecutable) -or -not (Test-Path -LiteralPath $scpExecutable)) {
    throw "Windows OpenSSH client tools were not found under $openSshDirectory."
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$vmEndpointPath = Join-Path $PSScriptRoot "vm-endpoint.local.psd1"
if ((Test-Path -LiteralPath $vmEndpointPath) -and -not $PSBoundParameters.ContainsKey("VmAlias")) {
    $vmEndpoint = Import-PowerShellDataFile -LiteralPath $vmEndpointPath
    if ([string]::IsNullOrWhiteSpace([string]$vmEndpoint.Target)) {
        throw "VM endpoint configuration has no Target: $vmEndpointPath"
    }
    $VmAlias = [string]$vmEndpoint.Target
    if ($null -ne $vmEndpoint.Port) { $sshOptions += @("-p", [string]$vmEndpoint.Port) }
    if (-not [string]::IsNullOrWhiteSpace([string]$vmEndpoint.IdentityFile)) {
        $sshOptions += @("-i", [string]$vmEndpoint.IdentityFile)
    }
}
$artifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { Join-Path $repoRoot "artifacts\ab_tests" } else { $ArtifactRoot }
$artifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)

# Retry is intentionally bound to the build identity captured by the original
# launch.  It may reuse that release, but it must never silently export a newer
# working tree into an existing RunId.
if ($Mode -eq "Retry") {
    if ([string]::IsNullOrWhiteSpace($RunId)) { throw "Retry requires -RunId." }
    $retryStatePath = Join-Path (Join-Path $artifactRoot $RunId) "run_state.json"
    if (-not (Test-Path -LiteralPath $retryStatePath)) { throw "Run state was not found: $retryStatePath" }
    $retryState = Get-Content -LiteralPath $retryStatePath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$retryState.host_export_directory) -or [string]::IsNullOrWhiteSpace([string]$retryState.manifest_sha256)) {
        throw "Run '$RunId' has no reusable immutable build identity."
    }
    $OutputDirectory = [string]$retryState.host_export_directory
    $Scenario = [string]$retryState.scenario
    $ExpectedManifestSha256 = [string]$retryState.manifest_sha256
    $SkipExport = $true
}

$outputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repoRoot "build\test_steam" } else { $OutputDirectory }
$outputDirectory = [System.IO.Path]::GetFullPath($outputDirectory)
$hostExecutable = Join-Path $outputDirectory "GameFactory.console.exe"
if (-not (Test-Path $hostExecutable)) { $hostExecutable = Join-Path $outputDirectory "GameFactory.exe" }

$runId = if ([string]::IsNullOrWhiteSpace($RunId)) {
    "ab_{0}_{1}" -f (Get-Date -Format "yyyyMMdd_HHmmss"), ([Guid]::NewGuid().ToString("N").Substring(0, 4))
}
else {
    $RunId
}
$runDirectory = Join-Path $artifactRoot $runId
$attemptNumber = if ($Attempt -gt 0) { $Attempt } else { 1 }
$artifactDirectory = Join-Path $runDirectory ("attempt_{0:D3}" -f $attemptNumber)
$runtimeDirectory = Join-Path $PSScriptRoot ".runtime"
$localConfigPath = Join-Path $runtimeDirectory "client_config.json"
$localStatusPath = Join-Path $runtimeDirectory "client_status.json"
$localRunnerPath = Join-Path $PSScriptRoot "vm\run_client.ps1"
$hostOutputDirectory = Join-Path $artifactDirectory "host"
$clientOutputDirectory = Join-Path $artifactDirectory "client"
$sessionOutputDirectory = Join-Path $artifactDirectory "session"
$hostGodotLogPath = Join-Path $hostOutputDirectory "godot.log"
$attemptLabel = "attempt_{0:D3}" -f $attemptNumber
$attemptEvidenceId = "{0}_{1}" -f $runId, $attemptLabel
$vmGodotLogPath = "C:/GameFactoryAgent/logs/$runId/$attemptLabel/godot.log"
$clientLiveLogPath = Join-Path $clientOutputDirectory "game.jsonl"
$lastVmLogSyncUtc = [DateTimeOffset]::MinValue
$vmLiveLogPath = $null
$vmExecutable = $null
$resultPath = Join-Path $artifactDirectory "result.json"
$attemptStatePath = Join-Path $artifactDirectory "state.json"
$runStatePath = Join-Path $runDirectory "run_state.json"
$hostProcess = $null
$hostLogTailProcess = $null
$existingRunState = $null
$vmCleanupSucceeded = $false
$netfoxShutdownExpected = $false
$buildHelperTimeoutSeconds = 210
$result = [ordered]@{
    result = "failed"
    test_run_id = $runId
    scenario = $Scenario
    mode = "infrastructure_only"
    layer = "harness"
    stage = "initializing"
    reason = $null
    lobby_id = $null
    build_id = $null
    git_commit = $null
    deepest_completed_stage = $null
    completed_stages = @()
    timings_ms = [ordered]@{}
    cleanup_verified = $false
    infrastructure = [ordered]@{
        vm_health_initial = $null
        vm_restart_attempted = $false
        vm_health_after_restart = $null
        fresh_transport_requested = [bool]$FreshTransport
        host_steam_restart_result = $null
        vm_steam_restart_result = $null
        steam_readiness_result = $null
        steam_process_ready = $false
        steam_ipc_probe_attempted = $false
        steam_ipc_probe_result = $null
    }
    build_mapping = [ordered]@{
        host_directory = $outputDirectory
        vm_build_root = $VmBuildRoot
    }
    started_utc = [DateTimeOffset]::UtcNow.ToString("O")
    completed_utc = $null
}
$runTarget = if ($Scenario -eq "netfox_time_sync") { "netfox" } elseif ($Scenario -eq "netfox_gameplay") { "netfox-gameplay" } elseif ($Scenario -eq "netfox_player_3d") { "netfox-player-3d" } else { "steam-gameplay" }
$scenarioCategory = switch ($Scenario) { "steam_basic" { "ab_test.scenario" } "netfox_time_sync" { "netfox.scenario" } "netfox_gameplay" { "netfox.movement" } "netfox_player_3d" { "netfox.player3d" } default { throw "Unsupported scenario '$Scenario'." } }

New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null

function Write-Harness([string]$Message) {
    Write-Host "[harness][$runId] $Message"
}

function Set-Failure([string]$Layer, [string]$Stage, [string]$Reason) {
    $script:result.layer = $Layer
    $script:result.stage = $Stage
    $script:result.reason = $Reason
    throw "[$Layer/$Stage] $Reason"
}

function Set-Blocked([string]$Layer, [string]$Stage, [string]$Reason) {
    $script:result.result = "blocked"
    $script:result.layer = $Layer
    $script:result.stage = $Stage
    $script:result.reason = $Reason
    throw "[$Layer/$Stage] $Reason"
}

function Complete-Stage([string]$Stage) {
    $script:result.deepest_completed_stage = $Stage
    $script:result.completed_stages += $Stage
    Write-Harness "stage complete: $Stage"
}


function Invoke-ExternalCommand([string]$FilePath, [string[]]$Arguments, [int]$TimeoutSeconds, [string]$Description, [switch]$SuppressOutput) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    # Windows PowerShell uses the .NET Framework ProcessStartInfo, which does
    # not expose ArgumentList. Every harness argument is passed as one quoted
    # token so remote commands remain intact on that runtime as well.
    $startInfo.Arguments = (($Arguments | ForEach-Object {
        '"' + $_.Replace('"', '\"') + '"'
    }) -join ' ')

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        # Process.Kill(bool) is not available in Windows PowerShell's .NET
        # Framework runtime. The harness launches ssh/scp directly, so killing
        # the command process is sufficient and remains compatible there.
        $process.Kill()
        $process.WaitForExit()
        [void]$stdoutTask.GetAwaiter().GetResult()
        [void]$stderrTask.GetAwaiter().GetResult()
        throw "$Description timed out after $TimeoutSeconds seconds."
    }

    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if (-not $SuppressOutput) {
        if (-not [string]::IsNullOrWhiteSpace($stdout)) { Write-Host $stdout.TrimEnd() }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) { Write-Warning $stderr.TrimEnd() }
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        StandardOutput = $stdout
        StandardError = $stderr
    }
}

function Invoke-Vm([string]$Command, [string]$Stage) {
    $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, $Command)) $externalCommandTimeoutSeconds "VM command for stage '$Stage'"
    if ($invocation.ExitCode -ne 0) {
        Set-Failure "vm_control" $Stage "VM command failed with exit code $($invocation.ExitCode)."
    }
}

function Invoke-VmPowerShell([string]$Script, [string]$Stage) {
    $scriptWithPreferences = '$ProgressPreference = ''SilentlyContinue''; ' + $Script
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($scriptWithPreferences))
    # On Windows, ssh.exe joins its arguments after the host into the remote
    # command. Supplying PowerShell as individual arguments is reliable for
    # ProcessStartInfo; passing one quoted compound command silently fails.
    $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) $externalCommandTimeoutSeconds "VM PowerShell command for stage '$Stage'"
    if ($invocation.ExitCode -ne 0) {
        $detail = ($invocation.StandardError, $invocation.StandardOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
        Set-Failure "vm_control" $Stage "VM PowerShell command failed with exit code $($invocation.ExitCode): $detail"
    }
}

function Stop-VmClientBestEffort {
    $script = '$ProgressPreference = ''SilentlyContinue''; $deadline = (Get-Date).AddSeconds(10); do { $processes = @(Get-Process -Name GameFactory -ErrorAction SilentlyContinue); if ($processes.Count -eq 0) { exit 0 }; $processes | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 200 } while ((Get-Date) -lt $deadline); exit 9'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    try {
        $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) $externalCommandTimeoutSeconds "VM client cleanup"
        $exitCode = $invocation.ExitCode
    }
    catch {
        $exitCode = -1
        Write-Warning "[harness][$runId] VM client cleanup timed out: $($_.Exception.Message)"
    }
    if ($exitCode -ne 0) {
        $script:vmCleanupSucceeded = $false
        Write-Warning "[harness][$runId] VM client cleanup returned exit code $exitCode."
    }
    else { $script:vmCleanupSucceeded = $true }
}

function Assert-NoStaleProcesses {
    if (@(Get-Process -Name GameFactory -ErrorAction SilentlyContinue).Count -gt 0) {
        Set-Failure "harness" "preflight_cleanup" "A local GameFactory process remained after cleanup."
    }
    $script = '$ProgressPreference = ''SilentlyContinue''; if (@(Get-Process -Name GameFactory -ErrorAction SilentlyContinue).Count -gt 0) { exit 9 } else { exit 0 }'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) $externalCommandTimeoutSeconds "VM process-state preflight"
    if ($invocation.ExitCode -eq 9) { Set-Failure "harness" "preflight_cleanup" "A VM GameFactory process remained after cleanup." }
    if ($invocation.ExitCode -ne 0) { Set-Blocked "vm_control" "preflight_reachability" "Could not verify VM process state; SSH exited with code $($invocation.ExitCode)." }
}

function Get-HostSteamReadiness {
    $steam = @(Get-Process -Name steam -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -ne 0 })
    $sessionIds = @($steam | Select-Object -ExpandProperty SessionId -Unique)
    $helpers = @(if ($sessionIds.Count -gt 0) {
        @(Get-Process -Name steamwebhelper -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -in $sessionIds })
    } else { @() })
    [ordered]@{
        steam_process_count = $steam.Count
        interactive_session_ids = $sessionIds
        steam_process_ids = @($steam | Select-Object -ExpandProperty Id | Sort-Object)
        steamwebhelper_count = $helpers.Count
        steamwebhelper_process_ids = @($helpers | Select-Object -ExpandProperty Id | Sort-Object)
        steam_path = if ($steam.Count -gt 0) { [string]$steam[0].Path } else { $null }
        online_status = "not_deterministically_available"
    }
}

function Wait-ForHostSteamReadiness([int]$TimeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $readiness = Get-HostSteamReadiness
        if ($readiness.steam_process_count -ge 1 -and $readiness.steamwebhelper_count -ge 1) { return $readiness }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw "Host Steam did not reach the interactive client + steamwebhelper readiness boundary within $TimeoutSeconds seconds."
}

function Get-TextSinceOffset([string]$Path, [long]$Offset) {
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($stream.Length -lt $Offset) { $Offset = 0 }
        [void]$stream.Seek($Offset, [System.IO.SeekOrigin]::Begin)
        $reader = [System.IO.StreamReader]::new($stream)
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Wait-ForHostSteamStableReadiness([string]$SteamPath, [long]$ConnectionLogOffset, [int]$TimeoutSeconds = 90, [int]$StableSeconds = 10) {
    $connectionLogPath = Join-Path (Join-Path (Split-Path -Parent $SteamPath) "logs") "connection_log.txt"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $loggedOnObserved = $false
    $stableSnapshot = $null
    $stableSince = $null
    do {
        $readiness = Get-HostSteamReadiness
        $newLogText = Get-TextSinceOffset $connectionLogPath $ConnectionLogOffset
        if ($newLogText -match '\[Logged On,.*RecvMsgClientLogOnResponse\(\).*processing complete') { $loggedOnObserved = $true }
        $processSet = "steam=$($readiness.steam_process_ids -join ',');helpers=$($readiness.steamwebhelper_process_ids -join ',');sessions=$($readiness.interactive_session_ids -join ',')"
        $present = $readiness.steam_process_count -ge 1 -and $readiness.steamwebhelper_count -ge 1
        if ($present -and $loggedOnObserved) {
            if ($processSet -ne $stableSnapshot) {
                $stableSnapshot = $processSet
                $stableSince = Get-Date
            }
            elseif (((Get-Date) - $stableSince).TotalSeconds -ge $StableSeconds) {
                return [ordered]@{
                    readiness = $readiness
                    connection_log_path = $connectionLogPath
                    logged_on_log_signal = $true
                    stable_process_set = $processSet
                    stable_seconds = $StableSeconds
                    observed_utc = [DateTimeOffset]::UtcNow.ToString("O")
                }
            }
        }
        else {
            $stableSnapshot = $null
            $stableSince = $null
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)
    throw "Host Steam did not reach the post-restart Logged On + stable interactive process-set readiness boundary within $TimeoutSeconds seconds."
}

function Restart-HostSteam {
    $before = Get-HostSteamReadiness
    $steamPath = [string]$before.steam_path
    if ([string]::IsNullOrWhiteSpace($steamPath) -or -not (Test-Path -LiteralPath $steamPath)) {
        $steamPath = "D:\\steam\\steam.exe"
    }
    if (-not (Test-Path -LiteralPath $steamPath)) { throw "Could not locate a host Steam executable for FreshTransport." }
    $connectionLogPath = Join-Path (Join-Path (Split-Path -Parent $steamPath) "logs") "connection_log.txt"
    $connectionLogOffset = if (Test-Path -LiteralPath $connectionLogPath) { ([System.IO.FileInfo]$connectionLogPath).Length } else { 0 }

    Get-Process -Name steamwebhelper, steam -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    $exitDeadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 500 } while (@(Get-Process -Name steam -ErrorAction SilentlyContinue).Count -gt 0 -and (Get-Date) -lt $exitDeadline)
    if (@(Get-Process -Name steam -ErrorAction SilentlyContinue).Count -gt 0) { throw "Host Steam processes did not exit before restart." }
    Start-Process -FilePath $steamPath | Out-Null
    $ready = Wait-ForHostSteamStableReadiness -SteamPath $steamPath -ConnectionLogOffset $connectionLogOffset
    return [ordered]@{ result = "passed"; steam_path = $steamPath; readiness = $ready.readiness; logged_on_log_signal = $ready.logged_on_log_signal; stable_process_set = $ready.stable_process_set; stable_seconds = $ready.stable_seconds; connection_log_path = $ready.connection_log_path; observed_utc = $ready.observed_utc }
}

function Restart-VmSteam {
    $nonce = [Guid]::NewGuid().ToString("N")
    $remoteScript = @"
`$ErrorActionPreference = 'Stop'
`$interactiveUser = (Get-CimInstance Win32_ComputerSystem).UserName
`$explorer = @(Get-Process -Name explorer -ErrorAction SilentlyContinue | Where-Object { `$_.SessionId -ne 0 } | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace([string]`$interactiveUser) -or `$explorer.Count -ne 1) { throw 'No interactive desktop session is available for VM Steam restart.' }
`$sessionId = [int]`$explorer[0].SessionId
`$steam = @(Get-Process -Name steam -ErrorAction SilentlyContinue | Where-Object { `$_.SessionId -eq `$sessionId } | Select-Object -First 1)
`$steamPath = if (`$steam.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace([string]`$steam[0].Path)) { [string]`$steam[0].Path } else { 'C:\Program Files (x86)\Steam\steam.exe' }
if (-not (Test-Path -LiteralPath `$steamPath)) { throw "Steam executable was not found: `$steamPath" }
Get-Process -Name steamwebhelper, steam -ErrorAction SilentlyContinue | Where-Object { `$_.SessionId -eq `$sessionId } | Stop-Process -Force -ErrorAction SilentlyContinue
`$deadline = (Get-Date).AddSeconds(20)
while (@(Get-Process -Name steam -ErrorAction SilentlyContinue | Where-Object { `$_.SessionId -eq `$sessionId }).Count -gt 0 -and (Get-Date) -lt `$deadline) { Start-Sleep -Milliseconds 500 }
if (@(Get-Process -Name steam -ErrorAction SilentlyContinue | Where-Object { `$_.SessionId -eq `$sessionId }).Count -gt 0) { throw 'VM Steam processes did not exit before restart.' }
`$taskName = 'GameFactorySteamFresh_$nonce'
`$action = New-ScheduledTaskAction -Execute `$steamPath
`$principal = New-ScheduledTaskPrincipal -UserId `$interactiveUser -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName `$taskName -Action `$action -Principal `$principal -Force | Out-Null
try { Start-ScheduledTask -TaskName `$taskName; Start-Sleep -Seconds 2 } finally { Unregister-ScheduledTask -TaskName `$taskName -Confirm:`$false -ErrorAction SilentlyContinue }
`$readyDeadline = (Get-Date).AddSeconds(60)
do {
    `$runningSteam = @(Get-Process -Name steam -ErrorAction SilentlyContinue | Where-Object { `$_.SessionId -eq `$sessionId })
    `$helpers = @(Get-Process -Name steamwebhelper -ErrorAction SilentlyContinue | Where-Object { `$_.SessionId -eq `$sessionId })
    if (`$runningSteam.Count -ge 1 -and `$helpers.Count -ge 1) {
        [ordered]@{ marker='GAMEFACTORY_VM_STEAM_RESTART'; nonce='$nonce'; result='passed'; interactive_user=`$interactiveUser; session_id=`$sessionId; steam_path=`$steamPath; steam_process_count=`$runningSteam.Count; steamwebhelper_count=`$helpers.Count; online_status='not_deterministically_available' } | ConvertTo-Json -Compress
        exit 0
    }
    Start-Sleep -Seconds 1
} while ((Get-Date) -lt `$readyDeadline)
throw 'VM Steam did not reach the interactive client + steamwebhelper readiness boundary within 60 seconds.'
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($remoteScript))
    $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) 100 "VM interactive Steam restart" -SuppressOutput
    if ($invocation.ExitCode -ne 0) { throw "VM Steam restart failed: $($invocation.StandardError.Trim())" }
    $line = @($invocation.StandardOutput -split "`r?`n" | Where-Object { $_ -match 'GAMEFACTORY_VM_STEAM_RESTART' } | Select-Object -Last 1)
    if ($line.Count -ne 1) { throw "VM Steam restart did not return its readiness marker." }
    $status = $line[0] | ConvertFrom-Json
    if ($status.marker -ne 'GAMEFACTORY_VM_STEAM_RESTART' -or $status.nonce -ne $nonce -or $status.result -ne 'passed') { throw "VM Steam restart marker did not match the requested interactive-session contract." }
    return $status
}

function Invoke-FreshTransportPreparation {
    $script:result.infrastructure.fresh_transport_requested = $true
    try {
        $hostRestart = Restart-HostSteam
        $script:result.infrastructure.host_steam_restart_result = $hostRestart
    }
    catch {
        $script:result.infrastructure.host_steam_restart_result = [ordered]@{ result = "failed"; reason = $_.Exception.Message; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
        Set-Blocked "steam" "fresh_transport_host" "FreshTransport could not restart host Steam: $($_.Exception.Message)"
    }
    try {
        $vm = Restart-VmSteam
        $script:result.infrastructure.vm_steam_restart_result = $vm
        $health = Test-VmEndpointHealth
        $script:result.infrastructure.steam_readiness_result = [ordered]@{ result = "passed"; host = $script:result.infrastructure.host_steam_restart_result.readiness; vm = $vm; vm_health_task_state = $health.task_state; online_status = "not_deterministically_available"; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
        $script:result.infrastructure.steam_process_ready = $true
    }
    catch {
        $script:result.infrastructure.vm_steam_restart_result = if ($null -eq $script:result.infrastructure.vm_steam_restart_result) { [ordered]@{ result = "failed"; reason = $_.Exception.Message; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") } } else { $script:result.infrastructure.vm_steam_restart_result }
        $script:result.infrastructure.steam_readiness_result = [ordered]@{ result = "failed"; reason = $_.Exception.Message; online_status = "not_deterministically_available"; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
        Set-Blocked "steam" "fresh_transport_vm" "FreshTransport could not establish VM Steam/session readiness: $($_.Exception.Message)"
    }
}

function Invoke-HostSteamIpcProbe {
    $script:result.infrastructure.steam_ipc_probe_attempted = $true
    $probeDirectory = Join-Path $hostOutputDirectory "steam_ipc_probe"
    $probeLogPath = Join-Path $probeDirectory "godot.log"
    $probeProfileRoot = Join-Path $probeDirectory "runtime_profile"
    $probeAppData = Join-Path $probeProfileRoot "AppData\\Roaming"
    $probeLocalAppData = Join-Path $probeProfileRoot "AppData\\Local"
    New-Item -ItemType Directory -Force -Path $probeDirectory, $probeAppData, $probeLocalAppData | Out-Null
    Remove-Item -LiteralPath $probeLogPath -Force -ErrorAction SilentlyContinue
    $hostSteam = Get-HostSteamReadiness
    if ($hostSteam.steam_process_count -lt 1 -or $hostSteam.steamwebhelper_count -lt 1) {
        $script:result.infrastructure.steam_ipc_probe_result = [ordered]@{ result = "failed"; scope = "host"; reason = "Host Steam process/session readiness was lost before IPC probe."; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
        Set-Blocked "steam" "fresh_transport_ipc" "Host Steam process/session readiness was lost before IPC probe."
    }
    $previousAppData = $env:APPDATA
    $previousLocalAppData = $env:LOCALAPPDATA
    $probeProcess = $null
    try {
        $env:APPDATA = $probeAppData
        $env:LOCALAPPDATA = $probeLocalAppData
        $probeProcess = Start-Process -FilePath $hostExecutable -ArgumentList @("--headless", "--log-file", $probeLogPath, "--run=steam", "--test-run-id=$attemptEvidenceId-ipc") -WorkingDirectory $outputDirectory -PassThru
    }
    finally { $env:APPDATA = $previousAppData; $env:LOCALAPPDATA = $previousLocalAppData }
    try {
        $deadline = (Get-Date).AddSeconds(30)
        do {
            if (Test-Path -LiteralPath $probeLogPath) {
                $text = Get-Content -LiteralPath $probeLogPath -Raw -ErrorAction SilentlyContinue
                if ($text -match '\[steam\.session\] ready') {
                    $script:result.infrastructure.steam_ipc_probe_result = [ordered]@{ result = "ready"; scope = "host"; log_path = $probeLogPath; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
                    return
                }
                if ($text -match '\[steam\.session\] initialization_failed|Initializing -> Failed') {
                    $reason = (($text -split "`r?`n" | Where-Object { $_ -match 'initialization_failed|Initializing -> Failed' } | Select-Object -Last 1) -join '')
                    $script:result.infrastructure.steam_ipc_probe_result = [ordered]@{ result = "failed"; scope = "host"; log_path = $probeLogPath; reason = $reason; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
                    Set-Blocked "steam" "fresh_transport_ipc" "Host GameFactory/GodotSteam IPC probe failed: $reason"
                }
            }
            Start-Sleep -Milliseconds 250
        } while ((Get-Date) -lt $deadline)
        $script:result.infrastructure.steam_ipc_probe_result = [ordered]@{ result = "timeout"; scope = "host"; log_path = $probeLogPath; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
        Set-Blocked "steam" "fresh_transport_ipc" "Host GameFactory/GodotSteam IPC probe timed out after 30 seconds."
    }
    finally { if ($null -ne $probeProcess -and -not $probeProcess.HasExited) { Stop-Process -Id $probeProcess.Id -Force -ErrorAction SilentlyContinue } }
}

function Write-ClientConfig([string]$Mode, [string[]]$Arguments, [object]$Manifest, [string]$ManifestHash, [string]$VmExecutable) {
    $clientConfig = [ordered]@{
        mode = $Mode
        executable = $VmExecutable
        arguments = $Arguments
        expected_build_id = [string]$Manifest.build_id
        expected_manifest_sha256 = $ManifestHash
    }
    $temporaryConfigPath = "$localConfigPath.tmp"
    $clientConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryConfigPath -Encoding utf8
    Move-Item -LiteralPath $temporaryConfigPath -Destination $localConfigPath -Force
    $invocation = Invoke-ExternalCommand $scpExecutable ($sshOptions + @($localConfigPath, "${VmAlias}:$VmConfigPath")) $externalCommandTimeoutSeconds "VM client configuration copy"
    if ($invocation.ExitCode -ne 0) { Set-Blocked "vm_control" "client_config_copy" "Could not copy the client configuration to the VM; SCP exited with $($invocation.ExitCode)." }
    Copy-Item -LiteralPath $localConfigPath -Destination (Join-Path $artifactDirectory "client_config_$Mode.json") -Force
}

function Invoke-VmRunner([string]$ExpectedStage, [int]$TimeoutSeconds) {
    Invoke-VmPowerShell "if (Test-Path -LiteralPath '$VmStatusPath') { Remove-Item -LiteralPath '$VmStatusPath' -Force }; exit 0" "status_cleanup"
    Remove-Item -LiteralPath $localStatusPath -Force -ErrorAction SilentlyContinue
    Invoke-VmPowerShell "Start-ScheduledTask -TaskName 'GameFactoryClient'" "client_start"

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $previousErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $statusCopy = Invoke-ExternalCommand $scpExecutable ($sshOptions + @("${VmAlias}:$VmStatusPath", $localStatusPath)) 10 "VM runner-status copy" -SuppressOutput
            $scpExitCode = $statusCopy.ExitCode
        }
        catch {
            $scpExitCode = -1
        }
        $ErrorActionPreference = $previousErrorPreference
        if ($scpExitCode -eq 0 -and (Test-Path -LiteralPath $localStatusPath)) {
            $status = Get-Content -LiteralPath $localStatusPath -Raw | ConvertFrom-Json
            if ($status.result -ne "passed") { Set-Failure "build" "build_parity" ([string]$status.reason) }
            if ($status.stage -ne $ExpectedStage) { Set-Failure "vm_control" "runner_status" "Expected VM runner stage '$ExpectedStage', observed '$($status.stage)'." }
            return $status
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure "vm_control" "runner_status" "Timed out waiting for VM runner stage '$ExpectedStage'."
}

function Get-VmReleaseExecutable([string]$ManifestHash) {
    if ($ManifestHash -notmatch '^[a-f0-9]{64}$') { throw "Manifest hash must be a lowercase SHA-256 value." }
    return (($VmBuildRoot.TrimEnd('/', '\') + "/releases/$ManifestHash/GameFactory.console.exe"))
}

function Test-CurrentExportReusable {
    $manifestPath = Join-Path $outputDirectory "build_manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $hostExecutable)) { return $false }
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifestCommit = [string]$manifest.git_commit
        if ($manifestCommit -notmatch '^[0-9a-f]{40}$') { return $false }

        # An immutable export is defined by its actual runtime inputs, not by
        # documentation or A/B orchestration commits. Compare the manifest's
        # source commit with HEAD while excluding the non-runtime scopes.
        $runtimePathspec = @('.', ':(exclude)docs/**', ':(exclude)tools/ab_test/**')
        & git -C $repoRoot diff --quiet "$manifestCommit..HEAD" -- @runtimePathspec
        if ($LASTEXITCODE -ne 0) { return $false }
        & git -C $repoRoot diff --quiet -- @runtimePathspec
        if ($LASTEXITCODE -ne 0) { return $false }
        & git -C $repoRoot diff --cached --quiet -- @runtimePathspec
        if ($LASTEXITCODE -ne 0) { return $false }
        return $true
    }
    catch { return $false }
}

function Get-VmReleaseCacheState([string]$ManifestHash) {
    if ($ManifestHash -notmatch '^[a-f0-9]{64}$') { throw "Manifest hash must be a lowercase SHA-256 value." }

    $vmRoot = $VmBuildRoot.TrimEnd('/', '\') -replace '/', '\'
    $releaseDirectory = "$vmRoot\releases\$ManifestHash"
    $remoteScript = @"
`$executable = Join-Path '$releaseDirectory' 'GameFactory.exe'
`$manifestPath = Join-Path '$releaseDirectory' 'build_manifest.json'
if (-not (Test-Path -LiteralPath `$executable) -or -not (Test-Path -LiteralPath `$manifestPath)) {
    [Console]::Out.Write('miss')
    exit 0
}
`$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath `$manifestPath).Hash.ToLowerInvariant()
if (`$actual -ne '$ManifestHash') {
    [Console]::Out.Write('mismatch')
    exit 0
}
`$markerPath = Join-Path '$vmRoot\parity' '$ManifestHash.json'
if (Test-Path -LiteralPath `$markerPath) {
    try {
        `$marker = Get-Content -LiteralPath `$markerPath -Raw | ConvertFrom-Json
        `$buildId = [string](Get-Content -LiteralPath `$manifestPath -Raw | ConvertFrom-Json).build_id
        if ([string]`$marker.manifest_sha256 -eq '$ManifestHash' -and [string]`$marker.build_id -eq `$buildId) {
            [Console]::Out.Write('verified_hit')
            exit 0
        }
    }
    catch { }
}
[Console]::Out.Write('hit')
exit 0
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($remoteScript))
    $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) $externalCommandTimeoutSeconds "VM cached-release check" -SuppressOutput
    if ($invocation.ExitCode -ne 0) {
        Set-Blocked "vm_control" "build_cache_check" "Could not inspect the VM release cache; SSH exited with code $($invocation.ExitCode)."
    }
    return $invocation.StandardOutput.Trim()
}

function Invoke-VmBuildParity([string]$ManifestHash, [string]$ExpectedBuildId, [bool]$FullFileHashVerification) {
    $vmRoot = $VmBuildRoot.TrimEnd('/', '\') -replace '/', '\'
    $releaseDirectory = "$vmRoot\releases\$ManifestHash"
    $fullVerificationLiteral = if ($FullFileHashVerification) { '$true' } else { '$false' }
    $remoteScript = @"
`$ErrorActionPreference = 'Stop'
`$timer = [Diagnostics.Stopwatch]::StartNew()
`$exportDirectory = '$releaseDirectory'
`$manifestPath = Join-Path `$exportDirectory 'build_manifest.json'
`$executable = Join-Path `$exportDirectory 'GameFactory.exe'
if (-not (Test-Path -LiteralPath `$manifestPath) -or -not (Test-Path -LiteralPath `$executable)) { throw 'Cached release is missing its manifest or executable.' }
`$manifest = Get-Content -LiteralPath `$manifestPath -Raw | ConvertFrom-Json
`$manifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath `$manifestPath).Hash.ToLowerInvariant()
if (`$manifestHash -ne '$ManifestHash') { throw "Manifest hash mismatch. Expected '$ManifestHash', observed '`$manifestHash'." }
if ([string]`$manifest.build_id -ne '$ExpectedBuildId') { throw "Build ID mismatch. Expected '$ExpectedBuildId', observed '`$(`$manifest.build_id)'." }
`$fullFileHashVerification = $fullVerificationLiteral
foreach (`$file in `$manifest.files) {
    `$relativePath = ([string]`$file.path).Replace('/', '\')
    `$path = Join-Path `$exportDirectory `$relativePath
    if (-not (Test-Path -LiteralPath `$path)) { throw "Manifest file is missing: `$relativePath" }
    if ((Get-Item -LiteralPath `$path).Length -ne [long]`$file.size) { throw "Manifest size mismatch: `$relativePath" }
    if (`$fullFileHashVerification) {
        `$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath `$path).Hash.ToLowerInvariant()
        if (`$hash -ne [string]`$file.sha256) { throw "Manifest hash mismatch: `$relativePath" }
    }
}
if (`$fullFileHashVerification) {
    `$parityDirectory = Join-Path '$vmRoot' 'parity'
    New-Item -ItemType Directory -Force -Path `$parityDirectory | Out-Null
    `$markerPath = Join-Path `$parityDirectory '$ManifestHash.json'
    `$temporaryMarkerPath = "`$markerPath.tmp"
    [ordered]@{ schema_version = 1; build_id = [string]`$manifest.build_id; manifest_sha256 = `$manifestHash; file_count = [int]`$manifest.file_count; verified_utc = [DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json | Set-Content -LiteralPath `$temporaryMarkerPath -Encoding utf8
    Move-Item -LiteralPath `$temporaryMarkerPath -Destination `$markerPath -Force
}
[ordered]@{ result = 'passed'; stage = 'build_parity'; build_id = [string]`$manifest.build_id; manifest_sha256 = `$manifestHash; file_count = [int]`$manifest.file_count; parity_mode = if (`$fullFileHashVerification) { 'full' } else { 'cached' }; parity_verification_ms = `$timer.ElapsedMilliseconds } | ConvertTo-Json -Compress
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($remoteScript))
    $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) $BuildStageTimeoutSeconds "VM build parity verification" -SuppressOutput
    if ($invocation.ExitCode -ne 0) {
        $detail = ($invocation.StandardError, $invocation.StandardOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
        Set-Failure "build" "build_parity" "VM build parity verification failed: $detail"
    }
    try { return ($invocation.StandardOutput | ConvertFrom-Json) }
    catch { Set-Failure "build" "build_parity" "VM build parity verification returned invalid JSON: $($invocation.StandardOutput)" }
}

function Stage-VmBuild([string]$ManifestHash) {
    if ($ManifestHash -notmatch '^[a-f0-9]{64}$') { Set-Failure "build" "stage_preflight" "Manifest hash must be a lowercase SHA-256 value." }

    $vmRoot = $VmBuildRoot.TrimEnd('/', '\') -replace '/', '\'
    $incomingDirectory = "$vmRoot\.incoming\$ManifestHash"
    $incomingArchive = "$vmRoot\.incoming\$ManifestHash.zip"
    $releaseDirectory = "$vmRoot\releases\$ManifestHash"
    $localArchive = Join-Path $runtimeDirectory "$ManifestHash.vm-stage.zip"
    $remoteArchive = $incomingArchive -replace '\\', '/'
    $remoteExecutable = Get-VmReleaseExecutable $ManifestHash

    Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($outputDirectory, $localArchive, [System.IO.Compression.CompressionLevel]::Fastest, $false)

        $prepare = @"
`$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path '$vmRoot\.incoming', '$vmRoot\releases' | Out-Null
Remove-Item -LiteralPath '$incomingDirectory', '$incomingArchive' -Force -Recurse -ErrorAction SilentlyContinue
exit 0
"@
        Invoke-VmPowerShell $prepare "build_stage_prepare"
        $copy = Invoke-ExternalCommand $scpExecutable ($sshOptions + @($localArchive, "${VmAlias}:$remoteArchive")) $BuildStageTimeoutSeconds "VM build archive copy"
        if ($copy.ExitCode -ne 0) { Set-Blocked "vm_control" "build_stage_copy" "Could not copy the immutable build archive to the VM; SCP exited with $($copy.ExitCode)." }

        $install = @"
`$ErrorActionPreference = 'Stop'
Expand-Archive -LiteralPath '$incomingArchive' -DestinationPath '$incomingDirectory' -Force
`$manifestPath = Join-Path '$incomingDirectory' 'build_manifest.json'
if (-not (Test-Path -LiteralPath `$manifestPath)) { throw 'Staged build has no build_manifest.json.' }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$manifestPath).Hash.ToLowerInvariant() -ne '$ManifestHash') { throw 'Staged build manifest hash does not match the host manifest.' }
Remove-Item -LiteralPath '$releaseDirectory' -Force -Recurse -ErrorAction SilentlyContinue
Move-Item -LiteralPath '$incomingDirectory' -Destination '$releaseDirectory' -Force
Remove-Item -LiteralPath '$incomingArchive' -Force -ErrorAction SilentlyContinue
exit 0
"@
        Invoke-VmPowerShell $install "build_stage_install"
    }
    finally {
        Remove-Item -LiteralPath $localArchive -Force -ErrorAction SilentlyContinue
    }

    $script:result.build_mapping["vm_release_directory"] = Split-Path -Parent $remoteExecutable
    $script:result.build_mapping["vm_executable"] = $remoteExecutable
    return $remoteExecutable
}

function Stop-TestProcesses {
    if ($null -ne $script:hostProcess -and -not $script:hostProcess.HasExited) {
        Write-Harness "stopping host process $($script:hostProcess.Id)"
        Stop-Process -Id $script:hostProcess.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $script:hostProcess.Id -Timeout 10 -ErrorAction SilentlyContinue
    }

    Stop-VmClientBestEffort
    if ($null -ne $script:hostLogTailProcess -and -not $script:hostLogTailProcess.HasExited) {
        Stop-Process -Id $script:hostLogTailProcess.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $script:hostLogTailProcess.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
    $localStopped = @(Get-Process -Name GameFactory -ErrorAction SilentlyContinue).Count -eq 0
    $script:result.cleanup_verified = $localStopped -and $script:vmCleanupSucceeded
}

function Sync-VmRunLog {
    # Client events are written inside its immutable staged release, not the
    # host output tree. Poll that log during the attempt so client checkpoints
    # are observable before teardown.
    if ([string]::IsNullOrWhiteSpace([string]$script:vmExecutable) -or ([DateTimeOffset]::UtcNow - $script:lastVmLogSyncUtc).TotalMilliseconds -lt 1000) { return }
    $script:lastVmLogSyncUtc = [DateTimeOffset]::UtcNow
    try {
        if ($null -eq $script:vmLiveLogPath) {
            $remoteRunsDirectory = (Join-Path (Split-Path -Parent $script:vmExecutable) "logs\runs") -replace '/', '\\'
            $remoteScript = "`$log = Get-ChildItem -LiteralPath '$remoteRunsDirectory' -Directory -Filter '*_$attemptEvidenceId' -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | ForEach-Object { Join-Path `$_.FullName 'game.jsonl' } | Where-Object { Test-Path -LiteralPath `$_ } | Select-Object -First 1; if (`$null -ne `$log) { [Console]::Out.Write(`$log.Replace('\', '/')) }"
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($remoteScript))
            $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) 10 "VM live-log discovery" -SuppressOutput
            if ($invocation.ExitCode -ne 0) { return }
            $match = [regex]::Match($invocation.StandardOutput, '(?m)^[A-Za-z]:/.*?/game\.jsonl')
            if (-not $match.Success) { return }
            $script:vmLiveLogPath = $match.Value
        }

        # Copy instead of serializing the open JSONL file through PowerShell.
        # SCP preserves exact bytes and avoids CLIXML/progress records corrupting
        # the stream that the harness parses for client checkpoints.
        $remoteSource = '{0}:{1}' -f $VmAlias, $script:vmLiveLogPath
        $copy = Invoke-ExternalCommand $scpExecutable ($sshOptions + @($remoteSource, $clientLiveLogPath)) 10 "VM live-log copy" -SuppressOutput
        if ($copy.ExitCode -ne 0) { return }
    }
    catch { }
}

function Get-RunLogFiles {
    Sync-VmRunLog
    $runsDirectory = Join-Path $outputDirectory "logs\runs"
    $files = @(Get-ChildItem -Path $runsDirectory -Directory -Filter "*_$attemptEvidenceId" -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName "game.jsonl" } |
        Where-Object { Test-Path $_ })
    if (Test-Path -LiteralPath $clientLiveLogPath) { $files += $clientLiveLogPath }
    return $files
}

function Get-LogEntries {
    $entries = @()
    foreach ($path in Get-RunLogFiles) {
        foreach ($line in Get-Content -Path $path -ErrorAction SilentlyContinue) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $entry = $line | ConvertFrom-Json
                $entry | Add-Member -NotePropertyName __path -NotePropertyValue $path -Force
                $entries += $entry
            }
            catch { }
        }
    }

    $attemptStart = [DateTimeOffset]::Parse([string]$result.started_utc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
    return @($entries | Where-Object {
        if ([string]$_.RunId -ne $attemptEvidenceId) { return $false }
        try { return (Get-LogUtc $_) -ge $attemptStart } catch { return $false }
    })
}

function Find-LogEvent([string]$Category, [string]$Event, [string]$Role) {
    foreach ($entry in Get-LogEntries) {
        if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
        if ($Role -and $entry.Fields.role -ne $Role) { continue }
        return $entry
    }
    return $null
}

function Assert-NoTerminalStartupFailure {
    $steamInitializationFailure = Get-LogEntries | Where-Object {
        $_.Category -eq "steam.session" -and $_.Event -eq "state_changed" -and $_.Fields.next -eq "Failed"
    } | Select-Object -First 1
    if ($null -ne $steamInitializationFailure) {
        $probeFailure = Get-LogEntries | Where-Object {
            $_.Category -eq "gameplay.probe" -and $_.Event -eq "initialization_failed"
        } | Select-Object -First 1
        $reason = if ($null -ne $probeFailure -and -not [string]::IsNullOrWhiteSpace([string]$probeFailure.Message)) {
            [string]$probeFailure.Message
        }
        else {
            "SteamSession entered Failed during initialization."
        }
        Set-Failure "steam" "initialization" $reason
    }
}

function Wait-ForLogEvent([string]$Category, [string]$Event, [string]$Role, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        $entry = Find-LogEvent $Category $Event $Role
        if ($null -ne $entry) { return $entry }
        $connectionFailed = Find-LogEvent "ab_test.scenario" "godot_connection_failed" "client"
        if ($null -eq $connectionFailed) { $connectionFailed = Find-LogEvent "netfox.scenario" "godot_connection_failed" "client" }
        if ($null -eq $connectionFailed) { $connectionFailed = Find-LogEvent "netfox.gameplay" "godot_connection_failed" "client" }
        if ($null -eq $connectionFailed) { $connectionFailed = Find-LogEvent "netfox.movement" "godot_connection_failed" "client" }
        if ($null -ne $connectionFailed) { Set-Failure "godot_multiplayer" "godot_signals" "Godot emitted ConnectionFailed." }
        $serverDisconnected = Find-LogEvent "ab_test.scenario" "godot_server_disconnected" "client"
        if ($null -eq $serverDisconnected) { $serverDisconnected = Find-LogEvent "netfox.scenario" "godot_server_disconnected" "client" }
        if ($null -eq $serverDisconnected) { $serverDisconnected = Find-LogEvent "netfox.gameplay" "godot_server_disconnected" "client" }
        if ($null -eq $serverDisconnected) { $serverDisconnected = Find-LogEvent "netfox.movement" "godot_server_disconnected" "client" }
        if ($null -ne $serverDisconnected -and -not $script:netfoxShutdownExpected) { Set-Failure "godot_multiplayer" "godot_signals" "Godot emitted ServerDisconnected." }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event ($Role)."
}

function Wait-ForLogEventAfter([string]$Category, [string]$Event, [string]$Role, [long]$AfterElapsedMilliseconds, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
            if ($Role -and $entry.Fields.role -ne $Role) { continue }
            if ([long]$entry.ElapsedMilliseconds -gt $AfterElapsedMilliseconds) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event after elapsed=$AfterElapsedMilliseconds."
}

function Wait-ForLogFieldValue([string]$Category, [string]$Event, [string]$Role, [string]$Field, [string]$Value, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
            if ($Role -and $entry.Fields.role -ne $Role) { continue }
            if ($entry.Fields.$Field -eq $Value) { return $entry }
        }
        $nativeDisconnected = Get-LogEntries | Where-Object {
            $_.Category -eq "steam.peer_status" -and $_.Event -eq "changed" -and
            $_.Fields.role -eq "client" -and $_.Fields.connection_status -eq "Disconnected"
        } | Select-Object -First 1
        if ($null -ne $nativeDisconnected) { Set-Failure "steam_peer" "native_handshake" "The client native peer changed to Disconnected before Godot connected." }
        $connectionFailed = Find-LogEvent "ab_test.scenario" "godot_connection_failed" "client"
        if ($null -eq $connectionFailed) { $connectionFailed = Find-LogEvent "netfox.scenario" "godot_connection_failed" "client" }
        if ($null -ne $connectionFailed) { Set-Failure "godot_multiplayer" "godot_signals" "Godot emitted ConnectionFailed." }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event $Field=$Value ($Role)."
}

function Wait-ForLogFieldValueAfter([string]$Category, [string]$Event, [string]$Role, [string]$Field, [string]$Value, [long]$AfterElapsedMilliseconds, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
            if ($Role -and $entry.Fields.role -ne $Role) { continue }
            if ($entry.Fields.$Field -ne $Value) { continue }
            if ([long]$entry.ElapsedMilliseconds -gt $AfterElapsedMilliseconds) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event $Field=$Value after elapsed=$AfterElapsedMilliseconds ($Role)."
}

function Get-LogUtc([object]$Entry) {
    return [DateTimeOffset]::Parse(
        [string]$Entry.Utc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind)
}

function Wait-ForLogEventAfterUtc([string]$Category, [string]$Event, [string]$Role, [DateTimeOffset]$AfterUtc, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
            if ($Role -and $entry.Fields.role -ne $Role) { continue }
            if ((Get-LogUtc $entry) -gt $AfterUtc) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event after utc=$AfterUtc ($Role)."
}

function Wait-ForLogFieldValueAfterUtc([string]$Category, [string]$Event, [string]$Role, [string]$Field, [string]$Value, [DateTimeOffset]$AfterUtc, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
            if ($Role -and $entry.Fields.role -ne $Role) { continue }
            if ($entry.Fields.$Field -ne $Value) { continue }
            if ((Get-LogUtc $entry) -gt $AfterUtc) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event $Field=$Value after utc=$AfterUtc ($Role)."
}

function Wait-ForLogFieldsAfterUtc([string]$Category, [string]$Event, [string]$Role, [hashtable]$ExpectedFields, [DateTimeOffset]$AfterUtc, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        if ($Category -eq "carry" -and $ExpectedFields.ContainsKey("item_network_object_id") -and $ExpectedFields.ContainsKey("holder_network_object_id")) {
            Assert-NoUnexpectedCarryHolderAfterUtc ([string]$ExpectedFields["item_network_object_id"]) ([string]$ExpectedFields["holder_network_object_id"]) $AfterUtc $Stage
        }
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
            if ($Role -and $entry.Fields.role -ne $Role) { continue }
            if ((Get-LogUtc $entry) -le $AfterUtc) { continue }
            $matches = $true
            foreach ($field in $ExpectedFields.Keys) {
                if ([string]$entry.Fields.$field -ne [string]$ExpectedFields[$field]) {
                    $matches = $false
                    break
                }
            }
            if ($matches) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    $expected = ($ExpectedFields.Keys | ForEach-Object { "$_=$($ExpectedFields[$_])" }) -join ", "
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event fields [$expected] after utc=$AfterUtc ($Role)."
}

function Assert-NoUnexpectedCarryHolderAfterUtc([string]$ItemNetworkObjectId, [string]$ExpectedHolderNetworkObjectId, [DateTimeOffset]$AfterUtc, [string]$Stage) {
    foreach ($entry in Get-LogEntries) {
        if ($entry.Category -ne "carry" -or ($entry.Event -ne "picked_up" -and $entry.Event -ne "state_applied")) { continue }
        if ((Get-LogUtc $entry) -le $AfterUtc) { continue }
        if ([string]$entry.Fields.item_network_object_id -ne $ItemNetworkObjectId) { continue }
        $observedHolder = [string]$entry.Fields.holder_network_object_id
        if ($observedHolder -ne "0" -and $observedHolder -ne $ExpectedHolderNetworkObjectId) {
            Set-Failure "gameplay" $Stage "carryable entered held state for unexpected holder after stage checkpoint (item=$ItemNetworkObjectId expected_holder=$ExpectedHolderNetworkObjectId observed_holder=$observedHolder)."
        }
    }
}

function Wait-ForCarryFollowAfterUtc([string]$Role, [string]$ItemNetworkObjectId, [string]$HolderNetworkObjectId, [DateTimeOffset]$AfterUtc, [int]$TimeoutSeconds, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        Assert-NoUnexpectedCarryHolderAfterUtc $ItemNetworkObjectId $HolderNetworkObjectId $AfterUtc $Stage
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne "carry" -or $entry.Event -ne "follow_observed") { continue }
            if ($entry.Fields.role -ne $Role -or (Get-LogUtc $entry) -le $AfterUtc) { continue }
            if ([string]$entry.Fields.item_network_object_id -ne $ItemNetworkObjectId) { continue }
            if ([string]$entry.Fields.holder_network_object_id -ne $HolderNetworkObjectId) { continue }
            $anchorDistance = [double]::Parse([string]$entry.Fields.anchor_distance, [Globalization.CultureInfo]::InvariantCulture)
            if ($anchorDistance -le 0.1) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure "replication" $Stage "Timed out after $TimeoutSeconds seconds waiting for carry follow within 0.1m (item=$ItemNetworkObjectId holder=$HolderNetworkObjectId role=$Role) after utc=$AfterUtc."
}

function Get-VectorDistance([string]$Left, [string]$Right) {
    $pattern = '^\(([-+]?[0-9]*\.?[0-9]+),\s*([-+]?[0-9]*\.?[0-9]+)\)$'
    $leftMatch = [regex]::Match($Left, $pattern)
    $rightMatch = [regex]::Match($Right, $pattern)
    if (-not $leftMatch.Success -or -not $rightMatch.Success) {
        throw "Could not compare movement positions '$Left' and '$Right'."
    }

    $style = [Globalization.NumberStyles]::Float
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $leftX = [double]::Parse($leftMatch.Groups[1].Value, $style, $culture)
    $leftY = [double]::Parse($leftMatch.Groups[2].Value, $style, $culture)
    $rightX = [double]::Parse($rightMatch.Groups[1].Value, $style, $culture)
    $rightY = [double]::Parse($rightMatch.Groups[2].Value, $style, $culture)
    return [Math]::Sqrt([Math]::Pow($leftX - $rightX, 2) + [Math]::Pow($leftY - $rightY, 2))
}

function Wait-ForRemotePresentationChangeAfterUtc([string]$Role, [string]$PlayerId, [string]$BaselineErrorPixels, [DateTimeOffset]$AfterUtc, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $baseline = [double]::Parse($BaselineErrorPixels, [Globalization.CultureInfo]::InvariantCulture)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Assert-NoTerminalStartupFailure
        foreach ($entry in Get-LogEntries) {
            if ($entry.Category -ne "netfox.reconciliation" -or $entry.Event -ne "presentation_sample") { continue }
            if ($entry.Fields.role -ne $Role) { continue }
            if ($entry.Fields.player_id -ne $PlayerId) { continue }
            if ((Get-LogUtc $entry) -le $AfterUtc) { continue }
            $error = [double]::Parse([string]$entry.Fields.presentation_error_pixels, [Globalization.CultureInfo]::InvariantCulture)
            if ([Math]::Abs($error - $baseline) -gt 0.01) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for a changed remote presentation position after utc=$AfterUtc ($Role)."
}

function Copy-RunArtifacts {
    foreach ($path in Get-RunLogFiles) {
        $entries = @()
        try { $entries = @(Get-Content $path | ForEach-Object { $_ | ConvertFrom-Json }) } catch { }
        $isClientRun = @($entries | Where-Object {
            if ($null -eq $_.Fields) { return $false }
            $roleProperty = $_.Fields.PSObject.Properties["role"]
            return $null -ne $roleProperty -and $roleProperty.Value -eq "client"
        }).Count -gt 0
        $role = if ($isClientRun) { "client" } else { "host" }
        $destination = if ($role -eq "client") { $clientOutputDirectory } else { $hostOutputDirectory }
        $runDirectory = Split-Path -Parent $path
        # The polled VM log is already stored directly in $clientOutputDirectory.
        # Copying its parent into that same directory recursively creates
        # client\client\... until the operator is interrupted.
        if (([IO.Path]::GetFullPath($runDirectory)).TrimEnd('\\') -eq ([IO.Path]::GetFullPath($destination)).TrimEnd('\\')) { continue }
        Copy-Item -Path $runDirectory -Destination (Join-Path $destination (Split-Path -Leaf $runDirectory)) -Recurse -Force
    }

    try {
        if ([string]::IsNullOrWhiteSpace([string]$script:vmExecutable)) { return }
        $clientGodotLog = Join-Path $clientOutputDirectory "godot.log"
        $copy = Invoke-ExternalCommand $scpExecutable ($sshOptions + @("${VmAlias}:$vmGodotLogPath", $clientGodotLog)) 10 "VM Godot-log copy" -SuppressOutput
        if ($copy.ExitCode -ne 0) {
            Write-Warning "[harness][$runId] VM Godot log was not available (SCP exit $($copy.ExitCode))."
        }
    }
    catch {
        Write-Warning "[harness][$runId] VM Godot-log collection failed: $($_.Exception.Message)"
    }

    $hostSession = Get-LogEntries | Where-Object { $_.Category -eq "diagnostics.session" -and $_.Event -eq "host_started" } | Select-Object -First 1
    if ($null -ne $hostSession -and $hostSession.Message -match "session=(?<id>[0-9a-fA-F-]+)") {
        $sessionPath = Join-Path $outputDirectory "logs\sessions\$($Matches.id)"
        if (Test-Path $sessionPath) { Copy-Item -Path $sessionPath -Destination $sessionOutputDirectory -Recurse -Force }
    }

    $nativePattern = "SteamMultiplayerPeer|SteamPacketPeer|process_ping|connection|ERR_|WARNING|invalid packet|listen socket|peer"
    $diagnosticPath = Join-Path $artifactDirectory "native_diagnostics.txt"
    $candidateLogs = @(Get-ChildItem -LiteralPath $artifactDirectory -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @(".log", ".jsonl", ".txt") })
    if ($candidateLogs.Count -gt 0) {
        $matches = @($candidateLogs | Select-String -Pattern $nativePattern -CaseSensitive:$false -ErrorAction SilentlyContinue)
        if ($matches.Count -gt 0) { $matches | ForEach-Object { "{0}:{1}: {2}" -f $_.Path, $_.LineNumber, $_.Line } | Set-Content -LiteralPath $diagnosticPath -Encoding utf8 }
        else { "No native diagnostic terms matched." | Set-Content -LiteralPath $diagnosticPath -Encoding utf8 }
    }
}

function Read-JsonFile([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "$Description was not found: $Path" }
    try { return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json) }
    catch { throw "$Description is not valid JSON: $Path ($($_.Exception.Message))" }
}

function Write-JsonFile([string]$Path, [object]$Value) {
    $temporaryPath = "$Path.tmp"
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporaryPath -Encoding utf8
    Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
}

function Get-RunAttempt([object]$RunState, [int]$RequestedAttempt) {
    $attempts = @($RunState.attempts)
    if ($attempts.Count -eq 0) { throw "Run '$runId' does not contain any attempts." }
    $number = if ($RequestedAttempt -gt 0) { $RequestedAttempt } else { [int]$RunState.latest_attempt }
    $match = @($attempts | Where-Object { [int]$_.attempt -eq $number }) | Select-Object -First 1
    if ($null -eq $match) { throw "Attempt $number was not found in run '$runId'." }
    return $match
}

function Set-ExistingAttemptContext([object]$RunState, [object]$AttemptState) {
    $script:Scenario = [string]$RunState.scenario
    $script:outputDirectory = [System.IO.Path]::GetFullPath([string]$RunState.host_export_directory)
    $script:hostExecutable = Join-Path $script:outputDirectory "GameFactory.console.exe"
    if (-not (Test-Path -LiteralPath $script:hostExecutable)) { $script:hostExecutable = Join-Path $script:outputDirectory "GameFactory.exe" }
    $script:attemptNumber = [int]$AttemptState.attempt
    $script:attemptLabel = "attempt_{0:D3}" -f $script:attemptNumber
    $script:attemptEvidenceId = if ($null -ne $AttemptState.PSObject.Properties['evidence_attempt_id']) { [string]$AttemptState.evidence_attempt_id } else { $script:runId }
    $script:artifactDirectory = [System.IO.Path]::GetFullPath([string]$AttemptState.artifact_directory)
    $script:hostOutputDirectory = Join-Path $script:artifactDirectory "host"
    $script:clientOutputDirectory = Join-Path $script:artifactDirectory "client"
    $script:sessionOutputDirectory = Join-Path $script:artifactDirectory "session"
    $script:hostGodotLogPath = Join-Path $script:hostOutputDirectory "godot.log"
    $script:clientLiveLogPath = Join-Path $script:clientOutputDirectory "game.jsonl"
    $script:resultPath = Join-Path $script:artifactDirectory "result.json"
    $script:attemptStatePath = Join-Path $script:artifactDirectory "state.json"
    $script:vmGodotLogPath = [string]$AttemptState.vm_godot_log_path
    $script:vmExecutable = [string]$AttemptState.vm_executable
    $script:vmLiveLogPath = [string]$AttemptState.vm_live_log_path
}

function Save-RunAndAttemptState([string]$Lifecycle, [bool]$CleanupVerified, [object]$ExistingRunState) {
    $attemptState = [ordered]@{
        schema_version = 1
        run_id = $runId
        attempt = $attemptNumber
        evidence_attempt_id = $attemptEvidenceId
        scenario = $Scenario
        lifecycle = $Lifecycle
        cleanup_verified = $CleanupVerified
        artifact_directory = $artifactDirectory
        host_process_id = if ($null -eq $hostProcess) { $null } else { $hostProcess.Id }
        host_log_tail_process_id = if ($null -eq $hostLogTailProcess) { $null } else { $hostLogTailProcess.Id }
        host_godot_log_path = $hostGodotLogPath
        client_log_path = $clientLiveLogPath
        vm_godot_log_path = $vmGodotLogPath
        vm_executable = $script:vmExecutable
        vm_live_log_path = $script:vmLiveLogPath
        lobby_id = $result.lobby_id
        topology = $result.deepest_completed_stage
        infrastructure = $result.infrastructure
        started_utc = $result.started_utc
        updated_utc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    Write-JsonFile $attemptStatePath $attemptState

    $runState = if ($null -ne $ExistingRunState) { $ExistingRunState } else {
        [ordered]@{
            schema_version = 1
            run_id = $runId
            scenario = $Scenario
            host_export_directory = $outputDirectory
            manifest_sha256 = $result.build_mapping["manifest_sha256"]
            build_id = $result.build_id
            git_commit = $result.git_commit
            created_utc = $result.started_utc
            attempts = @()
        }
    }
    $runState.latest_attempt = $attemptNumber
    $runState.updated_utc = [DateTimeOffset]::UtcNow.ToString("O")
    $withoutCurrent = @($runState.attempts | Where-Object { [int]$_.attempt -ne $attemptNumber })
    $runState.attempts = @($withoutCurrent + [pscustomobject]$attemptState)
    Write-JsonFile $runStatePath $runState
}

function Write-GenericEvidence([object]$RunState, [object]$AttemptState) {
    Set-ExistingAttemptContext $RunState $AttemptState
    Sync-VmRunLog
    try { Copy-RunArtifacts } catch { Write-Warning "[harness][$runId] artifact collection failed: $($_.Exception.Message)" }
    $entries = @(Get-LogEntries)
    $eventCounts = @($entries | Group-Object { "{0}.{1}" -f $_.Category, $_.Event } | Sort-Object Count -Descending | ForEach-Object {
        [ordered]@{ event = $_.Name; count = $_.Count }
    })
    $timeline = @($entries | Sort-Object { Get-LogUtc $_ } | Select-Object -Last 120 | ForEach-Object {
        [ordered]@{ utc = $_.Utc; category = $_.Category; event = $_.Event; level = $_.Level; source = $_.__path }
    })
    $textFiles = @(Get-ChildItem -LiteralPath $artifactDirectory -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @(".log", ".txt", ".jsonl") })
    $severeMatches = @($textFiles | Select-String -Pattern "(?i)fatal|crash|segmentation|unhandled exception|script error|application error" -ErrorAction SilentlyContinue)
    $evidence = [ordered]@{
        schema_version = 1
        run_id = $runId
        attempt = $attemptNumber
        generated_utc = [DateTimeOffset]::UtcNow.ToString("O")
        lifecycle_at_collection = [string]$AttemptState.lifecycle
        structured_entry_count = $entries.Count
        event_counts = $eventCounts
        severe_text_match_count = $severeMatches.Count
        severe_text_samples = @($severeMatches | Select-Object -First 30 | ForEach-Object { [ordered]@{ path = $_.Path; line = $_.LineNumber; text = $_.Line } })
        timeline = $timeline
        raw_evidence = @($textFiles | ForEach-Object { $_.FullName })
    }
    $evidencePath = Join-Path $artifactDirectory "evidence.json"
    Write-JsonFile $evidencePath $evidence
    Write-Harness "VERIFY_READY run_id=$runId attempt=$attemptNumber entries=$($entries.Count) evidence=$evidencePath"
}

function Stop-PersistedAttempt([object]$RunState, [object]$AttemptState) {
    Set-ExistingAttemptContext $RunState $AttemptState
    $persistedHostPid = [int]$AttemptState.host_process_id
    if ($persistedHostPid -gt 0) {
        $process = Get-Process -Id $persistedHostPid -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            Write-Harness "stopping persisted host process $persistedHostPid"
            Stop-Process -Id $persistedHostPid -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $persistedHostPid -Timeout 10 -ErrorAction SilentlyContinue
        }
    }
    $persistedTailPid = [int]$AttemptState.host_log_tail_process_id
    if ($persistedTailPid -gt 0) { Stop-Process -Id $persistedTailPid -Force -ErrorAction SilentlyContinue }
    Stop-VmClientBestEffort
    $localStopped = @(Get-Process -Name GameFactory -ErrorAction SilentlyContinue).Count -eq 0
    $cleanupVerified = $localStopped -and $vmCleanupSucceeded
    $script:result = [ordered]@{
        result = if ($cleanupVerified) { "stopped" } else { "cleanup_incomplete" }
        test_run_id = $runId
        scenario = $Scenario
        mode = "infrastructure_only"
        cleanup_verified = $cleanupVerified
        lobby_id = $AttemptState.lobby_id
        deepest_completed_stage = $AttemptState.topology
        infrastructure = if ($null -ne $AttemptState.PSObject.Properties['infrastructure']) { $AttemptState.infrastructure } else { $null }
        started_utc = $AttemptState.started_utc
        completed_utc = [DateTimeOffset]::UtcNow.ToString("O")
    }
    $script:hostProcess = $null
    $script:hostLogTailProcess = $null
    Save-RunAndAttemptState "stopped" $cleanupVerified $RunState
    Write-JsonFile $resultPath $result
    Write-Harness "STOPPED run_id=$runId attempt=$attemptNumber cleanup_verified=$cleanupVerified"
    if (-not $cleanupVerified) { exit 1 }
}

function Test-VmEndpointHealth {
    $nonce = [Guid]::NewGuid().ToString("N")
    $remoteScript = @"
`$ErrorActionPreference = 'Stop'
`$task = Get-ScheduledTask -TaskName 'GameFactoryClient'
`$info = Get-ScheduledTaskInfo -TaskName 'GameFactoryClient'
`$interactiveUser = (Get-CimInstance Win32_ComputerSystem).UserName
`$interactiveSteam = @(
    Get-Process -Name steam -ErrorAction SilentlyContinue |
        Where-Object { `$_.SessionId -ne 0 } |
        Select-Object -First 1)
[ordered]@{
    marker = 'GAMEFACTORY_VM_HEALTH'
    nonce = '$nonce'
    user = [Environment]::UserName
    task_name = `$task.TaskName
    task_state = `$task.State.ToString()
    task_enabled = [bool]`$task.Settings.Enabled
    last_task_result = [int]`$info.LastTaskResult
    task_principal = [string]`$task.Principal.UserId
    interactive_user = [string]`$interactiveUser
    interactive_session_present = -not [string]::IsNullOrWhiteSpace([string]`$interactiveUser)
    interactive_steam_process_present = `$interactiveSteam.Count -eq 1
} | ConvertTo-Json -Compress
"@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($remoteScript))
    $invocation = Invoke-ExternalCommand $sshExecutable ($sshOptions + @($VmAlias, "powershell.exe", "-NoProfile", "-NonInteractive", "-EncodedCommand", $encoded)) $externalCommandTimeoutSeconds "VM endpoint health check" -SuppressOutput
    if ($invocation.ExitCode -ne 0) {
        throw "VM endpoint health check failed: SSH/remote PowerShell exited with $($invocation.ExitCode). $($invocation.StandardError.Trim())"
    }

    $healthLine = @($invocation.StandardOutput -split "`r?`n" | Where-Object { $_ -match '"marker"\s*:\s*"GAMEFACTORY_VM_HEALTH"' } | Select-Object -Last 1)
    if ($healthLine.Count -ne 1) {
        $received = $invocation.StandardOutput.Trim()
        throw "VM endpoint health check did not return its required remote marker. Received: $received"
    }
    try { $health = $healthLine[0] | ConvertFrom-Json }
    catch { throw "VM endpoint health check returned malformed JSON: $($healthLine[0])" }
    if ($health.marker -ne "GAMEFACTORY_VM_HEALTH" -or $health.nonce -ne $nonce -or $health.task_name -ne "GameFactoryClient") {
        throw "VM endpoint health marker did not match the requested shell/task contract."
    }
    if (-not [bool]$health.task_enabled) { throw "VM GameFactoryClient scheduled task is disabled." }
    if (-not [bool]$health.interactive_session_present) { throw "VM has no interactive user session; the GameFactory client task cannot safely launch Steam." }
    return $health
}

function Write-VmHealthReady([object]$Health) {
    Write-Host "VM_HEALTH_READY target=$VmAlias user=$($Health.user) interactive_user=$($Health.interactive_user) task=$($Health.task_name) state=$($Health.task_state) last_task_result=$($Health.last_task_result)"
}

function Invoke-VmHealthRecoveryIfRequested {
    try {
        $initial = Test-VmEndpointHealth
        $script:result.infrastructure.vm_health_initial = [ordered]@{ result = "passed"; interactive_user = $initial.interactive_user; task_state = $initial.task_state; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
        Write-VmHealthReady $initial
        return
    }
    catch {
        $script:result.infrastructure.vm_health_initial = [ordered]@{ result = "failed"; reason = $_.Exception.Message; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
        if (-not $RecoverVm) { Set-Blocked "vm_control" "health" "VM Health failed before launch: $($_.Exception.Message)" }
    }

    $script:result.infrastructure.vm_restart_attempted = $true
    try {
        if (-not (Get-Command Restart-VM -ErrorAction SilentlyContinue)) { throw "Hyper-V Restart-VM is unavailable on this host." }
        if ([string]::IsNullOrWhiteSpace($VmName)) { throw "RecoverVm requires -VmName when no default VM name is configured." }
        Write-Harness "VM Health failed; restarting VM '$VmName' once"
        Restart-VM -Name $VmName -Force -ErrorAction Stop
    }
    catch {
        Set-Blocked "vm_control" "recovery_restart" "VM Health failed and the one allowed VM restart could not be started: $($_.Exception.Message)"
    }

    $deadline = (Get-Date).AddSeconds($VmRecoveryTimeoutSeconds)
    $lastReason = $null
    do {
        Start-Sleep -Seconds 5
        try {
            $afterRestart = Test-VmEndpointHealth
            $script:result.infrastructure.vm_health_after_restart = [ordered]@{ result = "passed"; interactive_user = $afterRestart.interactive_user; task_state = $afterRestart.task_state; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
            Write-VmHealthReady $afterRestart
            return
        }
        catch { $lastReason = $_.Exception.Message }
    } while ((Get-Date) -lt $deadline)

    $script:result.infrastructure.vm_health_after_restart = [ordered]@{ result = "failed"; reason = $lastReason; observed_utc = [DateTimeOffset]::UtcNow.ToString("O") }
    Set-Blocked "vm_control" "recovery_readiness" "VM did not satisfy Health within $VmRecoveryTimeoutSeconds seconds after its one allowed restart. Last reason: $lastReason"
}

if ($Mode -eq "Health") {
    $health = Test-VmEndpointHealth
    Write-VmHealthReady $health
    return
}

if ($Mode -in @("Verify", "Stop")) {
    if ([string]::IsNullOrWhiteSpace($RunId)) { throw "$Mode requires -RunId." }
    $existingRunState = Read-JsonFile $runStatePath "Run state"
    $existingAttempt = Get-RunAttempt $existingRunState $Attempt
    if ($Mode -eq "Verify") {
        Write-GenericEvidence $existingRunState $existingAttempt
        return
    }
    Stop-PersistedAttempt $existingRunState $existingAttempt
    return
}

if ($Mode -eq "Retry") {
    $existingRunState = Read-JsonFile $runStatePath "Run state"
    $previousAttempt = Get-RunAttempt $existingRunState 0
    if ([string]$previousAttempt.lifecycle -eq "running") {
        throw "Run '$runId' is still running. Use -Mode Stop -RunId $runId before Retry."
    }
    $attemptNumber = ([int](@($existingRunState.attempts | Measure-Object -Property attempt -Maximum).Maximum)) + 1
    $artifactDirectory = Join-Path $runDirectory ("attempt_{0:D3}" -f $attemptNumber)
    $hostOutputDirectory = Join-Path $artifactDirectory "host"
    $clientOutputDirectory = Join-Path $artifactDirectory "client"
    $sessionOutputDirectory = Join-Path $artifactDirectory "session"
    $hostGodotLogPath = Join-Path $hostOutputDirectory "godot.log"
    $clientLiveLogPath = Join-Path $clientOutputDirectory "game.jsonl"
    $resultPath = Join-Path $artifactDirectory "result.json"
    $attemptStatePath = Join-Path $artifactDirectory "state.json"
}
elseif (Test-Path -LiteralPath $runDirectory) {
    throw "Run '$runId' already exists. Use -Mode Retry after Stop, or choose a new -RunId."
}

New-Item -ItemType Directory -Force -Path $artifactDirectory, $hostOutputDirectory, $clientOutputDirectory, $sessionOutputDirectory | Out-Null

try {
    Write-Harness "test starting; artifacts=$artifactDirectory"
    $result.stage = "preflight_cleanup"
    Get-Process -Name GameFactory -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-VmClientBestEffort
    Assert-NoStaleProcesses
    Complete-Stage "preflight_cleanup"

    if ($RecoverVm) {
        $result.stage = "vm_health"
        Invoke-VmHealthRecoveryIfRequested
        Complete-Stage "vm_health"
    }
    $cleanupToHost = [System.Diagnostics.Stopwatch]::StartNew()

    $result.stage = "build"
    $reuseExistingExport = $SkipExport -or ((-not $ForceExport) -and (Test-CurrentExportReusable))
    if (-not $reuseExistingExport) {
        Write-Harness "exporting current build"
        $exportTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $buildInvocation = Invoke-BuildTestClientIsolated -BuildScript (Join-Path $repoRoot "tools\build_test_client.ps1") -Godot $Godot -OutputDirectory $outputDirectory -TimeoutSeconds $buildHelperTimeoutSeconds
        $result.timings_ms["export"] = $exportTimer.ElapsedMilliseconds
        Set-Content -LiteralPath (Join-Path $artifactDirectory "build_helper.stdout.log") -Value $buildInvocation.StandardOutput -Encoding utf8
        Set-Content -LiteralPath (Join-Path $artifactDirectory "build_helper.stderr.log") -Value $buildInvocation.StandardError -Encoding utf8
        if ($buildInvocation.TimedOut) { Set-Failure "build" "export" "Build helper process $($buildInvocation.ProcessId) timed out after $buildHelperTimeoutSeconds seconds." }
        if ($buildInvocation.ExitCode -ne 0) {
            $detail = ($buildInvocation.StandardError, $buildInvocation.StandardOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
            Set-Failure "build" "export" "Build helper process $($buildInvocation.ProcessId) exited with code $($buildInvocation.ExitCode): $detail"
        }
        $result.build_mapping["host_export"] = "fresh"
    }
    else {
        $result.build_mapping["host_export"] = if ($SkipExport) { "explicit_reuse" } else { "automatic_reuse" }
        Write-Harness "reusing existing host export ($($result.build_mapping["host_export"]))"
    }
    if (-not (Test-Path $hostExecutable)) { Set-Failure "build" "output" "Host executable was not found at $hostExecutable." }

    $manifestPath = Join-Path $outputDirectory "build_manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { Set-Failure "build" "build_parity" "Build manifest was not found at $manifestPath." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifestHash = Get-FileSha256 -LiteralPath $manifestPath
    if (-not [string]::IsNullOrWhiteSpace($ExpectedManifestSha256) -and $manifestHash -ne $ExpectedManifestSha256.ToLowerInvariant()) {
        Set-Failure "build" "build_identity" "The current build manifest hash does not match the suite's verified manifest."
    }
    $result.build_id = [string]$manifest.build_id
    $result.git_commit = [string]$manifest.git_commit
    $result.build_mapping["manifest_sha256"] = $manifestHash
    $result.build_mapping["file_count"] = [int]$manifest.file_count
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $artifactDirectory "build_manifest.json") -Force

    $result.stage = "build_parity"
    $vmExecutable = Get-VmReleaseExecutable $manifestHash
    if ($SkipBuildParity) {
        if ([string]::IsNullOrWhiteSpace($ExpectedManifestSha256)) {
            Set-Failure "harness" "build_parity" "Skipping VM parity requires an expected verified manifest hash."
        }
        $result.build_mapping["parity"] = "reused_suite_verification"
        $result.build_mapping["vm_release_directory"] = Split-Path -Parent $vmExecutable
        $result.build_mapping["vm_executable"] = $vmExecutable
        Complete-Stage "build_parity_reused"
    }
    else {
        $stageTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $cacheState = Get-VmReleaseCacheState $manifestHash
        if ($cacheState -eq "verified_hit") {
            Write-Harness "VM immutable release cache verified hit; skipping archive staging and full file hashes"
            $result.build_mapping["vm_release_cache"] = "verified_hit"
        }
        elseif ($cacheState -eq "hit") {
            Write-Harness "VM immutable release cache hit; skipping archive staging"
            $result.build_mapping["vm_release_cache"] = "hit"
        }
        elseif ($cacheState -eq "miss" -or $cacheState -eq "mismatch") {
            Write-Harness "VM immutable release cache $cacheState; staging build"
            $vmExecutable = Stage-VmBuild $manifestHash
            $result.build_mapping["vm_release_cache"] = $cacheState
        }
        else {
            Set-Failure "vm_control" "build_cache_check" "VM cached-release check returned unexpected state '$cacheState'."
        }
        $fullParity = $cacheState -ne "verified_hit" -or $ForceFullVmParity
        $parityStatus = Invoke-VmBuildParity $manifestHash ([string]$manifest.build_id) $fullParity
        if ([string]$parityStatus.build_id -ne [string]$manifest.build_id -or [string]$parityStatus.manifest_sha256 -ne $manifestHash) {
            Set-Failure "build" "build_parity" "The VM parity result did not match the host manifest."
        }
        $parityStatus | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $localStatusPath -Encoding utf8
        Copy-Item -LiteralPath $localStatusPath -Destination (Join-Path $artifactDirectory "vm_build_parity.json") -Force
        $result.timings_ms["vm_stage_and_parity"] = $stageTimer.ElapsedMilliseconds
        $result.timings_ms["vm_parity_verification"] = [long]$parityStatus.parity_verification_ms
        Complete-Stage "build_parity"
    }

    # Sync-VmRunLog runs from helper functions, so retain the resolved staged
    # executable in script scope after parity has established it.
    $script:vmExecutable = $vmExecutable

    if ($FreshTransport) {
        $result.stage = "fresh_transport"
        Invoke-FreshTransportPreparation
        Invoke-HostSteamIpcProbe
        Complete-Stage "fresh_transport"
    }

    if ($VerifyBuildOnly) {
        $result.result = "passed"
        $result.layer = $null
        $result.stage = "complete"
        $result.reason = $null
        Complete-Stage "build_stage_and_parity"
        Write-Harness "PASS build staging and VM parity verification"
        return
    }

    $result.stage = "host_launch"
    $hostConsolePath = Join-Path $hostOutputDirectory "console.log"
    $hostErrorPath = Join-Path $hostOutputDirectory "console.error.log"
    # The managed export materializes its embedded .NET payload beneath
    # LOCALAPPDATA on first launch. The tool-owned host process cannot rely on
    # the interactive profile being writable, so give this attempt an
    # artifact-owned runtime profile just as ExportSmoke does.
    $hostRuntimeProfileRoot = Join-Path $hostOutputDirectory "runtime_profile"
    $hostRuntimeAppData = Join-Path $hostRuntimeProfileRoot "AppData\Roaming"
    $hostRuntimeLocalAppData = Join-Path $hostRuntimeProfileRoot "AppData\Local"
    New-Item -ItemType Directory -Force -Path $hostRuntimeAppData, $hostRuntimeLocalAppData | Out-Null
    New-Item -ItemType File -Path $hostConsolePath -Force | Out-Null
    $hostArguments = @(
        "--rendering-method", "gl_compatibility", "--log-file", $hostGodotLogPath,
        "--run=$runTarget", "--steam-host",
        "--test-scenario=$Scenario", "--test-run-id=$attemptEvidenceId"
    )
    Write-Harness "launching host"
    $hostLobbyTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $previousAppData = $env:APPDATA
    $previousLocalAppData = $env:LOCALAPPDATA
    try {
        $env:APPDATA = $hostRuntimeAppData
        $env:LOCALAPPDATA = $hostRuntimeLocalAppData
        $hostProcess = Start-Process -FilePath $hostExecutable -ArgumentList $hostArguments -WorkingDirectory $outputDirectory -PassThru -RedirectStandardOutput $hostConsolePath -RedirectStandardError $hostErrorPath
    }
    finally {
        $env:APPDATA = $previousAppData
        $env:LOCALAPPDATA = $previousLocalAppData
    }
    if ($ShowHostConsole) {
        $quotedLogPath = $hostGodotLogPath.Replace("'", "''")
        $hostLogTailProcess = Start-Process -FilePath "powershell.exe" -ArgumentList @("-NoProfile", "-NoExit", "-Command", "Get-Content -LiteralPath '$quotedLogPath' -Wait") -PassThru
        Write-Harness "host console log viewer started (process $($hostLogTailProcess.Id))"
    }
    $result.timings_ms["cleanup_to_host_launch"] = $cleanupToHost.ElapsedMilliseconds

    $result.stage = "lobby_creation"
    [void](Wait-ForLogEvent "steam.lifecycle" "lobby_created" "host" $HostTimeoutSeconds "steam" "lobby_creation")
    $result.timings_ms["host_launch_to_lobby_created"] = $hostLobbyTimer.ElapsedMilliseconds
    Complete-Stage "A_lobby_creation"
    $hostReady = Wait-ForLogEvent $scenarioCategory "host_ready" "host" $HostTimeoutSeconds "steam" "host_lobby"
    $lobbyId = [string]$hostReady.Fields.lobby_id
    if ([string]::IsNullOrWhiteSpace($lobbyId) -or $lobbyId -notmatch "^\d+$") { Set-Failure "steam" "host_lobby" "Host ready event did not contain a valid lobby_id." }
    $result.lobby_id = $lobbyId
    Write-Harness "discovered lobby $lobbyId from structured host diagnostics"

    $result.stage = "client_config"
    # The scheduled task is reserved for the graphical client process. Install
    # its small runner dependencies here, after build parity is complete.
    $runnerCopy = Invoke-ExternalCommand $scpExecutable ($sshOptions + @($localRunnerPath, "${VmAlias}:$VmRunnerPath")) $externalCommandTimeoutSeconds "VM runner installation"
    if ($runnerCopy.ExitCode -ne 0) { Set-Blocked "vm_control" "runner_install" "Could not install the VM runner; SCP exited with $($runnerCopy.ExitCode)." }
    $hashUtilsCopy = Invoke-ExternalCommand $scpExecutable ($sshOptions + @((Join-Path (Split-Path -Parent $PSScriptRoot) "powershell\hash_utils.ps1"), "${VmAlias}:C:/GameFactoryAgent/hash_utils.ps1")) $externalCommandTimeoutSeconds "VM hash utility installation"
    if ($hashUtilsCopy.ExitCode -ne 0) { Set-Blocked "vm_control" "hash_utility_install" "Could not install the VM hash utility; SCP exited with $($hashUtilsCopy.ExitCode)." }
    # The GPU-P guest now has the host AMD OpenGL ICD, so keep the participant
    # windowed. This is both the real player path and makes each A/B attempt
    # directly observable in the Hyper-V console.
    $clientArguments = @("--rendering-method", "gl_compatibility", "--log-file", $vmGodotLogPath, "--run=$runTarget", "--steam-lobby=$lobbyId", "--test-scenario=$Scenario", "--test-run-id=$attemptEvidenceId")
    Write-ClientConfig "launch" $clientArguments $manifest $manifestHash $vmExecutable

    $result.stage = "client_launch"
    $clientConnectionTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $launchStatus = Invoke-VmRunner "client_launched" $HostTimeoutSeconds
    $result.timings_ms["vm_runner_to_client_launch"] = [long]$launchStatus.runner_to_client_launch_ms
    Write-Harness "VM scheduled task triggered"

    $result.stage = "lobby_membership"
    [void](Wait-ForLogEvent "steam.lifecycle" "lobby_joined" "client" $ScenarioTimeoutSeconds "steam" "lobby_membership")
    Complete-Stage "B_lobby_membership"

    $result.stage = "peer_creation"
    [void](Wait-ForLogEvent "steam.peer" "created" "client" $ScenarioTimeoutSeconds "steam_peer" "peer_creation")
    Complete-Stage "C_peer_creation"

    $result.stage = "peer_assignment"
    [void](Wait-ForLogEvent "steam.peer" "assigned_to_multiplayer_api" "client" $ScenarioTimeoutSeconds "godot_multiplayer" "peer_assignment")
    Complete-Stage "D_peer_assignment"

    $result.stage = "native_handshake"
    [void](Wait-ForLogFieldValue "steam.peer_status" "changed" "client" "connection_status" "Connected" $ScenarioTimeoutSeconds "steam_peer" "native_handshake")
    Complete-Stage "E_native_handshake"

    $result.stage = "client_connection"
    $godotConnected = if ($Scenario -eq "steam_basic") { Wait-ForLogEvent "ab_test.scenario" "godot_connected_to_server" "client" $ScenarioTimeoutSeconds "godot_multiplayer" "client_connection" }
    elseif ($Scenario -eq "netfox_time_sync") { Wait-ForLogEvent "netfox.scenario" "godot_connected_to_server" "client" $ScenarioTimeoutSeconds "godot_multiplayer" "client_connection" }
    elseif ($Scenario -eq "netfox_gameplay") { Wait-ForLogEvent "netfox.movement" "godot_connected_to_server" "client" $ScenarioTimeoutSeconds "godot_multiplayer" "client_connection" }
    elseif ($Scenario -eq "netfox_player_3d") { Wait-ForLogEvent "netfox.player3d" "godot_connected_to_server" "client" $ScenarioTimeoutSeconds "godot_multiplayer" "client_connection" }
    else { Set-Failure "harness" "scenario" "Unsupported scenario '$Scenario'." }
    $result.timings_ms["harness_client_stage_to_godot_connected"] = $clientConnectionTimer.ElapsedMilliseconds
    $result.timings_ms["client_process_to_godot_connected"] = [long]$godotConnected.ElapsedMilliseconds
    Complete-Stage "F_godot_signals"
    # Launch establishes only transport and participant topology.  It never
    # waits for, interprets, or accepts gameplay input; that happens after this
    # command has returned through the human/operator and Verify workflow.
    if ($Scenario -eq "steam_basic") {
        [void](Wait-ForLogEvent "ab_test.scenario" "client_world_ready" "client" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_world")
        Complete-Stage "G_gamefactory_lifecycle"
    }
    elseif ($Scenario -eq "netfox_time_sync") {
        [void](Wait-ForLogEvent "netfox.time" "initial_sync_complete" "host" $ScenarioTimeoutSeconds "netfox" "host_time_sync")
        [void](Wait-ForLogEvent "netfox.time" "initial_sync_complete" "client" $ScenarioTimeoutSeconds "netfox" "client_time_sync")
        Complete-Stage "G_netfox_time_topology"
    }
    elseif ($Scenario -in @("netfox_gameplay", "netfox_player_3d")) {
        [void](Wait-ForLogEvent $scenarioCategory "player_spawned" "host" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "host_player_spawn")
        $playersReady = Wait-ForLogEvent $scenarioCategory "players_ready" "client" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_topology"
        if ($playersReady.Fields.player_count -ne "2") { Set-Failure "gamefactory_lifecycle" "client_topology" "Client reported a player count other than two." }
        Complete-Stage "G_two_player_topology"
    }
    else { Set-Failure "harness" "scenario" "Unsupported scenario '$Scenario'." }

    $result.result = "running"
    $result.layer = $null
    $result.stage = "ready"
    $result.reason = $null
    Save-RunAndAttemptState "running" $false $existingRunState
    $result | ConvertTo-Json -Depth 8 | Set-Content -Path $resultPath -Encoding utf8
    Write-Harness "AB_READY run_id=$runId attempt=$attemptNumber manifest=$manifestHash host=running vm=running topology=$($result.deepest_completed_stage) artifact=$artifactDirectory"
}
catch {
    if ($null -eq $result.reason) {
        $result.reason = $_.Exception.Message
    }
    $terminalResult = if ([string]::IsNullOrWhiteSpace($result.result)) { "failed" } else { $result.result.ToUpperInvariant() }
    Write-Error "[harness][$runId] $terminalResult layer=$($result.layer) stage=$($result.stage): $($result.reason)"
}
finally {
    if ($result.result -eq "running") {
        # A successful Launch/Retry deliberately returns while both games live.
        # Verify captures evidence and Stop owns deterministic teardown.
        Write-Harness "launch state=$attemptStatePath"
    }
    else {
        Stop-TestProcesses
        try { Copy-RunArtifacts } catch { Write-Warning "[harness][$runId] artifact collection failed: $($_.Exception.Message)" }
        $result.completed_utc = [DateTimeOffset]::UtcNow.ToString("O")
        $result | ConvertTo-Json -Depth 8 | Set-Content -Path $resultPath -Encoding utf8
        try { Save-RunAndAttemptState "failed" $result.cleanup_verified $existingRunState } catch { Write-Warning "[harness][$runId] state persistence failed: $($_.Exception.Message)" }
        Write-Harness "result=$resultPath"
    }
}

if ($result.result -notin @("running", "passed")) { exit 1 }
