<#
Runs independent A/B attempts against one immutable exported build. The first
attempt proves VM parity; later attempts reuse that proof while still checking
the local manifest hash before any Steam process starts.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 1000)]
    [int]$Attempts = 10,
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
    [switch]$SkipExport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $repoRoot "build\test_steam" } else { $OutputDirectory }
$outputDirectory = [System.IO.Path]::GetFullPath($outputDirectory)
$suiteId = "suite_{0}_{1}" -f (Get-Date -Format "yyyyMMdd_HHmmss"), ([Guid]::NewGuid().ToString("N").Substring(0, 4))
$suiteDirectory = Join-Path $repoRoot "artifacts\ab_suites\$suiteId"
$attemptsDirectory = Join-Path $suiteDirectory "attempts"
$summaryPath = Join-Path $suiteDirectory "summary.json"
$runScript = Join-Path $PSScriptRoot "run.ps1"

New-Item -ItemType Directory -Force -Path $suiteDirectory, $attemptsDirectory | Out-Null

$summary = [ordered]@{
    suite_id = $suiteId
    scenario = $Scenario
    requested_attempts = $Attempts
    completed_attempts = 0
    passed_attempts = 0
    failed_attempts = 0
    result = "failed"
    build_id = $null
    manifest_sha256 = $null
    parity_verified_by_attempt = $null
    failed_stage_distribution = [ordered]@{}
    client_process_to_godot_connected_ms = @()
    attempts = @()
    started_utc = [DateTimeOffset]::UtcNow.ToString("O")
    completed_utc = $null
}

try {
    if (-not $SkipExport) {
        Write-Host "[suite][$suiteId] exporting one immutable test build"
        & (Join-Path $repoRoot "tools\build_test_client.ps1") -Godot $Godot -OutputDirectory $outputDirectory -Clean
        if ($LASTEXITCODE -ne 0) { throw "Godot export failed with exit code $LASTEXITCODE." }
    }

    $manifestPath = Join-Path $outputDirectory "build_manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Build manifest was not found at $manifestPath." }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $summary.build_id = [string]$manifest.build_id
    $summary.manifest_sha256 = $manifestHash
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $suiteDirectory "build_manifest.json") -Force

    for ($attemptNumber = 1; $attemptNumber -le $Attempts; $attemptNumber++) {
        $attemptId = "{0}_attempt{1:d2}" -f $suiteId, $attemptNumber
        Write-Host "[suite][$suiteId] starting attempt $attemptNumber/$Attempts ($attemptId)"
        # run.ps1 deliberately exits non-zero for an unsuccessful attempt.
        # Launching it in a child PowerShell keeps that result local to this
        # attempt so the suite can collect it and continue to the next clean
        # sample instead of ending at the first failure.
        $runArguments = @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $runScript,
            "-Godot", $Godot,
            "-OutputDirectory", $outputDirectory,
            "-VmAlias", $VmAlias,
            "-VmName", $VmName,
            "-VmShareName", $VmShareName,
            "-VmExecutable", $VmExecutable,
            "-VmConfigPath", $VmConfigPath,
            "-VmStatusPath", $VmStatusPath,
            "-VmRunnerPath", $VmRunnerPath,
            "-Scenario", $Scenario,
            "-HostTimeoutSeconds", $HostTimeoutSeconds,
            "-ScenarioTimeoutSeconds", $ScenarioTimeoutSeconds,
            "-SkipExport",
            "-ExpectedManifestSha256", $manifestHash,
            "-RunId", $attemptId,
            "-ArtifactRoot", $attemptsDirectory
        )
        if ($attemptNumber -gt 1) { $runArguments += "-SkipBuildParity" }

        & powershell.exe @runArguments
        $attemptExitCode = $LASTEXITCODE
        $resultPath = Join-Path $attemptsDirectory "$attemptId\result.json"
        if (-not (Test-Path -LiteralPath $resultPath)) {
            throw "Attempt $attemptNumber completed without result.json (exit code $attemptExitCode)."
        }

        $attempt = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $summary.completed_attempts++
        if ($attemptNumber -eq 1 -and $attempt.completed_stages -contains "build_parity") {
            $summary.parity_verified_by_attempt = $attemptId
        }
        if ($attempt.result -eq "passed") {
            $summary.passed_attempts++
            if ($null -ne $attempt.timings_ms.client_process_to_godot_connected) {
                $summary.client_process_to_godot_connected_ms += [long]$attempt.timings_ms.client_process_to_godot_connected
            }
        }
        else {
            $summary.failed_attempts++
            $stage = if ([string]::IsNullOrWhiteSpace([string]$attempt.stage)) { "unknown" } else { [string]$attempt.stage }
            if ($summary.failed_stage_distribution.Contains($stage)) { $summary.failed_stage_distribution[$stage]++ }
            else { $summary.failed_stage_distribution[$stage] = 1 }
        }
        $summary.attempts += [ordered]@{
            number = $attemptNumber
            run_id = $attempt.test_run_id
            result = $attempt.result
            layer = $attempt.layer
            stage = $attempt.stage
            deepest_completed_stage = $attempt.deepest_completed_stage
            cleanup_verified = $attempt.cleanup_verified
            artifact_directory = (Join-Path $attemptsDirectory $attemptId)
        }
    }

    if ($summary.parity_verified_by_attempt -eq $null) { throw "The suite did not complete its required VM parity verification." }
    $summary.result = if ($summary.failed_attempts -eq 0) { "passed" } else { "failed" }
}
catch {
    $summary.error = $_.Exception.Message
    Write-Error "[suite][$suiteId] FAIL: $($summary.error)"
}
finally {
    $summary.completed_utc = [DateTimeOffset]::UtcNow.ToString("O")
    $summary | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Write-Host "[suite][$suiteId] summary=$summaryPath"
}

if ($summary.result -ne "passed") { exit 1 }
