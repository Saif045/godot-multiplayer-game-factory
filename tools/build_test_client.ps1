param(
    [string]$Godot = "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$OutputDir = Join-Path $RepoRoot "build\test_steam"
$OutputExe = Join-Path $OutputDir "GameFactory.exe"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host ""
Write-Host "Building GameFactory test client..."
Write-Host "Repo:   $RepoRoot"
Write-Host "Output: $OutputExe"
Write-Host ""

& $Godot `
    --headless `
    --path $RepoRoot `
    --export-debug "Windows Desktop" `
    $OutputExe

if ($LASTEXITCODE -ne 0) {
    throw "Godot export failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $OutputExe)) {
    throw "Godot reported success, but GameFactory.exe was not created."
}

Write-Host ""
Write-Host "GameFactory test client ready:"
Write-Host $OutputExe