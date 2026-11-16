## mono 설치
## GitHub 릴리스에서 Setup 을 받아, 같은 릴리스의 SHA256SUMS.txt 와 해시를 맞춰 본 뒤에만 실행한다.
## "인터넷에서 받은 파일" 표시(Zone.Identifier)는 해시가 맞을 때만 지운다. 맞지 않으면 실행하지 않는다.
## 이 저장소에서 직접 빌드한 Setup(publish\releases)이 있으면 그것을 쓴다. 받은 파일이 아니라 맞춰 볼 것이 없다.

param(
    # 비우면 최신 릴리스. 예: 0.5.9.3
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$repo = 'GabrielJung0727/music-program'
$setupName = 'Mono-win-Setup.exe'
$sumsName = 'SHA256SUMS.txt'

# SHA256SUMS.txt 에서 한 파일의 해시. 없으면 $null.
function Get-ExpectedHash([string]$SumsPath, [string]$FileName) {
    foreach ($line in Get-Content -LiteralPath $SumsPath) {
        $parts = $line.Trim() -split '\s+', 2
        if ($parts.Count -eq 2 -and $parts[1].TrimStart('*') -eq $FileName) {
            return $parts[0].ToLowerInvariant()
        }
    }
    return $null
}

# 받은 파일의 해시가 목록과 같은가. 목록에 그 파일이 없어도 거짓이다.
function Test-SetupHash([string]$SetupPath, [string]$SumsPath) {
    $expected = Get-ExpectedHash $SumsPath (Split-Path $SetupPath -Leaf)
    if (-not $expected) { return $false }
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $SetupPath).Hash.ToLowerInvariant()
    return $actual -eq $expected
}

$local = Join-Path $PSScriptRoot "publish\releases\$setupName"
if (Test-Path $local) {
    Write-Host "이 저장소에서 빌드한 Setup 을 씁니다: $local" -ForegroundColor Cyan
    $setup = $local
} else {
    # 매번 빈 폴더에 받는다. 같은 이름의 옛 파일이 섞이지 않게.
    $dir = Join-Path $env:TEMP ("mono-install-" + [guid]::NewGuid().ToString('n'))
    New-Item -ItemType Directory -Path $dir | Out-Null
    $tag = if ($Version) { @("v$Version") } else { @() }
    Write-Host "GitHub 에서 받는 중… ($(if ($Version) { "v$Version" } else { '최신 릴리스' }))" -ForegroundColor Cyan
    gh release download @tag --repo $repo --pattern $setupName --pattern $sumsName --dir $dir
    if ($LASTEXITCODE -ne 0) { throw "릴리스를 받지 못했습니다." }

    $setup = Join-Path $dir $setupName
    $sums = Join-Path $dir $sumsName
    if (-not (Test-Path $sums)) {
        throw "이 릴리스에는 $sumsName 이 없어 받은 파일을 확인할 수 없습니다. 실행하지 않습니다."
    }
    if (-not (Test-SetupHash $setup $sums)) {
        throw "받은 $setupName 의 SHA-256 이 릴리스의 목록과 다릅니다. 실행하지 않습니다: $setup"
    }
    Write-Host "SHA-256 확인됨" -ForegroundColor Green

    # 확인된 파일에서만 "인터넷에서 받은 파일" 표시를 지운다.
    Unblock-File -LiteralPath $setup
}

Write-Host "실행: $setup" -ForegroundColor Green
Start-Process -FilePath $setup -WorkingDirectory (Split-Path $setup)
