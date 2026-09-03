## mono 설치 (SmartScreen 회피)
## GitHub 브라우저 다운로드는 Zone.Identifier가 붙어 빨간 SmartScreen이 뜹니다.
## 이 스크립트는 로컬 산출물을 쓰거나, gh로 받은 뒤 Unblock-File 합니다.

param(
    [string]$Version = "0.1.2"
)

$ErrorActionPreference = 'Stop'
$local = Join-Path $PSScriptRoot "publish\releases\Mono-win-Setup.exe"
$dest = Join-Path $env:TEMP "Mono-win-Setup.exe"

if (Test-Path $local) {
    $setup = $local
} else {
    Write-Host "로컬 Setup 없음 → GitHub에서 받는 중…" -ForegroundColor Cyan
    gh release download "v$Version" --repo GabrielJung0727/music-program --pattern "Mono-win-Setup.exe" --dir $env:TEMP --clobber
    $setup = $dest
}

Unblock-File -LiteralPath $setup -ErrorAction SilentlyContinue
Remove-Item -LiteralPath ($setup + ':Zone.Identifier') -ErrorAction SilentlyContinue
Write-Host "실행: $setup" -ForegroundColor Green
Start-Process -FilePath $setup -WorkingDirectory (Split-Path $setup)
