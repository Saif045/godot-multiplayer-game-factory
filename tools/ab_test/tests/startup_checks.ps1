Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'startup.ps1')
$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$fixture = Join-Path $repoRoot ('artifacts\startup_checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
function Assert-Throws([scriptblock]$Action, [string]$Pattern) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike $Pattern) { throw }
        return
    }
    throw "Expected failure: $Pattern"
}
try {
    Assert-Throws { Resolve-GraphicalExport $fixture } '*manifest is missing*'
    New-Item -ItemType File -Path (Join-Path $fixture 'GameFactory.console.exe'), (Join-Path $fixture 'build_manifest.json') | Out-Null
    Assert-Throws { Resolve-GraphicalExport $fixture } '*Graphical export executable is missing*'
    $graphical = Join-Path $fixture 'GameFactory.exe'
    New-Item -ItemType File -Path $graphical | Out-Null
    if ((Resolve-GraphicalExport $fixture) -ne $graphical) { throw 'Selected the console wrapper.' }

    $attempt = 'startup_regression_attempt_002'
    $started = [DateTimeOffset]::UtcNow
    $runPath = Join-Path $fixture ('logs\runs\fixture_' + $attempt)
    New-Item -ItemType Directory -Path $runPath -Force | Out-Null
    $logPath = Join-Path $runPath 'game.jsonl'
    function Write-Event([string]$Id, [DateTimeOffset]$Utc, [string]$Event = 'ready') {
        @{RunId=$Id; Utc=$Utc.ToString('O'); Category='shell'; Event=$Event} | ConvertTo-Json -Compress | Set-Content $logPath
    }
    Set-Content $logPath '{incomplete'
    if ($null -ne (Find-StartupReadyLog $fixture $attempt $started)) { throw 'Accepted malformed JSON.' }
    Write-Event 'startup_regression_attempt_001' $started
    if ($null -ne (Find-StartupReadyLog $fixture $attempt $started)) { throw 'Accepted a different attempt.' }
    Write-Event $attempt ($started.AddSeconds(-1))
    if ($null -ne (Find-StartupReadyLog $fixture $attempt $started)) { throw 'Accepted a pre-process event.' }
    Write-Event $attempt $started 'not_ready'
    if ($null -ne (Find-StartupReadyLog $fixture $attempt $started)) { throw 'Accepted the wrong boundary.' }

    # No processes are launched: exercise process-exit/error/timeout gates with
    # a process-shaped stub and real attempt-specific fixture logs.
    $process = [pscustomobject]@{Id=1; StartTime=$started.UtcDateTime; HasExited=$false; ExitCode=9; MainWindowTitle='GameFactory'}
    $process | Add-Member ScriptMethod Refresh { }
    Assert-Throws { Wait-GameFactoryStartup $process $fixture @("--test-run-id=$attempt") 0 } '*missing current-process shell/ready*'
    Write-Event $attempt $started
    Wait-GameFactoryStartup $process $fixture @("--test-run-id=$attempt") 1
    $process.HasExited = $true
    Assert-Throws { Wait-GameFactoryStartup $process $fixture @("--test-run-id=$attempt") 1 } '*exited during startup*'
    $process.HasExited = $false
    $process.MainWindowTitle = 'GameFactory - Application Error'
    Assert-Throws { Wait-GameFactoryStartup $process $fixture @("--test-run-id=$attempt") 1 } '*application-error dialog*'
    Write-Host 'PASS graphical export selection, attempt/time provenance, readiness, timeout, process exit and loader-error checks'
}
finally {
    # The fixture is a generated child of the repository artifact directory.
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\startup_checks')) + '\'
    if (-not [IO.Path]::GetFullPath($fixture).StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
