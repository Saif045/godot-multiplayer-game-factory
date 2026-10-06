Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'startup.ps1')
$process = $null
$logTailProcess = $null

$runtimeDirectory = Join-Path $PSScriptRoot '.runtime'
$configPath = Join-Path $runtimeDirectory 'host_config.json'
$statusPath = Join-Path $runtimeDirectory 'host_status.json'
$runnerTimer = [Diagnostics.Stopwatch]::StartNew()
$failureStage = 'host_config'

function Write-Status([hashtable]$Status) {
    $Status['observed_utc'] = [DateTimeOffset]::UtcNow.ToString('O')
    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    $temporaryPath = "$statusPath.tmp"
    $Status | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryPath -Encoding utf8
    Move-Item -LiteralPath $temporaryPath -Destination $statusPath -Force
}

try {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $executable = [string]$config.executable
    $workingDirectory = [string]$config.working_directory
    $manifestPath = Join-Path $workingDirectory 'build_manifest.json'
    if (-not (Test-Path -LiteralPath $executable)) { throw "Host executable does not exist: $executable" }
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Host build manifest does not exist: $manifestPath" }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash.ToLowerInvariant()
    if ([string]$manifest.build_id -ne [string]$config.expected_build_id) {
        throw "Build ID mismatch. Expected '$($config.expected_build_id)', observed '$($manifest.build_id)'."
    }
    if ($manifestHash -ne [string]$config.expected_manifest_sha256) {
        throw "Manifest hash mismatch. Expected '$($config.expected_manifest_sha256)', observed '$manifestHash'."
    }

    $logIndex = [Array]::IndexOf([string[]]$config.arguments, '--log-file')
    if ($logIndex -ge 0 -and $logIndex + 1 -lt $config.arguments.Count) {
        $logDirectory = Split-Path -Parent ([string]$config.arguments[$logIndex + 1])
        if (-not [string]::IsNullOrWhiteSpace($logDirectory)) { New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null }
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$config.godot_log_path)) {
        New-Item -ItemType File -Force -Path ([string]$config.godot_log_path) | Out-Null
    }
    New-Item -ItemType Directory -Force -Path $config.app_data, $config.local_app_data | Out-Null

    $failureStage = 'host_startup'
    $previousAppData = $env:APPDATA
    $previousLocalAppData = $env:LOCALAPPDATA
    try {
        $env:APPDATA = [string]$config.app_data
        $env:LOCALAPPDATA = [string]$config.local_app_data
        $process = Start-Process -FilePath $executable -ArgumentList @($config.arguments) -WorkingDirectory $workingDirectory -PassThru -RedirectStandardOutput ([string]$config.standard_output_path) -RedirectStandardError ([string]$config.standard_error_path)
    }
    finally {
        $env:APPDATA = $previousAppData
        $env:LOCALAPPDATA = $previousLocalAppData
    }
    Wait-GameFactoryStartup $process $workingDirectory @($config.arguments) ([int]$config.startup_timeout_seconds)

    $logTailProcess = $null
    if ([bool]$config.show_log_window) {
        $quotedLogPath = ([string]$config.godot_log_path).Replace("'", "''")
        $logTailProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
            '-NoProfile',
            '-NoExit',
            '-Command',
            "Get-Content -LiteralPath '$quotedLogPath' -Wait"
        ) -PassThru
    }

    Write-Status @{
        result = 'passed'
        stage = 'host_launched'
        process_id = $process.Id
        executable = $executable
        build_id = [string]$manifest.build_id
        manifest_sha256 = $manifestHash
        runner_to_host_launch_ms = $runnerTimer.ElapsedMilliseconds
        log_tail_process_id = if ($null -eq $logTailProcess) { $null } else { $logTailProcess.Id }
    }
}
catch {
    if ($null -ne $process) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    if ($null -ne $logTailProcess) { Stop-Process -Id $logTailProcess.Id -Force -ErrorAction SilentlyContinue }
    Write-Status @{
        result = 'failed'
        stage = $failureStage
        reason = $_.Exception.Message
    }
    exit 1
}
