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
    [switch]$SkipExport,
    [switch]$ForceExport,
    [switch]$SkipBuildParity,
    [switch]$ForceFullVmParity,
    [string]$ExpectedManifestSha256,
    [string]$RunId,
    [string]$ArtifactRoot,
    [switch]$VerifyBuildOnly,
    [switch]$KeepProcesses,
    [switch]$ShowHostConsole
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "powershell\hash_utils.ps1")
. (Join-Path (Split-Path -Parent $PSScriptRoot) "powershell\process_utils.ps1")
$sshOptions = @("-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=5", "-o", "ServerAliveCountMax=2")
$externalCommandTimeoutSeconds = 30
$openSshDirectory = Join-Path $env:WINDIR "System32\OpenSSH"
$sshExecutable = Join-Path $openSshDirectory "ssh.exe"
$scpExecutable = Join-Path $openSshDirectory "scp.exe"
if (-not (Test-Path -LiteralPath $sshExecutable) -or -not (Test-Path -LiteralPath $scpExecutable)) {
    throw "Windows OpenSSH client tools were not found under $openSshDirectory."
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
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
$artifactRoot = if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) { Join-Path $repoRoot "artifacts\ab_tests" } else { $ArtifactRoot }
$artifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
$artifactDirectory = Join-Path $artifactRoot $runId
$runtimeDirectory = Join-Path $PSScriptRoot ".runtime"
$localConfigPath = Join-Path $runtimeDirectory "client_config.json"
$localStatusPath = Join-Path $runtimeDirectory "client_status.json"
$localRunnerPath = Join-Path $PSScriptRoot "vm\run_client.ps1"
$hostOutputDirectory = Join-Path $artifactDirectory "host"
$clientOutputDirectory = Join-Path $artifactDirectory "client"
$sessionOutputDirectory = Join-Path $artifactDirectory "session"
$hostGodotLogPath = Join-Path $hostOutputDirectory "godot.log"
$vmGodotLogPath = "C:/GameFactoryAgent/gamefactory_$runId.godot.log"
$clientLiveLogPath = Join-Path $clientOutputDirectory "game.jsonl"
$lastVmLogSyncUtc = [DateTimeOffset]::MinValue
$vmLiveLogPath = $null
$resultPath = Join-Path $artifactDirectory "result.json"
$hostProcess = $null
$hostLogTailProcess = $null
$vmCleanupSucceeded = $false
$netfoxShutdownExpected = $false
$buildHelperTimeoutSeconds = 210
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
        vm_build_root = $VmBuildRoot
    }
    started_utc = [DateTimeOffset]::UtcNow.ToString("O")
    completed_utc = $null
}
$runTarget = if ($Scenario -eq "netfox_time_sync") { "netfox" } elseif ($Scenario -eq "netfox_gameplay") { "netfox-gameplay" } elseif ($Scenario -eq "netfox_player_3d") { "netfox-player-3d" } else { "steam-gameplay" }
$scenarioCategory = switch ($Scenario) { "steam_basic" { "ab_test.scenario" } "netfox_time_sync" { "netfox.scenario" } "netfox_gameplay" { "netfox.movement" } "netfox_player_3d" { "netfox.player3d" } default { throw "Unsupported scenario '$Scenario'." } }

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
    return (($VmBuildRoot.TrimEnd('/', '\') + "/releases/$ManifestHash/GameFactory.exe"))
}

function Test-CurrentExportReusable {
    $manifestPath = Join-Path $outputDirectory "build_manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $hostExecutable)) { return $false }
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $headCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]$manifest.git_commit -ne $headCommit) { return $false }
        $dirty = & git -C $repoRoot status --porcelain --untracked-files=all
        if ($LASTEXITCODE -ne 0 -or -not [string]::IsNullOrWhiteSpace(($dirty -join [Environment]::NewLine))) { return $false }
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
    if ($KeepProcesses) { return }

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
    if ($null -eq $script:vmExecutable -or ([DateTimeOffset]::UtcNow - $script:lastVmLogSyncUtc).TotalMilliseconds -lt 1000) { return }
    $script:lastVmLogSyncUtc = [DateTimeOffset]::UtcNow
    try {
        if ($null -eq $script:vmLiveLogPath) {
            $remoteRunsDirectory = (Join-Path (Split-Path -Parent $script:vmExecutable) "logs\runs") -replace '/', '\\'
            $remoteScript = "`$log = Get-ChildItem -LiteralPath '$remoteRunsDirectory' -Directory -Filter '*_$runId' -ErrorAction SilentlyContinue | ForEach-Object { Join-Path `$_.FullName 'game.jsonl' } | Where-Object { Test-Path -LiteralPath `$_ } | Select-Object -First 1; if (`$null -ne `$log) { [Console]::Out.Write(`$log.Replace('\', '/')) }"
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
    $files = @(Get-ChildItem -Path $runsDirectory -Directory -Filter "*_$runId" -ErrorAction SilentlyContinue |
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

try {
    Write-Harness "test starting; artifacts=$artifactDirectory"
    $result.stage = "preflight_cleanup"
    Get-Process -Name GameFactory -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-VmClientBestEffort
    Assert-NoStaleProcesses
    Complete-Stage "preflight_cleanup"
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
    New-Item -ItemType File -Path $hostConsolePath -Force | Out-Null
    $hostArguments = @(
        "--rendering-method", "gl_compatibility", "--log-file", $hostGodotLogPath,
        "--run=$runTarget", "--steam-host",
        "--test-scenario=$Scenario", "--test-run-id=$runId"
    )
    Write-Harness "launching host"
    $hostLobbyTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $hostProcess = Start-Process -FilePath $hostExecutable -ArgumentList $hostArguments -WorkingDirectory $outputDirectory -PassThru -RedirectStandardOutput $hostConsolePath -RedirectStandardError $hostErrorPath
    if ($ShowHostConsole) {
        $quotedLogPath = $hostConsolePath.Replace("'", "''")
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
    $clientArguments = @("--rendering-method", "gl_compatibility", "--log-file", $vmGodotLogPath, "--run=$runTarget", "--steam-lobby=$lobbyId", "--test-scenario=$Scenario", "--test-run-id=$runId")
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
    if ($Scenario -eq "steam_basic") {
        [void](Wait-ForLogEvent "ab_test.scenario" "client_world_ready" "client" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_world")
        Complete-Stage "G_gamefactory_lifecycle"
        [void](Wait-ForLogEvent "ab_test.scenario" "client_passed" "client" $ScenarioTimeoutSeconds "replication" "client_door_confirmation")
        [void](Wait-ForLogEvent "ab_test.scenario" "host_passed" "host" $ScenarioTimeoutSeconds "replication" "host_door_confirmation")
        Complete-Stage "H_replication"
    }
    elseif ($Scenario -eq "netfox_time_sync") {
        $hostTimeSync = Wait-ForLogEvent "netfox.time" "initial_sync_complete" "host" $ScenarioTimeoutSeconds "netfox" "host_time_sync"
        Complete-Stage "G_netfox_host_time_sync"
        $clientTimeSync = Wait-ForLogEvent "netfox.time" "initial_sync_complete" "client" $ScenarioTimeoutSeconds "netfox" "time_sync"
        $result.timings_ms["client_process_to_netfox_sync"] = [long]$clientTimeSync.ElapsedMilliseconds
        Complete-Stage "H_netfox_client_time_sync"
        [void](Wait-ForLogEvent "netfox.time" "client_sync_complete" "host" $ScenarioTimeoutSeconds "netfox" "host_client_time_sync")
        Complete-Stage "I_netfox_host_client_sync"
        [void](Wait-ForLogFieldValue "netfox.time" "tick_progress" "host" "tick_monotonic" "true" $ScenarioTimeoutSeconds "netfox" "tick_loop")
        [void](Wait-ForLogFieldValue "netfox.time" "tick_progress" "client" "tick_monotonic" "true" $ScenarioTimeoutSeconds "netfox" "tick_loop")
        $clientTickSample = Wait-ForLogFieldValue "netfox.time" "tick_progress" "client" "rtt_known" "true" $ScenarioTimeoutSeconds "netfox" "rtt"
        $result.timings_ms["client_remote_rtt_ms"] = [double]$clientTickSample.Fields.remote_rtt_ms
        $result.timings_ms["netfox_tickrate"] = [long]$clientTickSample.Fields.tickrate
        Complete-Stage "J_netfox_ticks"
        [void](Wait-ForLogEvent "netfox.time" "client_sample_received" "host" $ScenarioTimeoutSeconds "netfox" "client_sample_delivery")
        $netfoxShutdownExpected = $true
        [void](Wait-ForLogEvent "netfox.time" "stopped" "host" $ScenarioTimeoutSeconds "netfox" "time_stop")
        [void](Wait-ForLogEvent "netfox.time" "stopped" "client" $ScenarioTimeoutSeconds "netfox" "time_stop")
        Complete-Stage "K_netfox_lifecycle_stop"
    }
    elseif ($Scenario -eq "netfox_gameplay") {
        # This is a two-account interactive sandbox, not the retired scripted
        # divergence/reconciliation scenario. The checkpoints deliberately
        # prove the real ownership boundary before asking the operator to move:
        # host-side spawning, replicated configuration, client topology, then
        # both directions of manually generated input and remote observation.
        [void](Wait-ForLogEvent "netfox.movement" "player_spawned" "host" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "host_player_spawn")
        Complete-Stage "G_host_player_spawn"
        $playersReady = Wait-ForLogEvent "netfox.movement" "players_ready" "client" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_topology"
        if ($playersReady.Fields.player_count -ne "2") { Set-Failure "gamefactory_lifecycle" "client_topology" "Client reported a player count other than two." }
        Complete-Stage "H_client_two_player_topology"

        # ElapsedMilliseconds is process-local, so never use it to order host
        # and VM events. Establish each manual checkpoint with UTC after the
        # two-player topology is ready; otherwise an earlier host input can
        # satisfy this stage before the client was present.
        $hostCheckpointUtc = [DateTimeOffset]::UtcNow
        $hostBaseline = Wait-ForLogEventAfterUtc "netfox.movement" "local_player_moved" "host" $hostCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "host_position_baseline"
        $hostRemotePresentationBaseline = Wait-ForLogFieldValueAfterUtc "netfox.reconciliation" "presentation_sample" "client" "player_id" "1" $hostCheckpointUtc $ScenarioTimeoutSeconds "replication" "host_remote_presentation_baseline"
        Write-Harness "manual gameplay ready: move the HOST marker with WASD for 15 seconds"
        $hostInput = Wait-ForLogFieldValueAfterUtc "netfox.movement" "local_input_active" "host" "active" "True" $hostCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "host_manual_input"
        $hostMovement = Wait-ForLogEventAfterUtc "netfox.movement" "local_player_moved" "host" (Get-LogUtc $hostInput) $ScenarioTimeoutSeconds "gameplay" "host_position_changed"
        if ((Get-VectorDistance ([string]$hostMovement.Fields.position) ([string]$hostBaseline.Fields.position)) -le 0.01) {
            Set-Failure "gameplay" "host_position_changed" "Host input became active but its local simulated position did not change."
        }
        [void](Wait-ForRemotePresentationChangeAfterUtc "client" "1" ([string]$hostRemotePresentationBaseline.Fields.presentation_error_pixels) (Get-LogUtc $hostInput) $ScenarioTimeoutSeconds "replication" "host_movement_observed_by_client")
        Complete-Stage "I_host_input_and_client_remote_observation"

        $clientCheckpointUtc = [DateTimeOffset]::UtcNow
        $clientBaseline = Wait-ForLogEventAfterUtc "netfox.movement" "local_player_moved" "client" $clientCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "client_position_baseline"
        $clientRemotePresentationBaseline = Wait-ForLogFieldValueAfterUtc "netfox.reconciliation" "presentation_sample" "host" "player_id" "2" $clientCheckpointUtc $ScenarioTimeoutSeconds "replication" "client_remote_presentation_baseline"
        Write-Harness "manual gameplay ready: move the CLIENT marker with WASD for 15 seconds"
        $clientInput = Wait-ForLogFieldValueAfterUtc "netfox.movement" "local_input_active" "client" "active" "True" $clientCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "client_manual_input"
        $clientMovement = Wait-ForLogEventAfterUtc "netfox.movement" "local_player_moved" "client" (Get-LogUtc $clientInput) $ScenarioTimeoutSeconds "gameplay" "client_position_changed"
        if ((Get-VectorDistance ([string]$clientMovement.Fields.position) ([string]$clientBaseline.Fields.position)) -le 0.01) {
            Set-Failure "gameplay" "client_position_changed" "Client input became active but its local simulated position did not change."
        }
        [void](Wait-ForRemotePresentationChangeAfterUtc "host" "2" ([string]$clientRemotePresentationBaseline.Fields.presentation_error_pixels) (Get-LogUtc $clientInput) $ScenarioTimeoutSeconds "replication" "client_movement_observed_by_host")
        Complete-Stage "J_client_input_and_host_remote_observation"

        [void](Wait-ForLogEvent "netfox.history_age" "sample" "host" $ScenarioTimeoutSeconds "netfox" "host_history_diagnostics")
        [void](Wait-ForLogEvent "netfox.history_age" "sample" "client" $ScenarioTimeoutSeconds "netfox" "client_history_diagnostics")
        Complete-Stage "K_history_diagnostics"
    }
    elseif ($Scenario -eq "netfox_player_3d") {
        # Intentionally small manual acceptance: this scene is a visual
        # factory composition, not another reconciliation harness.
        $hostPlayerSpawn = Wait-ForLogEvent "netfox.player3d" "player_spawned" "host" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "host_player_spawn"
        $hostPlayerNetworkObjectId = [string]$hostPlayerSpawn.Fields.network_object_id
        Complete-Stage "G_host_player_spawn"
        $playersReady = Wait-ForLogEvent "netfox.player3d" "players_ready" "client" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_topology"
        if ($playersReady.Fields.player_count -ne "2") { Set-Failure "gamefactory_lifecycle" "client_topology" "Client reported a player count other than two." }
        $clientPlayerSpawn = Wait-ForLogFieldValue "netfox.player3d" "player_spawned" "host" "owner_peer_id" ([string]$playersReady.Fields.local_peer_id) $ScenarioTimeoutSeconds "gamefactory_lifecycle" "client_player_spawn"
        $clientPlayerNetworkObjectId = [string]$clientPlayerSpawn.Fields.network_object_id
        Complete-Stage "H_client_two_player_topology"

        $hostCheckpointUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual 3D acceptance ready: move and jump on the HOST for 15 seconds"
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "local_player_moved" "host" $hostCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "host_local_walk")
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "remote_player_moved" "client" $hostCheckpointUtc $ScenarioTimeoutSeconds "replication" "host_walk_visible_on_client")
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "local_jump_observed" "host" $hostCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "host_local_jump")
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "remote_jump_observed" "client" $hostCheckpointUtc $ScenarioTimeoutSeconds "replication" "host_jump_visible_on_client")
        Complete-Stage "I_host_walk_and_jump_observed_remotely"

        $clientCheckpointUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual 3D acceptance ready: move and jump on the CLIENT for 15 seconds"
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "local_player_moved" "client" $clientCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "client_local_walk")
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "remote_player_moved" "host" $clientCheckpointUtc $ScenarioTimeoutSeconds "replication" "client_walk_visible_on_host")
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "local_jump_observed" "client" $clientCheckpointUtc $ScenarioTimeoutSeconds "gameplay" "client_local_jump")
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "remote_jump_observed" "host" $clientCheckpointUtc $ScenarioTimeoutSeconds "replication" "client_jump_visible_on_host")
        Complete-Stage "J_client_walk_and_jump_observed_remotely"

        # Interaction is intentionally outside Netfox rollback: these prompts
        # prove a local E press becomes a reliable request, a server-owned
        # replicated switch mutation, and a visual change on the other peer.
        $hostInteractionUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual interaction ready: move the HOST within 2.75m of the center switch, then press E once"
        $hostRequest = Wait-ForLogEventAfterUtc "interaction" "requested" "host" $hostInteractionUtc $ScenarioTimeoutSeconds "gameplay" "host_interaction_request"
        $hostState = Wait-ForLogFieldValueAfterUtc "interaction.switch" "state_changed" "host" "is_on" "True" (Get-LogUtc $hostRequest) $ScenarioTimeoutSeconds "replication" "host_interaction_server_state"
        [void](Wait-ForLogFieldValueAfterUtc "interaction.switch" "visual_applied" "client" "is_on" "True" (Get-LogUtc $hostState) $ScenarioTimeoutSeconds "replication" "host_interaction_visible_on_client")
        Complete-Stage "K_host_interaction_server_authority_and_client_replication"

        $clientInteractionUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual interaction ready: move the CLIENT within 2.75m of the center switch, then press E once"
        $clientRequest = Wait-ForLogEventAfterUtc "interaction" "requested" "client" $clientInteractionUtc $ScenarioTimeoutSeconds "gameplay" "client_interaction_request"
        $clientState = Wait-ForLogFieldValueAfterUtc "interaction.switch" "state_changed" "host" "is_on" "False" (Get-LogUtc $clientRequest) $ScenarioTimeoutSeconds "replication" "client_interaction_server_state"
        [void](Wait-ForLogFieldValueAfterUtc "interaction.switch" "visual_applied" "client" "is_on" "False" (Get-LogUtc $clientState) $ScenarioTimeoutSeconds "replication" "client_interaction_visible_on_client")
        Complete-Stage "L_client_interaction_server_authority_and_client_replication"

        # Carry is discrete server state, not rollback state. Each manual
        # input has a distinct UTC checkpoint so early E/Q input cannot make a
        # later assertion pass. The initial replicated state identifies the
        # single cube without depending on a hard-coded Netfox object ID.
        $carryableInitialState = Wait-ForLogFieldValue "carry" "state_applied" "host" "holder_network_object_id" "0" $ScenarioTimeoutSeconds "gamefactory_lifecycle" "carryable_initial_world_state"
        $carryableNetworkObjectId = [string]$carryableInitialState.Fields.item_network_object_id

        $hostCarryPickupUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual carry M0 ready: Do not press E or Q until prompted. HOST: move near the green cube. Do not press Q. Press E once to pick it up."
        $hostCarryRequest = Wait-ForLogFieldValueAfterUtc "interaction" "requested" "host" "target_network_object_id" $carryableNetworkObjectId $hostCarryPickupUtc $ScenarioTimeoutSeconds "gameplay" "host_carry_pickup_request"
        Assert-NoUnexpectedCarryHolderAfterUtc $carryableNetworkObjectId $hostPlayerNetworkObjectId $hostCarryPickupUtc "host_carry_pickup"
        $hostCarryPickup = Wait-ForLogFieldsAfterUtc "carry" "picked_up" "host" @{ item_network_object_id = $carryableNetworkObjectId; holder_network_object_id = $hostPlayerNetworkObjectId } (Get-LogUtc $hostCarryRequest) $ScenarioTimeoutSeconds "gameplay" "host_carry_pickup"
        Assert-NoUnexpectedCarryHolderAfterUtc $carryableNetworkObjectId $hostPlayerNetworkObjectId (Get-LogUtc $hostCarryRequest) "host_carry_pickup"
        [void](Wait-ForLogFieldsAfterUtc "carry" "state_applied" "client" @{ item_network_object_id = $carryableNetworkObjectId; holder_network_object_id = $hostPlayerNetworkObjectId } (Get-LogUtc $hostCarryPickup) $ScenarioTimeoutSeconds "replication" "host_carry_visible_on_client")
        $hostCarryMoveUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual carry M3 ready: HOST: move with WASD while carrying the cube. Do not press Q yet."
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "local_player_moved" "host" $hostCarryMoveUtc $ScenarioTimeoutSeconds "gameplay" "host_carry_movement")
        [void](Wait-ForCarryFollowAfterUtc "client" $carryableNetworkObjectId $hostPlayerNetworkObjectId $hostCarryMoveUtc $ScenarioTimeoutSeconds "host_carry_follow_visible_on_client")
        $hostDropUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual carry M5 ready: HOST: press Q once to drop the cube."
        $hostDrop = Wait-ForLogFieldsAfterUtc "carry" "dropped" "host" @{ item_network_object_id = $carryableNetworkObjectId; player_network_object_id = $hostPlayerNetworkObjectId } $hostDropUtc $ScenarioTimeoutSeconds "gameplay" "host_carry_drop"
        [void](Wait-ForLogFieldsAfterUtc "carry" "state_applied" "client" @{ item_network_object_id = $carryableNetworkObjectId; holder_network_object_id = "0" } (Get-LogUtc $hostDrop) $ScenarioTimeoutSeconds "replication" "host_drop_visible_on_client")
        Complete-Stage "M_host_carry_pickup_follow_and_drop"

        $clientCarryPickupUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual carry N0 ready: Do not press E or Q until prompted. CLIENT: focus the VM game window, move near the green cube. Do not press Q. Press E once to pick it up."
        $clientCarryRequest = Wait-ForLogFieldValueAfterUtc "interaction" "requested" "client" "target_network_object_id" $carryableNetworkObjectId $clientCarryPickupUtc $ScenarioTimeoutSeconds "gameplay" "client_carry_pickup_request"
        Assert-NoUnexpectedCarryHolderAfterUtc $carryableNetworkObjectId $clientPlayerNetworkObjectId $clientCarryPickupUtc "client_carry_pickup"
        $clientCarryPickup = Wait-ForLogFieldsAfterUtc "carry" "picked_up" "host" @{ item_network_object_id = $carryableNetworkObjectId; holder_network_object_id = $clientPlayerNetworkObjectId } (Get-LogUtc $clientCarryRequest) $ScenarioTimeoutSeconds "gameplay" "client_carry_pickup"
        Assert-NoUnexpectedCarryHolderAfterUtc $carryableNetworkObjectId $clientPlayerNetworkObjectId (Get-LogUtc $clientCarryRequest) "client_carry_pickup"
        [void](Wait-ForLogFieldsAfterUtc "carry" "state_applied" "host" @{ item_network_object_id = $carryableNetworkObjectId; holder_network_object_id = $clientPlayerNetworkObjectId } (Get-LogUtc $clientCarryPickup) $ScenarioTimeoutSeconds "replication" "client_carry_visible_on_host")
        $clientCarryMoveUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual carry N3 ready: CLIENT: focus the VM game window, then move with WASD while carrying the cube. Do not press Q yet."
        [void](Wait-ForLogEventAfterUtc "netfox.player3d" "local_player_moved" "client" $clientCarryMoveUtc $ScenarioTimeoutSeconds "gameplay" "client_carry_movement")
        [void](Wait-ForCarryFollowAfterUtc "host" $carryableNetworkObjectId $clientPlayerNetworkObjectId $clientCarryMoveUtc $ScenarioTimeoutSeconds "client_carry_follow_visible_on_host")
        $clientDropUtc = [DateTimeOffset]::UtcNow
        Write-Harness "manual carry N5 ready: CLIENT: focus the VM game window, then press Q once to drop the cube."
        $clientDrop = Wait-ForLogFieldsAfterUtc "carry" "dropped" "host" @{ item_network_object_id = $carryableNetworkObjectId; player_network_object_id = $clientPlayerNetworkObjectId } $clientDropUtc $ScenarioTimeoutSeconds "gameplay" "client_carry_drop"
        [void](Wait-ForLogFieldsAfterUtc "carry" "state_applied" "host" @{ item_network_object_id = $carryableNetworkObjectId; holder_network_object_id = "0" } (Get-LogUtc $clientDrop) $ScenarioTimeoutSeconds "replication" "client_drop_visible_on_host")
        Complete-Stage "N_client_carry_pickup_follow_and_drop"
    }
    else { Set-Failure "harness" "scenario" "Unsupported scenario '$Scenario'." }

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
    $terminalResult = if ([string]::IsNullOrWhiteSpace($result.result)) { "failed" } else { $result.result.ToUpperInvariant() }
    Write-Error "[harness][$runId] $terminalResult layer=$($result.layer) stage=$($result.stage): $($result.reason)"
}
finally {
    Stop-TestProcesses
    try { Copy-RunArtifacts } catch { Write-Warning "[harness][$runId] artifact collection failed: $($_.Exception.Message)" }
    $result.completed_utc = [DateTimeOffset]::UtcNow.ToString("O")
    $result | ConvertTo-Json -Depth 5 | Set-Content -Path $resultPath -Encoding utf8
    Write-Harness "result=$resultPath"
}

if ($result.result -ne "passed") { exit 1 }
