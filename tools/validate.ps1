<#
Universal, local validation entry point. It composes existing project checks;
it does not replace their assertions or start an operator-driven A/B session.
#>
[CmdletBinding()]
param(
    [ValidateSet("List", "Quick", "Auto", "Headless", "Probe", "ExportSmoke", "Full")]
    [string]$Mode = "Auto",
    [ValidateSet("Gas")]
    [string]$Probe,
    [string[]]$ChangedPath,
    [string]$Godot = "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$results = [Collections.Generic.List[object]]::new()

function Add-Result([string]$Status, [string]$Name, [string]$Command, [string]$Detail) {
    $record = [pscustomobject]@{ Status = $Status; Name = $Name; Command = $Command; Detail = $Detail }
    $script:results.Add($record)
    Write-Host ("{0,-4} {1} - {2}" -f $Status, $Name, $Detail)
}

function Invoke-Check([string]$Name, [string]$Command, [scriptblock]$Action) {
    try {
        & $Action
        Add-Result "PASS" $Name $Command "completed"
        return $true
    }
    catch {
        Add-Result "FAIL" $Name $Command $_.Exception.Message
        return $false
    }
}

function Invoke-GitHygiene {
    Invoke-Check "source-hygiene" "git diff --check" {
        & git -C $repoRoot diff --check
        if ($LASTEXITCODE -ne 0) { throw "git diff --check exited with $LASTEXITCODE." }
    } | Out-Null
}

function Invoke-DeterministicBuild {
    Invoke-Check "dotnet-build" "dotnet build GameFactory.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false" {
        & dotnet build (Join-Path $repoRoot "GameFactory.csproj") --disable-build-servers -m:1 -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE." }
    } | Out-Null
}

function Invoke-UnitRegression {
    Invoke-Check "unit-regression" "dotnet test tests/GameFactory.Tests/GameFactory.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false" {
        & dotnet test (Join-Path $repoRoot "tests\GameFactory.Tests\GameFactory.Tests.csproj") --disable-build-servers -m:1 -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "dotnet test exited with $LASTEXITCODE." }
    } | Out-Null
}

function Assert-Godot {
    if (-not (Test-Path -LiteralPath $Godot)) { throw "Godot console executable was not found: $Godot" }
}

function Invoke-BoundedGodot([string]$Description, [string[]]$Arguments, [int]$TimeoutSeconds) {
    $stdoutPath = Join-Path $repoRoot ".tmp-validate-$Description.stdout.log"
    $stderrPath = Join-Path $repoRoot ".tmp-validate-$Description.stderr.log"
    $engineLogPath = Join-Path $repoRoot ".tmp-validate-$Description.engine.log"
    Remove-Item -LiteralPath $stdoutPath, $stderrPath, $engineLogPath -Force -ErrorAction SilentlyContinue
    # Godot 4.7 has no --user-data-dir flag.  Direct the engine's own log into
    # this disposable workspace location instead, before any app arguments.
    $godotArguments = @("--log-file", $engineLogPath) + $Arguments
    $process = Start-Process -FilePath $Godot -ArgumentList $godotArguments -WorkingDirectory $repoRoot -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            throw "$Description timed out after $TimeoutSeconds seconds; process $($process.Id) was stopped."
        }
        # Refresh after the bounded wait: Start-Process may otherwise expose a
        # stale/null ExitCode even though the child has terminated successfully.
        $process.Refresh()
        [int]$exitCode = $process.ExitCode
        $output = @(
            if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Tail 30 }
            if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Tail 30 }
            if (Test-Path -LiteralPath $engineLogPath) { Get-Content -LiteralPath $engineLogPath -Tail 30 }
        ) -join [Environment]::NewLine
        if ($exitCode -ne 0) { throw "$Description exited with $exitCode. $output" }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath, $engineLogPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-Headless {
    Invoke-Check "godot-headless" "Godot --headless --path <repo> --editor --quit" {
        Assert-Godot
        Invoke-BoundedGodot "headless" @("--headless", "--path", $repoRoot, "--editor", "--quit") 60
    } | Out-Null
}

function Invoke-GasProbe {
    Invoke-Check "probe-gas" "Godot --headless --path <repo> --run=gas-interop" {
        Assert-Godot
        Invoke-BoundedGodot "gas-probe" @("--headless", "--path", $repoRoot, "--", "--run=gas-interop") 75
    } | Out-Null
}

function Invoke-PowerShellParser([string[]]$Paths) {
    Invoke-Check "powershell-static" "PowerShell parser for changed project tools" {
        foreach ($path in $Paths) {
            $tokens = $null
            $errors = $null
            [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
            if ($errors.Count -gt 0) { throw "Parse failure in $($path): $($errors[0].Message)" }
        }
    } | Out-Null
}

function Invoke-ExportSmoke {
    Invoke-Check "export-smoke" "tools/build_test_client.ps1; exported GameFactory headless boot" {
        Assert-Godot
        & (Join-Path $repoRoot "tools\build_test_client.ps1") -Godot $Godot
        if ($LASTEXITCODE -ne 0) { throw "build_test_client.ps1 exited with $LASTEXITCODE." }
        $outputDirectory = Join-Path $repoRoot "build\test_steam"
        $executable = Join-Path $outputDirectory "GameFactory.console.exe"
        if (-not (Test-Path -LiteralPath $executable)) { $executable = Join-Path $outputDirectory "GameFactory.exe" }
        if (-not (Test-Path -LiteralPath $executable)) { throw "Export did not produce GameFactory.exe." }
        $logPath = Join-Path $repoRoot ".tmp-export-smoke.engine.log"
        $stdoutPath = Join-Path $repoRoot ".tmp-export-smoke.stdout.log"
        $stderrPath = Join-Path $repoRoot ".tmp-export-smoke.stderr.log"
        $runtimeProfileRoot = Join-Path $repoRoot ".tmp-godot-export-profile"
        $runtimeAppData = Join-Path $runtimeProfileRoot "AppData\Roaming"
        $runtimeLocalAppData = Join-Path $runtimeProfileRoot "AppData\Local"
        Remove-Item -LiteralPath $logPath, $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
        # Do not force-kill a normal exported Godot process after an arbitrary
        # delay. The existing GAS interop probe gives this smoke check a real,
        # self-terminating managed-runtime contract instead.
        New-Item -ItemType Directory -Force -Path $runtimeAppData, $runtimeLocalAppData | Out-Null
        $previousAppData = $env:APPDATA
        $previousLocalAppData = $env:LOCALAPPDATA
        try {
            $env:APPDATA = $runtimeAppData
            $env:LOCALAPPDATA = $runtimeLocalAppData
            $process = Start-Process -FilePath $executable -ArgumentList @("--headless", "--log-file", $logPath, "--", "--run=gas-interop") -WorkingDirectory $outputDirectory -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        }
        finally {
            $env:APPDATA = $previousAppData
            $env:LOCALAPPDATA = $previousLocalAppData
        }
        try {
            if (-not $process.WaitForExit(45000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                throw "Exported runtime did not complete the self-terminating GAS smoke probe within 45 seconds; process $($process.Id) was stopped."
            }
            $process.Refresh()
            [int]$exitCode = $process.ExitCode
            $runtimeOutput = @(
                if (Test-Path -LiteralPath $stdoutPath) { Get-Content -LiteralPath $stdoutPath -Raw }
                if (Test-Path -LiteralPath $stderrPath) { Get-Content -LiteralPath $stderrPath -Raw }
                if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw }
            ) -join [Environment]::NewLine
            if ($exitCode -ne 0) { throw "Exported runtime exited with $exitCode. $runtimeOutput" }
            if ($runtimeOutput -notmatch '\[gas\.interop\] probe_passed') {
                throw "Exported runtime exited without the required gas.interop probe_passed marker. $runtimeOutput"
            }
        }
        finally {
            Remove-Item -LiteralPath $logPath, $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
        }
    } | Out-Null
}

function Get-ChangedPaths {
    if ($null -ne $ChangedPath -and $ChangedPath.Count -gt 0) { return @($ChangedPath) }
    $tracked = @(& git -C $repoRoot diff --name-only HEAD)
    $untracked = @(& git -C $repoRoot ls-files --others --exclude-standard)
    return @($tracked + $untracked | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
}

function Invoke-Auto {
    $paths = @(Get-ChangedPaths)
    if ($paths.Count -eq 0 -or @($paths | Where-Object { $_ -notmatch '^docs/' }).Count -eq 0) {
        Write-Host "AUTO source-hygiene: documentation-only or no changed paths."
        Invoke-GitHygiene
        return
    }

    $joined = $paths -join "`n"
    $requiresQuick = $joined -match '\.(cs|csproj)$' -or $joined -match '(^|/)project\.godot$'
    $requiresHeadless = $joined -match '^(factory/shell|factory/gameplay/gas|factory/networking/netfox|addons/)' -or $joined -match '(^|/)project\.godot$'
    $requiresGasProbe = $joined -match '^factory/gameplay/gas/' -or $joined -match '^sandbox/gas/'
    $requiresExport = $joined -match '^(factory/steam|addons/godotsteam)/'
    $toolPaths = @($paths | Where-Object { $_ -match '^tools/.*\.ps1$' } | ForEach-Object { Join-Path $repoRoot $_ })

    Write-Host "AUTO changed paths: $($paths -join ', ')"
    if ($requiresQuick) { Write-Host "AUTO selected Quick: runtime source/config changed."; Invoke-Quick }
    else { Invoke-GitHygiene }
    if ($toolPaths.Count -gt 0) { Write-Host "AUTO selected PowerShell static validation: changed tool scripts."; Invoke-PowerShellParser $toolPaths }
    if ($requiresHeadless) { Write-Host "AUTO selected Headless: Godot runtime/plugin surface changed."; Invoke-Headless }
    if ($requiresGasProbe) { Write-Host "AUTO selected Gas probe: GAS surface changed."; Invoke-GasProbe }
    if ($requiresExport) { Write-Host "AUTO selected ExportSmoke: Steam/native export surface changed."; Invoke-ExportSmoke }
}

function Invoke-Quick {
    Invoke-GitHygiene
    Invoke-DeterministicBuild
    Invoke-UnitRegression
}

if ($Mode -eq "List") {
    Write-Host "Modes: Quick, Auto, Headless, Probe -Probe Gas, ExportSmoke, Full"
    Write-Host "Probe Gas: self-terminating GodotGAS interop contract."
    Write-Host "Manual-only probes: Steam native re-host (logged-in Steam); Netfox/Steam scenarios (two-account A/B)."
    Write-Host "A/B is operator-driven and intentionally not started by validate.ps1: tools/ab_test/run.ps1 -Mode Launch|Verify|Retry|Stop"
    return
}

switch ($Mode) {
    "Quick" { Invoke-Quick }
    "Auto" { Invoke-Auto }
    "Headless" { Invoke-Headless }
    "Probe" { if ($Probe -eq "Gas") { Invoke-GasProbe } else { throw "Probe mode requires -Probe Gas. Use -Mode List for inventory." } }
    "ExportSmoke" { Invoke-ExportSmoke }
    "Full" { Invoke-Quick; Invoke-Headless; Invoke-GasProbe; Invoke-ExportSmoke; Add-Result "SKIP" "a-b" "tools/ab_test/run.ps1" "operator-driven; not started by Full" }
}

if ($Mode -ne "Full") { Add-Result "SKIP" "a-b" "tools/ab_test/run.ps1" "operator-driven; not started by this mode" }
Write-Host ""
Write-Host "Validation summary:"
$results | ForEach-Object { Write-Host ("{0,-4} {1} - {2}" -f $_.Status, $_.Name, $_.Detail) }
if (@($results | Where-Object Status -eq "FAIL").Count -gt 0) { exit 1 }
