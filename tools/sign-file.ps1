param(
    [Parameter(Mandatory = $true)][string]$File,
    [Parameter(Mandatory = $true)][string]$Thumbprint
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $File)) { exit 0 }

# vpk 는 이 스크립트를 powershell.exe(5.1)로 띄우고, 환경 변수는 publish.ps1 을 돌린
# 셸에서 그대로 물려받는다. pwsh 7 에서 릴리스를 돌리면 PSModulePath 앞쪽이 7.x 모듈
# 폴더로 채워지는데, 5.1 이 거기서 Microsoft.PowerShell.Security 를 집으면 두 가지 중
# 하나로 깨진다 — 못 찾으면 "Cert: 드라이브가 없습니다", 찾으면 "TypeData 가 이미
# 있습니다". 둘 다 인증서 문제처럼 보이지 않아 원인을 짚기 어렵다.
# 이 스크립트가 하는 일은 서명뿐이므로, 5.1 은 제 모듈 폴더만 보게 잘라 둔다.
if ($PSVersionTable.PSEdition -ne 'Core') {
    $env:PSModulePath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\Modules'
}
Import-Module Microsoft.PowerShell.Security

$cert = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
Set-AuthenticodeSignature -FilePath $File -Certificate $cert -HashAlgorithm SHA256 | Out-Null
