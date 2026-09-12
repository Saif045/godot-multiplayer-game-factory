#requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$HostPublicKey
)

<#+
Bootstraps the Windows Hyper-V guest used by the host/VM acceptance harness.

Run this once from an elevated PowerShell session in the guest. It deliberately
does not install Steam, launch GameFactory, mount a network drive, or alter the
game source tree. Builds are staged locally on the guest by the harness so a
host rebuild cannot replace files while a VM test is running.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$agentDirectory = 'C:\GameFactoryAgent'
$runnerPath = Join-Path $agentDirectory 'run_client.ps1'
$taskName = 'GameFactoryClient'
$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name

$capability = Get-WindowsCapability -Online -Name 'OpenSSH.Server~~~~0.0.1.0'
if ($capability.State -ne 'Installed') {
    Add-WindowsCapability -Online -Name $capability.Name | Out-Null
}

Set-Service -Name sshd -StartupType Automatic
Start-Service -Name sshd

if (-not (Get-NetFirewallRule -Name 'GameFactory-OpenSSH-Server' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -Name 'GameFactory-OpenSSH-Server' `
        -DisplayName 'GameFactory OpenSSH Server' `
        -Direction Inbound -Action Allow -Protocol TCP -LocalPort 22 -Profile Any | Out-Null
}

$buildDirectory = 'C:\GameFactoryBuilds'
New-Item -ItemType Directory -Force -Path $agentDirectory, $buildDirectory | Out-Null
# The scheduled task uses a limited interactive token. Give that exact account
# access to the harness-owned directories instead of depending on elevation.
foreach ($directory in @($agentDirectory, $buildDirectory)) {
    & icacls.exe $directory /grant "${currentIdentity}:(OI)(CI)M" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unable to grant the task account access to $directory." }
}

$sshDirectory = Join-Path $env:USERPROFILE '.ssh'
New-Item -ItemType Directory -Force -Path $sshDirectory | Out-Null
$authorizedKeysPath = Join-Path $sshDirectory 'authorized_keys'

New-Item -ItemType File -Force -Path $authorizedKeysPath | Out-Null
$existingKeys = Get-Content -LiteralPath $authorizedKeysPath -ErrorAction SilentlyContinue
if ($existingKeys -notcontains $HostPublicKey) {
    Add-Content -LiteralPath $authorizedKeysPath -Value $HostPublicKey
}

& icacls.exe $authorizedKeysPath /inheritance:r /grant "${currentIdentity}:F" /grant 'Administrators:F' /grant 'SYSTEM:F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to set authorized_keys permissions.' }

# The inbox OpenSSH configuration's special administrator-group match can
# reset service-mode connections on some Windows builds. The normal per-user
# authorized_keys path above is sufficient for this dedicated test VM.
$sshdConfigPath = Join-Path $env:ProgramData 'ssh\sshd_config'
$sshdConfig = Get-Content -LiteralPath $sshdConfigPath
$normalConfig = @($sshdConfig | Where-Object {
    $_ -notmatch '^\s*Match\s+Group\s+administrators\s*$' -and
    $_ -notmatch '^\s*AuthorizedKeysFile\s+__PROGRAMDATA__/ssh/administrators_authorized_keys\s*$'
})
$normalConfig | Set-Content -LiteralPath $sshdConfigPath -Encoding ascii
& (Join-Path $env:WINDIR 'System32\OpenSSH\sshd.exe') -t
if ($LASTEXITCODE -ne 0) { throw 'OpenSSH configuration validation failed.' }
Restart-Service -Name sshd

$taskAction = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runnerPath`""
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $currentIdentity -LogonType Interactive -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -MultipleInstances Parallel
$task = New-ScheduledTask -Action $taskAction -Principal $taskPrincipal -Settings $taskSettings
Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null

$status = [ordered]@{
    result = 'passed'
    observed_utc = [DateTimeOffset]::UtcNow.ToString('O')
    computer_name = $env:COMPUTERNAME
    user = $currentIdentity
    sshd_status = (Get-Service sshd).Status.ToString()
    authorized_keys_path = $authorizedKeysPath
    scheduled_task = $taskName
    staging_directory = 'C:\GameFactoryBuilds'
    ipv4_addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' } |
        Select-Object -ExpandProperty IPAddress)
}
$status | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $agentDirectory 'bootstrap_status.json') -Encoding utf8
Write-Host 'GameFactory Hyper-V guest bootstrap completed.'
Write-Host (Join-Path $agentDirectory 'bootstrap_status.json')
