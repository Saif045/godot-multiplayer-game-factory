# Export selection and host startup gates; no transport or topology decisions.
function Resolve-GraphicalExport([string]$Directory) {
    $executable = Join-Path $Directory 'GameFactory.exe'
    if (-not (Test-Path -LiteralPath (Join-Path $Directory 'build_manifest.json'))) {
        throw "Completed export manifest is missing: $Directory"
    }
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Graphical export executable is missing: $executable"
    }
    return $executable
}

function Find-StartupReadyLog([string]$Directory, [string]$AttemptId, [DateTimeOffset]$ProcessStartedUtc) {
    $runsDirectory = Join-Path $Directory 'logs\runs'
    foreach ($run in Get-ChildItem -LiteralPath $runsDirectory -Directory -ErrorAction SilentlyContinue) {
        if (-not $run.Name.EndsWith("_$AttemptId", [StringComparison]::Ordinal)) { continue }
        $path = Join-Path $run.FullName 'game.jsonl'
        foreach ($line in Get-Content -LiteralPath $path -ErrorAction SilentlyContinue) {
            try {
                $entry = $line | ConvertFrom-Json
                if ($entry.RunId -eq $AttemptId -and $entry.Category -eq 'shell' -and $entry.Event -eq 'ready' -and
                    [DateTimeOffset]::Parse([string]$entry.Utc) -ge $ProcessStartedUtc) {
                    return $path
                }
            }
            catch { } # A partially flushed JSONL record cannot prove readiness.
        }
    }
    return $null
}

function Wait-GameFactoryStartup($Process, [string]$Directory, [string[]]$Arguments, [int]$TimeoutSeconds = 120) {
    $attemptArguments = @($Arguments | Where-Object { $_ -like '--test-run-id=*' })
    if ($attemptArguments.Count -ne 1) { throw 'Startup requires exactly one --test-run-id.' }
    $attemptId = $attemptArguments[0].Substring('--test-run-id='.Length)
    if ([string]::IsNullOrWhiteSpace($attemptId)) { throw 'Startup requires a nonempty attempt ID.' }
    $startedUtc = [DateTimeOffset]$Process.StartTime.ToUniversalTime()
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $Process.Refresh()
        if ($Process.HasExited) { throw "Process $($Process.Id) exited during startup (exit code $($Process.ExitCode))." }
        if ([string]$Process.MainWindowTitle -match 'Application Error') {
            throw "Process $($Process.Id) displayed an application-error dialog during startup: $($Process.MainWindowTitle)"
        }
        $readyLog = Find-StartupReadyLog $Directory $attemptId $startedUtc
        if ($null -ne $readyLog) {
            $Process.Refresh()
            if ($Process.HasExited) { throw "Process $($Process.Id) exited at the shell/ready boundary." }
            return
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Startup timed out after $TimeoutSeconds seconds: missing current-process shell/ready for attempt '$attemptId' under '$Directory\logs\runs'."
}
