Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$hashUtilsPath = Join-Path $PSScriptRoot "hash_utils.ps1"
if (-not (Test-Path -LiteralPath $hashUtilsPath)) {
    $hashUtilsPath = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "powershell\hash_utils.ps1"
}
. $hashUtilsPath

$agentDirectory = "C:\GameFactoryAgent"
$configPath = Join-Path $agentDirectory "client_config.json"
$statusPath = Join-Path $agentDirectory "client_status.json"
$runnerTimer = [Diagnostics.Stopwatch]::StartNew()
$failureStage = "build_parity"

function Write-Status([hashtable]$Status) {
    $Status["observed_utc"] = [DateTimeOffset]::UtcNow.ToString("O")
    $temporaryPath = "$statusPath.tmp"
    $Status | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryPath -Encoding utf8
    Move-Item -LiteralPath $temporaryPath -Destination $statusPath -Force
}

try {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $executable = [string]$config.executable
    $exportDirectory = Split-Path -Parent $executable
    $manifestPath = Join-Path $exportDirectory "build_manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "The VM-visible build manifest does not exist: $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifestHash = Get-FileSha256 -LiteralPath $manifestPath
    if ([string]$manifest.build_id -ne [string]$config.expected_build_id) {
        throw "Build ID mismatch. Expected '$($config.expected_build_id)', observed '$($manifest.build_id)'."
    }
    if ($manifestHash -ne [string]$config.expected_manifest_sha256) {
        throw "Manifest hash mismatch. Expected '$($config.expected_manifest_sha256)', observed '$manifestHash'."
    }

    $parityVerificationMilliseconds = $runnerTimer.ElapsedMilliseconds

    $status = @{
        result = "passed"
        stage = "build_parity"
        build_id = [string]$manifest.build_id
        git_commit = [string]$manifest.git_commit
        manifest_sha256 = $manifestHash
        file_count = [int]$manifest.file_count
        executable = $executable
        mode = [string]$config.mode
        parity_verification_ms = $parityVerificationMilliseconds
    }

    if ([string]$config.mode -ne "launch") { throw "Unknown runner mode '$($config.mode)'." }
    if (-not (Test-Path -LiteralPath $executable)) { throw "Client executable does not exist: $executable" }

    # A retry has its own remote log namespace. Godot does not create an
    # arbitrary --log-file parent directory, so establish it before launch.
    $logFileArgument = @($config.arguments | Where-Object { $_ -eq "--log-file" })
    if ($logFileArgument.Count -gt 0) {
        $logIndex = [Array]::IndexOf([string[]]$config.arguments, "--log-file")
        if ($logIndex -ge 0 -and $logIndex + 1 -lt $config.arguments.Count) {
            $logDirectory = Split-Path -Parent ([string]$config.arguments[$logIndex + 1])
            if (-not [string]::IsNullOrWhiteSpace($logDirectory)) { New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null }
        }
    }

    $failureStage = "client_startup"
    $process = Start-Process -FilePath $executable -ArgumentList @($config.arguments) -WorkingDirectory $exportDirectory -PassThru
    # Start-Process returning a PID only proves that Windows accepted the
    # request. Check the initial process state and its visible error window so
    # a loader failure is reported as startup failure rather than a later
    # multiplayer timeout.
    Start-Sleep -Seconds 3
    $process.Refresh()
    $startupWindowTitle = [string]$process.MainWindowTitle
    if ($process.HasExited) {
        throw "Client exited during startup (exit code $($process.ExitCode))."
    }
    if ($startupWindowTitle -match "Application Error") {
        throw "Client displayed a Windows application-error dialog during startup: $startupWindowTitle"
    }
    $status["stage"] = "client_launched"
    $status["process_id"] = $process.Id
    $status["startup_window_title"] = $startupWindowTitle
    $status["runner_to_client_launch_ms"] = $runnerTimer.ElapsedMilliseconds
    Write-Status $status
}
catch {
    Write-Status @{
        result = "failed"
        stage = $failureStage
        reason = $_.Exception.Message
    }
    exit 1
}
