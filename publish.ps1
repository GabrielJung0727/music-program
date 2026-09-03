## mono – QA용 Self-Contained + Velopack 릴리스
## 대상: Windows 11 x64 (런타임 설치 불필요)
##
## 사용:
##   .\publish.ps1
##   .\publish.ps1 -Version 0.1.1
##
## 자동 업데이트: publish\releases 의 Setup.exe로 설치한 뒤,
## GitHub Releases에 releases 폴더 산출물을 올리면 앱의 "받고 다시 시작"이 동작합니다.

param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = 'Stop'
$outDir = "$PSScriptRoot\publish\mono-win-x64"
$releasesDir = "$PSScriptRoot\publish\releases"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$commonArgs = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    "-p:Version=$Version",
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    '-o', $outDir
)

Write-Host ">>> 버전 $Version" -ForegroundColor Cyan
Write-Host '>>> Mono.Core' -ForegroundColor Cyan
dotnet publish src/Mono.Core/Mono.Core.csproj @commonArgs

Write-Host '>>> Mono.Output' -ForegroundColor Cyan
dotnet publish src/Mono.Output/Mono.Output.csproj @commonArgs

Write-Host '>>> Mono.Control' -ForegroundColor Cyan
dotnet publish src/Mono.Control/Mono.Control.csproj @commonArgs

if (Test-Path "$PSScriptRoot\src\Mono.Core\data") {
    Copy-Item "$PSScriptRoot\src\Mono.Core\data" "$outDir\data" -Recurse -Force
}

$zipPath = "$PSScriptRoot\publish\mono-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "zip: $zipPath ($zipSize MB)" -ForegroundColor Green

Write-Host '>>> Velopack pack' -ForegroundColor Cyan
dotnet tool update -g vpk --version 1.2.0 | Out-Null
if (Test-Path $releasesDir) { Remove-Item $releasesDir -Recurse -Force }
New-Item -ItemType Directory -Path $releasesDir | Out-Null
vpk pack --packId Mono --packVersion $Version --packDir $outDir --mainExe Mono.Control.exe --packTitle mono --outputDir $releasesDir

Write-Host "`n=== 완료 ===" -ForegroundColor Green
Write-Host "QA zip (수동 교체): $zipPath"
Write-Host "설치본 + 자동업데이트: $releasesDir  (Setup.exe를 실행해 설치)"
Write-Host "다음 버전: GitHub Releases에 $releasesDir 파일을 올리고, 앱에서 '받고 다시 시작'"
