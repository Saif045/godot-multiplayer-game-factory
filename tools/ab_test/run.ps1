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
    [string]$VmAlias = "gamefactory-vm",
    [string]$VmExecutable = "\\VBOXSVR\GameFactoryBuild\GameFactory.exe",
    [string]$VmConfigPath = "C:/GameFactoryAgent/client_config.json",
    [ValidateSet("steam_basic")]
    [string]$Scenario = "steam_basic",
    [int]$HostTimeoutSeconds = 120,
    [int]$ScenarioTimeoutSeconds = 120,
    [switch]$SkipExport,
    [switch]$KeepProcesses
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputDirectory = Join-Path $repoRoot "build\test_steam"
$hostExecutable = Join-Path $outputDirectory "GameFactory.console.exe"
if (-not (Test-Path $hostExecutable)) { $hostExecutable = Join-Path $outputDirectory "GameFactory.exe" }

$runId = "ab_{0}_{1}" -f (Get-Date -Format "yyyyMMdd_HHmmss"), ([Guid]::NewGuid().ToString("N").Substring(0, 4))
$artifactDirectory = Join-Path $repoRoot "artifacts\ab_tests\$runId"
$runtimeDirectory = Join-Path $PSScriptRoot ".runtime"
$localConfigPath = Join-Path $runtimeDirectory "client_config.json"
$hostOutputDirectory = Join-Path $artifactDirectory "host"
$clientOutputDirectory = Join-Path $artifactDirectory "client"
$sessionOutputDirectory = Join-Path $artifactDirectory "session"
$resultPath = Join-Path $artifactDirectory "result.json"
$hostProcess = $null
$result = [ordered]@{
    result = "failed"
    test_run_id = $runId
    scenario = $Scenario
    layer = "harness"
    stage = "initializing"
    reason = $null
    lobby_id = $null
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

function Invoke-Vm([string]$Command, [string]$Stage) {
    & ssh $VmAlias $Command
    if ($LASTEXITCODE -ne 0) {
        Set-Failure "vm_control" $Stage "VM command failed with exit code $LASTEXITCODE."
    }
}

function Stop-VmClientBestEffort {
    & ssh $VmAlias 'powershell.exe -NoProfile -Command "Stop-Process -Name GameFactory -Force -ErrorAction SilentlyContinue"'
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "[harness][$runId] VM client cleanup returned exit code $LASTEXITCODE."
    }
}

function Stop-TestProcesses {
    if ($KeepProcesses) { return }

    if ($null -ne $script:hostProcess -and -not $script:hostProcess.HasExited) {
        Write-Harness "stopping host process $($script:hostProcess.Id)"
        Stop-Process -Id $script:hostProcess.Id -Force -ErrorAction SilentlyContinue
    }

    Stop-VmClientBestEffort
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

    $sessionsDirectory = Join-Path $outputDirectory "logs\sessions"
    if (Test-Path $sessionsDirectory) {
        foreach ($path in Get-ChildItem -Path $sessionsDirectory -Filter "master.jsonl" -Recurse -File -ErrorAction SilentlyContinue) {
            foreach ($line in Get-Content -Path $path.FullName -ErrorAction SilentlyContinue) {
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                try {
                    $master = $line | ConvertFrom-Json
                    if ($master.entry.RunId -ne $runId) { continue }
                    $entry = $master.entry
                    $entry | Add-Member -NotePropertyName __path -NotePropertyValue $path.FullName -Force
                    $entry | Add-Member -NotePropertyName __source_role -NotePropertyValue $master.source_role -Force
                    $entries += $entry
                }
                catch { }
            }
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
        $entry = Find-LogEvent $Category $Event $Role
        if ($null -ne $entry) { return $entry }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    Set-Failure $Layer $Stage "Timed out after $TimeoutSeconds seconds waiting for $Category/$Event ($Role)."
}

function Copy-RunArtifacts {
    foreach ($path in Get-RunLogFiles) {
        $entries = @()
        try { $entries = @(Get-Content $path | ForEach-Object { $_ | ConvertFrom-Json }) } catch { }
        $isClientRun = @($entries | Where-Object {
            $_.Fields -and
            $_.Fields.PSObject.Properties.Name -contains "role" -and
            $_.Fields.role -eq "client"
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
}

try {
    Write-Harness "test starting; artifacts=$artifactDirectory"
    $result.stage = "preflight_cleanup"
    Get-Process -Name GameFactory -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-VmClientBestEffort

    $result.stage = "build"
    if (-not $SkipExport) {
        Write-Harness "exporting current build"
        & (Join-Path $repoRoot "tools\build_test_client.ps1") -Godot $Godot -Clean
        if ($LASTEXITCODE -ne 0) { Set-Failure "build" "export" "Godot export failed with exit code $LASTEXITCODE." }
    }
    if (-not (Test-Path $hostExecutable)) { Set-Failure "build" "output" "Host executable was not found at $hostExecutable." }

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

    $result.stage = "host_lobby"
    $hostReady = Wait-ForLogEvent "ab_test.scenario" "host_ready" "host" $HostTimeoutSeconds "steam" "host_lobby"
    $lobbyId = [string]$hostReady.Fields.lobby_id
    if ([string]::IsNullOrWhiteSpace($lobbyId) -or $lobbyId -notmatch "^\d+$") { Set-Failure "steam" "host_lobby" "Host ready event did not contain a valid lobby_id." }
    $result.lobby_id = $lobbyId
    Write-Harness "discovered lobby $lobbyId from structured host diagnostics"

    $result.stage = "client_config"
    $clientConfig = [ordered]@{
        executable = $VmExecutable
        arguments = @("--rendering-method", "gl_compatibility", "--run=steam-gameplay", "--steam-lobby=$lobbyId", "--test-scenario=$Scenario", "--test-run-id=$runId")
    }
    $temporaryConfigPath = "$localConfigPath.tmp"
    $clientConfig | ConvertTo-Json -Depth 4 | Set-Content -Path $temporaryConfigPath -Encoding utf8
    Move-Item -Path $temporaryConfigPath -Destination $localConfigPath -Force
    & scp $localConfigPath "${VmAlias}:$VmConfigPath"
    if ($LASTEXITCODE -ne 0) { Set-Failure "vm_control" "client_config_copy" "SCP failed with exit code $LASTEXITCODE." }
    Copy-Item -Path $localConfigPath -Destination (Join-Path $artifactDirectory "client_config.json") -Force

    $result.stage = "client_launch"
    Invoke-Vm 'powershell.exe -NoProfile -Command "Start-ScheduledTask -TaskName GameFactoryClient"' "client_start"
    Write-Harness "VM scheduled task triggered"

    $result.stage = "client_lobby"
    [void](Wait-ForLogEvent "ab_test.scenario" "client_joined_lobby" "client" $ScenarioTimeoutSeconds "steam" "client_lobby")

    $result.stage = "client_connection"
    [void](Wait-ForLogEvent "network.connection" "connected_to_server" "" $ScenarioTimeoutSeconds "godot_multiplayer" "client_connection")
    [void](Wait-ForLogEvent "ab_test.scenario" "client_world_ready" "client" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_world")
    [void](Wait-ForLogEvent "ab_test.scenario" "client_passed" "client" $ScenarioTimeoutSeconds "replication" "client_door_confirmation")
    [void](Wait-ForLogEvent "ab_test.scenario" "host_passed" "host" $ScenarioTimeoutSeconds "replication" "host_door_confirmation")

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
