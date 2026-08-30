param(
    [string]$Godot = "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe",
    [string]$OutputDirectory,
    [int]$ExportTimeoutSeconds = 180,
    [int]$PostExportExitGraceSeconds = 10,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { Join-Path $RepoRoot "build\test_steam" } else { $OutputDirectory }
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
$OutputExe = Join-Path $OutputDir "GameFactory.exe"

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

$stdoutPath = Join-Path $RepoRoot ".tmp-build-export.log"
$stderrPath = Join-Path $RepoRoot ".tmp-build-export.error.log"
Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
$arguments = @("--headless", "--path", "`"$RepoRoot`"", "--export-debug", "`"Windows Desktop`"", "`"$OutputExe`"")
$process = Start-Process -FilePath $Godot -ArgumentList $arguments -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
$deadline = (Get-Date).AddSeconds($ExportTimeoutSeconds)
$packingCompletedAt = $null
$terminatedAfterCompletedExport = $false
try {
    do {
        $process.Refresh()
        if ($process.HasExited) { $process.WaitForExit(); $process.Refresh(); break }
        if ($null -eq $packingCompletedAt -and (Test-Path -LiteralPath $stdoutPath)) {
            $output = Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue
            if ($output -match '(?s)\[\s*DONE\s*\].{0,100}savepack' -and (Test-Path -LiteralPath $OutputExe) -and (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($OutputExe, ".pck")))) {
                $packingCompletedAt = Get-Date
            }
        }
        if ($null -ne $packingCompletedAt -and (Get-Date) -ge $packingCompletedAt.AddSeconds($PostExportExitGraceSeconds)) {
            Write-Warning "Godot finished packing but did not exit within $PostExportExitGraceSeconds seconds; terminating the stuck exporter process $($process.Id)."
            Stop-Process -Id $process.Id -Force
            $terminatedAfterCompletedExport = $true
            break
        }
        if ((Get-Date) -ge $deadline) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "Godot export timed out after $ExportTimeoutSeconds seconds before a completed package was observed."
        }
        Start-Sleep -Milliseconds 250
    } while ($true)

    $completedOutput = (Test-Path -LiteralPath $OutputExe) -and
        (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($OutputExe, ".pck"))) -and
        (Test-Path -LiteralPath $stdoutPath) -and
        ((Get-Content -LiteralPath $stdoutPath -Raw -ErrorAction SilentlyContinue) -match '(?s)\[\s*DONE\s*\].{0,100}savepack')
    if (-not $terminatedAfterCompletedExport -and $null -ne $process.ExitCode -and $process.ExitCode -ne 0) {
        throw "Godot export failed with exit code $($process.ExitCode)."
    }
    if (-not $completedOutput) {
        throw "Godot exited without a completed Windows package. Exit code: $($process.ExitCode)."
    }
}
finally {
    if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Tail 25 | Write-Host }
    if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Tail 50 | Write-Host }
    Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
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
