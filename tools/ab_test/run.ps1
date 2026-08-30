<#
Runs the existing two-account Steam gameplay slice as a host-PC -> VM acceptance test.

Prerequisites: Steam is logged in on both machines, `ssh gamefactory-vm` works without a
password, and the VM's GameFactoryClient scheduled task launches C:\GameFactoryAgent\run_client.ps1
inside the logged-in desktop session. The task is intentionally the only way this script starts
the VM game: launching it directly through SSH puts Steam in the wrong Windows session.
#>
[CmdletBinding()]
param(
    [string]$Godot = "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe",
    [string]$OutputDirectory,
    [string]$VmAlias = "gamefactory-vm",
    [string]$VmName = "dev-win11",
    [string]$VmShareName = "GameFactoryBuild",
    [string]$VmExecutable = "\\VBOXSVR\GameFactoryBuild\GameFactory.exe",
    [string]$VmConfigPath = "C:/GameFactoryAgent/client_config.json",
    [string]$VmStatusPath = "C:/GameFactoryAgent/client_status.json",
    [string]$VmRunnerPath = "C:/GameFactoryAgent/run_client.ps1",
    [ValidateSet("steam_basic")]
    [string]$Scenario = "steam_basic",
    [int]$HostTimeoutSeconds = 120,
    [int]$ScenarioTimeoutSeconds = 120,
    [switch]$SkipExport,
    [switch]$KeepProcesses
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$sshOptions = @("-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=5", "-o", "ServerAliveCountMax=2")

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repoRoot "build\test_steam" } else { $OutputDirectory }
$outputDirectory = [System.IO.Path]::GetFullPath($outputDirectory)
$hostExecutable = Join-Path $outputDirectory "GameFactory.console.exe"
if (-not (Test-Path $hostExecutable)) { $hostExecutable = Join-Path $outputDirectory "GameFactory.exe" }

$runId = "ab_{0}_{1}" -f (Get-Date -Format "yyyyMMdd_HHmmss"), ([Guid]::NewGuid().ToString("N").Substring(0, 4))
$artifactDirectory = Join-Path $repoRoot "artifacts\ab_tests\$runId"
$runtimeDirectory = Join-Path $PSScriptRoot ".runtime"
$localConfigPath = Join-Path $runtimeDirectory "client_config.json"
$localStatusPath = Join-Path $runtimeDirectory "client_status.json"
$localRunnerPath = Join-Path $PSScriptRoot "vm\run_client.ps1"
$hostOutputDirectory = Join-Path $artifactDirectory "host"
$clientOutputDirectory = Join-Path $artifactDirectory "client"
$sessionOutputDirectory = Join-Path $artifactDirectory "session"
$resultPath = Join-Path $artifactDirectory "result.json"
$hostProcess = $null
$vmCleanupSucceeded = $false
$result = [ordered]@{
    result = "failed"
    test_run_id = $runId
    scenario = $Scenario
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
    build_mapping = [ordered]@{
        host_directory = $outputDirectory
        vm_share = $VmShareName
        vm_executable = $VmExecutable
    }
    started_utc = [DateTimeOffset]::UtcNow.ToString("O")
    completed_utc = $null
}

New-Item -ItemType Directory -Force -Path $artifactDirectory, $hostOutputDirectory, $clientOutputDirectory, $sessionOutputDirectory, $runtimeDirectory | Out-Null

function Write-Harness([string]$Message) {
    Write-Host "[harness][$runId] $Message"
}

function Set-Failure([string]$Layer, [string]$Stage, [string]$Reason) {
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

function Invoke-Vm([string]$Command, [string]$Stage) {
    & ssh @sshOptions $VmAlias $Command
    if ($LASTEXITCODE -ne 0) {
        Set-Failure "vm_control" $Stage "VM command failed with exit code $LASTEXITCODE."
    }
}

function Invoke-VmPowerShell([string]$Script, [string]$Stage) {
    $scriptWithPreferences = '$ProgressPreference = ''SilentlyContinue''; ' + $Script
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($scriptWithPreferences))
    & ssh @sshOptions $VmAlias "powershell.exe -NoProfile -NonInteractive -EncodedCommand $encoded"
    if ($LASTEXITCODE -ne 0) {
        Set-Failure "vm_control" $Stage "VM PowerShell command failed with exit code $LASTEXITCODE."
    }
}

function Stop-VmClientBestEffort {
    $script = '$ProgressPreference = ''SilentlyContinue''; $deadline = (Get-Date).AddSeconds(10); do { $processes = @(Get-Process -Name GameFactory -ErrorAction SilentlyContinue); if ($processes.Count -eq 0) { exit 0 }; $processes | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 200 } while ((Get-Date) -lt $deadline); exit 9'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    & ssh @sshOptions $VmAlias "powershell.exe -NoProfile -NonInteractive -EncodedCommand $encoded"
    if ($LASTEXITCODE -ne 0) {
        $script:vmCleanupSucceeded = $false
        Write-Warning "[harness][$runId] VM client cleanup returned exit code $LASTEXITCODE."
    }
    else { $script:vmCleanupSucceeded = $true }
}

function Assert-NoStaleProcesses {
    if (@(Get-Process -Name GameFactory -ErrorAction SilentlyContinue).Count -gt 0) {
        Set-Failure "harness" "preflight_cleanup" "A local GameFactory process remained after cleanup."
    }
    $script = '$ProgressPreference = ''SilentlyContinue''; if (@(Get-Process -Name GameFactory -ErrorAction SilentlyContinue).Count -gt 0) { exit 9 } else { exit 0 }'
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script))
    & ssh @sshOptions $VmAlias "powershell.exe -NoProfile -NonInteractive -EncodedCommand $encoded"
    if ($LASTEXITCODE -eq 9) { Set-Failure "harness" "preflight_cleanup" "A VM GameFactory process remained after cleanup." }
    if ($LASTEXITCODE -ne 0) { Set-Failure "vm_control" "preflight_reachability" "Could not verify VM process state; SSH exited with code $LASTEXITCODE." }
}

function Write-ClientConfig([string]$Mode, [string[]]$Arguments, [object]$Manifest, [string]$ManifestHash) {
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
    & scp @sshOptions $localConfigPath "${VmAlias}:$VmConfigPath"
    if ($LASTEXITCODE -ne 0) { Set-Failure "vm_control" "client_config_copy" "SCP failed with exit code $LASTEXITCODE." }
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
        & scp @sshOptions "${VmAlias}:$VmStatusPath" $localStatusPath 2>$null
        $scpExitCode = $LASTEXITCODE
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

function Assert-VirtualBoxShareMapping {
    $vbox = "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe"
    if (-not (Test-Path -LiteralPath $vbox)) { Set-Failure "build" "build_parity" "VBoxManage was not found at $vbox." }
    $info = & $vbox showvminfo $VmName --machinereadable
    if ($LASTEXITCODE -ne 0) { Set-Failure "build" "build_parity" "Could not inspect VirtualBox VM '$VmName'." }
    $namePattern = '^SharedFolderNameMachineMapping(?<index>\d+)="' + [regex]::Escape($VmShareName) + '"$'
    $nameMatch = $info | Select-String -Pattern $namePattern | Select-Object -First 1
    if ($null -eq $nameMatch) { Set-Failure "build" "build_parity" "VirtualBox share '$VmShareName' is not configured for '$VmName'." }
    $index = $nameMatch.Matches[0].Groups['index'].Value
    $pathLine = $info | Where-Object { $_ -match "^SharedFolderPathMachineMapping$index=" } | Select-Object -First 1
    if ($null -eq $pathLine -or $pathLine -notmatch '="(?<path>.*)"$') { Set-Failure "build" "build_parity" "VirtualBox share '$VmShareName' has no readable host mapping." }
    $mappedPath = $Matches['path'] -replace '\\\\', '\'
    $mappedPath = [System.IO.Path]::GetFullPath($mappedPath)
    if ($mappedPath.TrimEnd('\') -ne $outputDirectory.TrimEnd('\')) {
        Set-Failure "build" "build_parity" "VirtualBox share maps to '$mappedPath', not '$outputDirectory'."
    }
    $script:result.build_mapping["virtualbox_vm"] = $VmName
    $script:result.build_mapping["verified_host_directory"] = $mappedPath
}

function Stop-TestProcesses {
    if ($KeepProcesses) { return }

    if ($null -ne $script:hostProcess -and -not $script:hostProcess.HasExited) {
        Write-Harness "stopping host process $($script:hostProcess.Id)"
        Stop-Process -Id $script:hostProcess.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $script:hostProcess.Id -Timeout 10 -ErrorAction SilentlyContinue
    }

    Stop-VmClientBestEffort
    $localStopped = @(Get-Process -Name GameFactory -ErrorAction SilentlyContinue).Count -eq 0
    $script:result.cleanup_verified = $localStopped -and $script:vmCleanupSucceeded
}

function Get-RunLogFiles {
    $runsDirectory = Join-Path $outputDirectory "logs\runs"
    if (-not (Test-Path $runsDirectory)) { return @() }
    return @(Get-ChildItem -Path $runsDirectory -Directory -Filter "*_$runId" -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName "game.jsonl" } |
        Where-Object { Test-Path $_ })
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

    return $entries
}

function Find-LogEvent([string]$Category, [string]$Event, [string]$Role) {
    foreach ($entry in Get-LogEntries) {
        if ($entry.Category -ne $Category -or $entry.Event -ne $Event) { continue }
        if ($Role -and $entry.Fields.role -ne $Role) { continue }
        return $entry
    }
    return $null
}

function Wait-ForLogEvent([string]$Category, [string]$Event, [string]$Role, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $connectionFailed = Find-LogEvent "ab_test.scenario" "godot_connection_failed" "client"
        if ($null -ne $connectionFailed) { Set-Failure "godot_multiplayer" "godot_signals" "Godot emitted ConnectionFailed." }
        $serverDisconnected = Find-LogEvent "ab_test.scenario" "godot_server_disconnected" "client"
        if ($null -ne $serverDisconnected) { Set-Failure "godot_multiplayer" "godot_signals" "Godot emitted ServerDisconnected." }
        $entry = Find-LogEvent $Category $Event $Role
        if ($null -ne $entry) { return $entry }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event ($Role)."
}

function Wait-ForLogFieldValue([string]$Category, [string]$Event, [string]$Role, [string]$Field, [string]$Value, [int]$TimeoutSeconds, [string]$Layer, [string]$Stage) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
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
        if ($null -ne $connectionFailed) { Set-Failure "godot_multiplayer" "godot_signals" "Godot emitted ConnectionFailed." }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event $Field=$Value ($Role)."
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
        Copy-Item -Path $runDirectory -Destination (Join-Path $destination (Split-Path -Leaf $runDirectory)) -Recurse -Force
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

try {
    Write-Harness "test starting; artifacts=$artifactDirectory"
    $result.stage = "preflight_cleanup"
    Get-Process -Name GameFactory -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-VmClientBestEffort
    Assert-NoStaleProcesses
    Complete-Stage "preflight_cleanup"
    $cleanupToHost = [System.Diagnostics.Stopwatch]::StartNew()

    $result.stage = "build"
    if (-not $SkipExport) {
        Write-Harness "exporting current build"
        try { & (Join-Path $repoRoot "tools\build_test_client.ps1") -Godot $Godot -OutputDirectory $outputDirectory -Clean }
        catch { Set-Failure "build" "export" $_.Exception.Message }
        if ($LASTEXITCODE -ne 0) { Set-Failure "build" "export" "Godot export failed with exit code $LASTEXITCODE." }
    }
    if (-not (Test-Path $hostExecutable)) { Set-Failure "build" "output" "Host executable was not found at $hostExecutable." }

    $manifestPath = Join-Path $outputDirectory "build_manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { Set-Failure "build" "build_parity" "Build manifest was not found at $manifestPath." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $result.build_id = [string]$manifest.build_id
    $result.git_commit = [string]$manifest.git_commit
    $result.build_mapping["manifest_sha256"] = $manifestHash
    $result.build_mapping["file_count"] = [int]$manifest.file_count
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $artifactDirectory "build_manifest.json") -Force

    $result.stage = "build_parity"
    Assert-VirtualBoxShareMapping
    & scp @sshOptions $localRunnerPath "${VmAlias}:$VmRunnerPath"
    if ($LASTEXITCODE -ne 0) { Set-Failure "vm_control" "runner_install" "Could not install the VM runner; SCP exited with $LASTEXITCODE." }
    Write-ClientConfig "verify_only" @() $manifest $manifestHash
    $parityStatus = Invoke-VmRunner "build_parity" $HostTimeoutSeconds
    if ([string]$parityStatus.build_id -ne [string]$manifest.build_id -or [string]$parityStatus.manifest_sha256 -ne $manifestHash) {
        Set-Failure "build" "build_parity" "The VM parity result did not match the host manifest."
    }
    Copy-Item -LiteralPath $localStatusPath -Destination (Join-Path $artifactDirectory "vm_build_parity.json") -Force
    $result.timings_ms["vm_parity_verification"] = [long]$parityStatus.parity_verification_ms
    Complete-Stage "build_parity"

    $result.stage = "host_launch"
    $hostConsolePath = Join-Path $hostOutputDirectory "console.log"
    $hostErrorPath = Join-Path $hostOutputDirectory "console.error.log"
    $hostArguments = @(
        "--rendering-method", "gl_compatibility",
        "--run=steam-gameplay", "--steam-host",
        "--test-scenario=$Scenario", "--test-run-id=$runId"
    )
    Write-Harness "launching host"
    $hostProcess = Start-Process -FilePath $hostExecutable -ArgumentList $hostArguments -WorkingDirectory $outputDirectory -PassThru -RedirectStandardOutput $hostConsolePath -RedirectStandardError $hostErrorPath
    $result.timings_ms["cleanup_to_host_launch"] = $cleanupToHost.ElapsedMilliseconds

    $result.stage = "lobby_creation"
    [void](Wait-ForLogEvent "steam.lifecycle" "lobby_created" "host" $HostTimeoutSeconds "steam" "lobby_creation")
    Complete-Stage "A_lobby_creation"
    $hostReady = Wait-ForLogEvent "ab_test.scenario" "host_ready" "host" $HostTimeoutSeconds "steam" "host_lobby"
    $lobbyId = [string]$hostReady.Fields.lobby_id
    if ([string]::IsNullOrWhiteSpace($lobbyId) -or $lobbyId -notmatch "^\d+$") { Set-Failure "steam" "host_lobby" "Host ready event did not contain a valid lobby_id." }
    $result.lobby_id = $lobbyId
    Write-Harness "discovered lobby $lobbyId from structured host diagnostics"

    $result.stage = "client_config"
    $clientArguments = @("--rendering-method", "gl_compatibility", "--run=steam-gameplay", "--steam-lobby=$lobbyId", "--test-scenario=$Scenario", "--test-run-id=$runId")
    Write-ClientConfig "launch" $clientArguments $manifest $manifestHash

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
    $godotConnected = Wait-ForLogEvent "network.connection" "connected_to_server" "" $ScenarioTimeoutSeconds "godot_multiplayer" "client_connection"
    $result.timings_ms["harness_client_stage_to_godot_connected"] = $clientConnectionTimer.ElapsedMilliseconds
    $result.timings_ms["client_process_to_godot_connected"] = [long]$godotConnected.ElapsedMilliseconds
    Complete-Stage "F_godot_signals"
    [void](Wait-ForLogEvent "ab_test.scenario" "client_world_ready" "client" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_world")
    Complete-Stage "G_gamefactory_lifecycle"
    [void](Wait-ForLogEvent "ab_test.scenario" "client_passed" "client" $ScenarioTimeoutSeconds "replication" "client_door_confirmation")
    [void](Wait-ForLogEvent "ab_test.scenario" "host_passed" "host" $ScenarioTimeoutSeconds "replication" "host_door_confirmation")
    Complete-Stage "H_replication"

    $result.result = "passed"
    $result.layer = $null
    $result.stage = "complete"
    $result.reason = $null
    Write-Harness "PASS scenario=$Scenario lobby=$lobbyId"
}
catch {
    if ($null -eq $result.reason) {
        $result.reason = $_.Exception.Message
    }
    Write-Error "[harness][$runId] FAIL layer=$($result.layer) stage=$($result.stage): $($result.reason)"
}
finally {
    Stop-TestProcesses
    try { Copy-RunArtifacts } catch { Write-Warning "[harness][$runId] artifact collection failed: $($_.Exception.Message)" }
    $result.completed_utc = [DateTimeOffset]::UtcNow.ToString("O")
    $result | ConvertTo-Json -Depth 5 | Set-Content -Path $resultPath -Encoding utf8
    Write-Harness "result=$resultPath"
}

if ($result.result -ne "passed") { exit 1 }
