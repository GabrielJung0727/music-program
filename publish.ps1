## mono – QA용 Self-Contained 배포 스크립트
## 대상: Windows 11 x64 (런타임 설치 불필요)

$ErrorActionPreference = 'Stop'
$outDir = "$PSScriptRoot\publish\mono-win-x64"

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

$commonArgs = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    '-o', $outDir
)

Write-Host '>>> Mono.Core' -ForegroundColor Cyan
dotnet publish src/Mono.Core/Mono.Core.csproj @commonArgs

Write-Host '>>> Mono.Output' -ForegroundColor Cyan
dotnet publish src/Mono.Output/Mono.Output.csproj @commonArgs

Write-Host '>>> Mono.Control' -ForegroundColor Cyan
dotnet publish src/Mono.Control/Mono.Control.csproj @commonArgs

# data 폴더가 있으면 함께 복사
if (Test-Path "$PSScriptRoot\src\Mono.Core\data") {
    Copy-Item "$PSScriptRoot\src\Mono.Core\data" "$outDir\data" -Recurse -Force
}

# zip 패키징
$zipPath = "$PSScriptRoot\publish\mono-win-x64.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath

$size = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "`n=== 완료: $zipPath ($size MB) ===" -ForegroundColor Green
