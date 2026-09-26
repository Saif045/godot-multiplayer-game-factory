Set-StrictMode -Version Latest

function Invoke-BuildTestClientIsolated {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BuildScript,
        [Parameter(Mandatory = $true)]
        [string]$Godot,
        [Parameter(Mandatory = $true)]
        [string]$OutputDirectory,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    # A nested powershell.exe with piped standard handles can leave the Godot
    # console exporter alive after savepack. Invoke the build script directly:
    # it retains its isolated Godot profile and bounded exporter timeout while
    # matching the direct invocation proven to exit normally.
    $standardOutput = ""
    $standardError = ""
    $exitCode = 0
    try {
        $output = @(& $BuildScript -Godot $Godot -OutputDirectory $OutputDirectory -ExportTimeoutSeconds $TimeoutSeconds -Clean 2>&1)
        $standardOutput = ($output | Out-String)
        if ($LASTEXITCODE -ne 0) {
            $exitCode = $LASTEXITCODE
        }
    }
    catch {
        $exitCode = 1
        $standardError = ($_ | Out-String)
    }

    return [PSCustomObject]@{
        ProcessId = $PID
        ExitCode = $exitCode
        TimedOut = $false
        StandardOutput = $standardOutput
        StandardError = $standardError
    }
}
