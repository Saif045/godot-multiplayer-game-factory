param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "powershell\hash_utils.ps1")

$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path
$resolvedRepository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$manifestPath = Join-Path $resolvedOutput "build_manifest.json"

$files = @(Get-ChildItem -LiteralPath $resolvedOutput -Recurse -File |
    Where-Object {
        $_.FullName -ne $manifestPath -and
        $_.FullName -notlike (Join-Path $resolvedOutput "logs\*")
    } |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($resolvedOutput.Length).TrimStart('\').Replace('\', '/')
            size = $_.Length
            sha256 = Get-FileSha256 -LiteralPath $_.FullName
        }
    })

if ($files.Count -eq 0) {
    throw "Cannot create a build manifest for an empty export directory: $resolvedOutput"
}

$canonical = ($files | ForEach-Object { "{0}|{1}|{2}" -f $_.path, $_.size, $_.sha256 }) -join "`n"
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $digest = [BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical))).Replace('-', '').ToLowerInvariant()
}
finally {
    $sha256.Dispose()
}

$gitCommit = (& git -C $resolvedRepository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not determine the Git commit for the build manifest." }
$dirtyOutput = & git -C $resolvedRepository status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0) { throw "Could not determine the Git working-tree state for the build manifest." }

$manifest = [ordered]@{
    schema_version = 1
    build_id = "gf_{0}_{1}" -f $gitCommit.Substring(0, 8), $digest.Substring(0, 12)
    git_commit = $gitCommit
    source_dirty = -not [string]::IsNullOrWhiteSpace(($dirtyOutput -join "`n"))
    created_utc = [DateTimeOffset]::UtcNow.ToString("O")
    content_sha256 = $digest
    file_count = $files.Count
    files = $files
}

$temporaryPath = "$manifestPath.tmp"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryPath -Encoding utf8
Move-Item -LiteralPath $temporaryPath -Destination $manifestPath -Force
Write-Output $manifestPath
