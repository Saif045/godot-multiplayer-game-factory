param(
    [Parameter(Mandatory = $true)] [int]$ProcessId,
    [Parameter(Mandatory = $true)] [string]$ProcessStartedUtc,
    [Parameter(Mandatory = $true)] [string]$AttemptId,
    [Parameter(Mandatory = $true)] [string]$StatusPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Update-Status([hashtable]$Update) {
    if (-not (Test-Path -LiteralPath $StatusPath)) { return }
    try {
        $existing = Get-Content -LiteralPath $StatusPath -Raw | ConvertFrom-Json
        $status = [ordered]@{}
        foreach ($property in $existing.PSObject.Properties) {
            $status[$property.Name] = $property.Value
        }
        foreach ($key in $Update.Keys) { $status[$key] = $Update[$key] }
        $temporaryPath = "$StatusPath.tmp"
        $status | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryPath -Encoding utf8
        Move-Item -LiteralPath $temporaryPath -Destination $StatusPath -Force
    }
    catch {
        # Exit observation must never affect the launched game or its runner.
    }
}

$started = [DateTimeOffset]::Parse($ProcessStartedUtc)
$exitCode = $null
try {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    $process.WaitForExit()
    $exitCode = $process.ExitCode
}
catch {
    $exitCode = "unavailable: $($_.Exception.GetType().Name)"
}

$ended = [DateTimeOffset]::UtcNow
$applicationEvents = @(
    Get-WinEvent -FilterHashtable @{ LogName = "Application"; StartTime = $started.UtcDateTime.AddSeconds(-2); EndTime = $ended.UtcDateTime.AddSeconds(10) } -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Id -in 1000, 1001, 1026 -or
            $_.ProviderName -in @("Application Error", "Windows Error Reporting", ".NET Runtime") -or
            $_.Message -match "GameFactory|$ProcessId|0xC0000142"
        } |
        Select-Object -First 10 | ForEach-Object {
            [ordered]@{
                time_utc = $_.TimeCreated.ToUniversalTime().ToString("O")
                id = $_.Id
                provider = $_.ProviderName
                message = ($_.Message -replace "[\r\n]+", " ").Trim()
            }
        }
)

Update-Status @{
    process_exit_observed_utc = $ended.ToString("O")
    process_exit_code = $exitCode
    process_exit_watcher_attempt_id = $AttemptId
    application_events_near_exit = $applicationEvents
}
