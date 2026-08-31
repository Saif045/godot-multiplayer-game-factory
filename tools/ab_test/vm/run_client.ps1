Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$agentDirectory = "C:\GameFactoryAgent"
$configPath = Join-Path $agentDirectory "client_config.json"
$statusPath = Join-Path $agentDirectory "client_status.json"
$runnerTimer = [Diagnostics.Stopwatch]::StartNew()

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
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
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

    if ([string]$config.mode -eq "verify_only") {
        foreach ($file in $manifest.files) {
            $relativePath = ([string]$file.path).Replace('/', '\')
            $path = Join-Path $exportDirectory $relativePath
            if (-not (Test-Path -LiteralPath $path)) { throw "Manifest file is missing on the VM: $relativePath" }
            $item = Get-Item -LiteralPath $path
            if ($item.Length -ne [long]$file.size) { throw "Manifest size mismatch on the VM: $relativePath" }
            $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -ne [string]$file.sha256) { throw "Manifest hash mismatch on the VM: $relativePath" }
        }
        $status["parity_verification_ms"] = $runnerTimer.ElapsedMilliseconds
        Write-Status $status
        exit 0
    }
    if ([string]$config.mode -ne "launch") { throw "Unknown runner mode '$($config.mode)'." }
    if (-not (Test-Path -LiteralPath $executable)) { throw "Client executable does not exist: $executable" }

    $process = Start-Process -FilePath $executable -ArgumentList @($config.arguments) -WorkingDirectory $exportDirectory -PassThru
    $status["stage"] = "client_launched"
    $status["process_id"] = $process.Id
    $status["runner_to_client_launch_ms"] = $runnerTimer.ElapsedMilliseconds
    Write-Status $status
}
catch {
    Write-Status @{
        result = "failed"
        stage = "build_parity"
        reason = $_.Exception.Message
    }
    exit 1
}
