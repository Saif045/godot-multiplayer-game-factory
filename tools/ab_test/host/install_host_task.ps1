[CmdletBinding()]
param(
    [string]$TaskName = 'GameFactoryHost'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runnerPath = Join-Path $PSScriptRoot 'run_host.ps1'
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
if (-not (Test-Path -LiteralPath $runnerPath)) { throw "Host runner was not found: $runnerPath" }

$taskAction = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runnerPath`""
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $currentIdentity -LogonType Interactive -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew
$task = New-ScheduledTask -Action $taskAction -Principal $taskPrincipal -Settings $taskSettings
Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null
Write-Host "Installed $TaskName for $currentIdentity."
