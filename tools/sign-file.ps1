param(
    [Parameter(Mandatory = $true)][string]$File,
    [Parameter(Mandatory = $true)][string]$Thumbprint
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $File)) { exit 0 }
$cert = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
Set-AuthenticodeSignature -FilePath $File -Certificate $cert -HashAlgorithm SHA256 | Out-Null
