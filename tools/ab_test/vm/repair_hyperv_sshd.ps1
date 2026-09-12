#requires -RunAsAdministrator
[CmdletBinding()]
param()

<#+
Repairs the inbox OpenSSH service-mode administrator Match Group behavior on
the dedicated Hyper-V test VM. It keeps the existing GameFactory public key,
but switches to the standard per-user authorized_keys location and validates
the configuration before restarting sshd.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$programDataKey = Join-Path $env:ProgramData 'ssh\administrators_authorized_keys'
$sshDirectory = Join-Path $env:USERPROFILE '.ssh'
$userKey = Join-Path $sshDirectory 'authorized_keys'
$sshdConfigPath = Join-Path $env:ProgramData 'ssh\sshd_config'
$sshdExecutable = Join-Path $env:WINDIR 'System32\OpenSSH\sshd.exe'

if (-not (Test-Path -LiteralPath $programDataKey)) {
    throw "Expected bootstrap key file was not found: $programDataKey"
}

New-Item -ItemType Directory -Force -Path $sshDirectory | Out-Null
Get-Content -LiteralPath $programDataKey | Set-Content -LiteralPath $userKey -Encoding ascii
& icacls.exe $userKey /inheritance:r /grant "${identity}:F" /grant 'Administrators:F' /grant 'SYSTEM:F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to set authorized_keys permissions.' }

$config = Get-Content -LiteralPath $sshdConfigPath
$updatedConfig = @($config | Where-Object {
    $_ -notmatch '^\s*Match\s+Group\s+administrators\s*$' -and
    $_ -notmatch '^\s*AuthorizedKeysFile\s+__PROGRAMDATA__/ssh/administrators_authorized_keys\s*$'
})
$updatedConfig | Set-Content -LiteralPath $sshdConfigPath -Encoding ascii

& $sshdExecutable -t
if ($LASTEXITCODE -ne 0) { throw 'OpenSSH configuration validation failed; sshd was not restarted.' }

Restart-Service -Name sshd
[pscustomobject]@{
    result = 'passed'
    user = $identity
    authorized_keys_path = $userKey
    sshd_status = (Get-Service -Name sshd).Status.ToString()
} | ConvertTo-Json -Depth 3
