param(
    [string]$Godot = "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe",
    [string]$OutputDirectory,
    [int]$ExportTimeoutSeconds = 180,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $RepoRoot "build\test_steam" } else { $OutputDirectory }
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$OutputExe = Join-Path $OutputDir "GameFactory.exe"
$GodotProfileRoot = Join-Path $RepoRoot ".tmp-godot-export-profile"
$GodotAppData = Join-Path $GodotProfileRoot "AppData\Roaming"
$GodotLocalAppData = Join-Path $GodotProfileRoot "AppData\Local"

if ($Clean -and (Test-Path -LiteralPath $OutputDir)) {
    $buildRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "build")).TrimEnd('\') + '\'
    if (-not $OutputDir.StartsWith($buildRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an export directory outside the repository build directory: $OutputDir"
    }
    Write-Host "Removing previous generated export: $OutputDir"
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host ""
Write-Host "Building GameFactory test client..."
Write-Host "Repo:   $RepoRoot"
Write-Host "Output: $OutputExe"
Write-Host ""

# Godot's C# export plugin publishes the project with the target RID and does
# not restore first. A normal editor/solution build creates only the generic
# net8.0 assets target, which makes that publish fail with NETSDK1047 and
# leaves a package without managed assemblies. Prime the exact Windows RID
# assets before invoking Godot's exporter.
Write-Host "Restoring GameFactory for win-x64 export..."
& dotnet restore (Join-Path $RepoRoot "GameFactory.csproj") --runtime win-x64 --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore for win-x64 export failed with exit code $LASTEXITCODE."
}

# Godot's editor exporter writes settings and caches beneath APPDATA and
# LOCALAPPDATA. Validation processes may not be permitted to mutate the
# interactive profile, so use a reusable project-local profile instead. The
# installed export templates are read-only inputs; seed only this version's
# Windows x86_64 templates into the isolated profile.
$versionOutput = & $Godot --version
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch 'v?(?<version>\d+\.\d+\.\d+\.stable\.mono)') {
    throw "Could not determine the installed Godot Mono export-template version. Output: $versionOutput"
}
$templateVersion = $Matches.version
$installedTemplates = Join-Path $env:APPDATA "Godot\export_templates\$templateVersion"
$isolatedTemplates = Join-Path $GodotAppData "Godot\export_templates\$templateVersion"
$templateNames = @(
    "windows_debug_x86_64.exe",
    "windows_debug_x86_64_console.exe",
    "windows_release_x86_64.exe",
    "windows_release_x86_64_console.exe"
)
if (-not (Test-Path -LiteralPath $installedTemplates)) {
    throw "Godot $templateVersion export templates are not installed at $installedTemplates."
}
New-Item -ItemType Directory -Force -Path $isolatedTemplates, $GodotLocalAppData | Out-Null
foreach ($templateName in $templateNames) {
    $sourceTemplate = Join-Path $installedTemplates $templateName
    $isolatedTemplate = Join-Path $isolatedTemplates $templateName
    if (-not (Test-Path -LiteralPath $sourceTemplate)) {
        throw "Required Godot export template is missing: $sourceTemplate"
    }
    if (-not (Test-Path -LiteralPath $isolatedTemplate)) {
        Copy-Item -LiteralPath $sourceTemplate -Destination $isolatedTemplate
    }
}

$stdoutPath = Join-Path $RepoRoot ".tmp-build-export.log"
$stderrPath = Join-Path $RepoRoot ".tmp-build-export.error.log"
$engineLogPath = Join-Path $RepoRoot ".tmp-build-export.engine.log"
Remove-Item -LiteralPath $stdoutPath, $stderrPath, $engineLogPath -Force -ErrorAction SilentlyContinue
$arguments = @("--headless", "--log-file", "`"$engineLogPath`"", "--path", "`"$RepoRoot`"", "--export-debug", "`"Windows Desktop`"", "`"$OutputExe`"")
# Windows PowerShell 5 does not expose Start-Process -Environment. Set these
# only while creating the child, then immediately restore the caller process.
$previousAppData = $env:APPDATA
$previousLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = $GodotAppData
    $env:LOCALAPPDATA = $GodotLocalAppData
    $process = Start-Process -FilePath $Godot -ArgumentList $arguments -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
}
finally {
    $env:APPDATA = $previousAppData
    $env:LOCALAPPDATA = $previousLocalAppData
}
$deadline = (Get-Date).AddSeconds($ExportTimeoutSeconds)
try {
    do {
        $process.Refresh()
        if ($process.HasExited) { $process.WaitForExit(); $process.Refresh(); break }
        if ((Get-Date) -ge $deadline) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Godot export timed out after $ExportTimeoutSeconds seconds before it exited normally."
        }
        Start-Sleep -Milliseconds 250
    } while ($true)

    $completedOutput = (Test-Path -LiteralPath $OutputExe) -and
        (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($OutputExe, ".pck")))
    # Godot emits managed-export diagnostics on stderr. Inspect both streams:
    # accepting a packed executable after an ERROR here can leave a stale or
    # incomplete managed payload that the A/B harness cannot meaningfully test.
    $exportLog = @(
        if (Test-Path -LiteralPath $stdoutPath) {
            Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $stderrPath) {
            Get-Content -LiteralPath $stderrPath -Raw -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $engineLogPath) {
            Get-Content -LiteralPath $engineLogPath -Raw -ErrorAction SilentlyContinue
        }
    ) -join [Environment]::NewLine
    if ($exportLog -match 'dotnet publish exited with code: [1-9]' -or
        $exportLog -match 'ERROR: Export \.NET Project:' -or
        $exportLog -match 'ERROR: Project export for preset') {
        throw "Godot reported a managed export failure; refusing to accept the generated package."
    }
    if ($null -ne $process.ExitCode -and $process.ExitCode -ne 0) {
        throw "Godot export failed with exit code $($process.ExitCode)."
    }
    if (-not $completedOutput) {
        throw "Godot exited without a complete Windows package."
    }
}
finally {
    if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Tail 25 | Write-Host }
    if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Tail 50 | Write-Host }
    if (Test-Path -LiteralPath $engineLogPath) { Get-Content -LiteralPath $engineLogPath -Tail 50 | Write-Host }
    Remove-Item -LiteralPath $stdoutPath, $stderrPath, $engineLogPath -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $OutputExe)) {
    throw "Godot reported success, but GameFactory.exe was not created."
}

$manifestPath = & (Join-Path $PSScriptRoot "write_build_manifest.ps1") -OutputDirectory $OutputDir -RepositoryRoot $RepoRoot
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $manifestPath)) {
    throw "Build manifest generation failed."
}

Write-Host ""
Write-Host "GameFactory test client ready:"
Write-Host $OutputExe
Write-Host "Manifest: $manifestPath"
