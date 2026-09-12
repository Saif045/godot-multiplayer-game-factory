[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$HostPublicKey
)

<#+
Authorizes the host's existing GameFactory SSH public key for the currently
logged-in Windows user. This is intentionally per-user and does not require
administrator elevation or alter the SSH service configuration.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$sshDirectory = Join-Path $env:USERPROFILE '.ssh'
$authorizedKeysPath = Join-Path $sshDirectory 'authorized_keys'

New-Item -ItemType Directory -Force -Path $sshDirectory | Out-Null
Set-Content -LiteralPath $authorizedKeysPath -Value $HostPublicKey -Encoding ascii
& icacls.exe $authorizedKeysPath /inheritance:r /grant "${identity}:F" /grant 'SYSTEM:F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Unable to set authorized_keys permissions.' }

[pscustomobject]@{
    result = 'passed'
    user = $identity
    authorized_keys_path = $authorizedKeysPath
} | ConvertTo-Json -Depth 3
