## mono – Self-Contained + Velopack 릴리스
## 대상: Windows 11 x64
##
##   .\publish.ps1 -Version 0.1.2
##   .\publish.ps1 -Version 0.1.2 -SkipZip -GitHubRelease

param(
    [string]$Version = "0.1.2",
    [switch]$GitHubRelease,
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'
$outDir = "$PSScriptRoot\publish\mono-win-x64"
$releasesDir = "$PSScriptRoot\publish\releases"
$iconPath = "$PSScriptRoot\src\Mono.Control\Assets\icons\app\mono-app.ico"
$logoPng = "$PSScriptRoot\src\Mono.Control\Assets\icons\tray\mono-tray-light.png"
$splashPath = "$PSScriptRoot\publish\installer-assets\splash.png"
$signScript = "$PSScriptRoot\tools\sign-file.ps1"

function New-IcoFromPng([string]$Png, [string]$Ico) {
    # Prefer already-built high-contrast app icon; tray PNG is too dark for Explorer.
    $preferred = Join-Path $PSScriptRoot 'src\Mono.Control\Assets\icons\app\mono-app.ico'
    if ((Test-Path $preferred) -and ((Get-Item $preferred).Length -gt 1000)) {
        Copy-Item $preferred $Ico -Force
        return
    }
    Add-Type -AssemblyName System.Drawing
    $img = [System.Drawing.Image]::FromFile($Png)
    try {
        $bmp = New-Object System.Drawing.Bitmap $img, 256, 256
        try {
            $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
            $fs = [System.IO.File]::Create($Ico)
            try { $icon.Save($fs) } finally { $fs.Close() }
            $icon.Dispose()
        } finally { $bmp.Dispose() }
    } finally { $img.Dispose() }
}

function New-Splash([string]$Png, [string]$Dest) {
    Add-Type -AssemblyName System.Drawing
    New-Item -ItemType Directory -Force -Path (Split-Path $Dest) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap 620, 300
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(17, 17, 19))
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $logo = [System.Drawing.Image]::FromFile($Png)
    $size = 88
    $g.DrawImage($logo, [int]((620 - $size) / 2), 70, $size, $size)
    $font = New-Object System.Drawing.Font 'Segoe UI Semibold', 22
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, 242, 246))
    $text = 'mono'
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($text, $font, $brush, (New-Object System.Drawing.RectangleF 0, 175, 620, 40), $sf)
    $g.Dispose(); $logo.Dispose(); $font.Dispose(); $brush.Dispose()
    $bmp.Save($Dest, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Get-MonoCodeCert {
    $existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq 'CN=mono' -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1
    if (-not $existing) {
        $existing = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=mono' `
            -CertStoreLocation Cert:\CurrentUser\My -KeyExportPolicy Exportable `
            -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5)
    }
    $cer = Join-Path $env:TEMP 'mono-codesign.cer'
    Export-Certificate -Cert $existing -FilePath $cer | Out-Null
    foreach ($storeName in @('TrustedPublisher')) {
        $have = Get-ChildItem "Cert:\CurrentUser\$storeName" -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $existing.Thumbprint }
        if (-not $have) {
            try {
                Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\CurrentUser\$storeName" | Out-Null
            } catch {
                Write-Host "인증서를 $storeName 에 넣지 못했습니다: $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }
    # Root trust (needed for Valid Authenticode / less SmartScreen noise). May need UI in interactive sessions.
    try {
        & certutil.exe -user -addstore Root $cer | Out-Null
    } catch {
        Write-Host "Root 인증서 등록은 나중에 수동으로 가능합니다." -ForegroundColor Yellow
    }
    return $existing
}

function Save-GithubTokenToPrefs {
    try {
        $token = (gh auth token 2>$null)
        if (-not $token) { return }
        $token = $token.Trim()
        [System.Environment]::SetEnvironmentVariable('GITHUB_TOKEN', $token, 'User')
        $dir = Join-Path $env:LOCALAPPDATA 'Mono'
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        $prefs = Join-Path $dir 'prefs.ini'
        $lines = @()
        if (Test-Path $prefs) {
            $lines = Get-Content $prefs | Where-Object { $_ -notmatch '^(?i)github_token=' }
        }
        $lines += "github_token=$token"
        Set-Content -Path $prefs -Value $lines -Encoding UTF8
    } catch { }
}

if (-not (Test-Path $iconPath) -and (Test-Path $logoPng)) {
    New-IcoFromPng $logoPng $iconPath
}
New-Splash $logoPng $splashPath
Copy-Item $logoPng "$PSScriptRoot\src\Mono.Setup\Assets\logo.png" -Force
Copy-Item $iconPath "$PSScriptRoot\src\Mono.Setup\mono-app.ico" -Force

$cert = Get-MonoCodeCert
$thumb = $cert.Thumbprint
$signTemplate = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$signScript`" -File {{file}} -Thumbprint $thumb"

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
if ($LASTEXITCODE -ne 0) { throw "Core publish failed" }

Write-Host '>>> Mono.Output' -ForegroundColor Cyan
dotnet publish src/Mono.Output/Mono.Output.csproj @commonArgs
if ($LASTEXITCODE -ne 0) { throw "Output publish failed" }

Write-Host '>>> Mono.Control' -ForegroundColor Cyan
dotnet publish src/Mono.Control/Mono.Control.csproj @commonArgs
if ($LASTEXITCODE -ne 0) { throw "Control publish failed" }

if (Test-Path "$PSScriptRoot\src\Mono.Core\data") {
    Copy-Item "$PSScriptRoot\src\Mono.Core\data" "$outDir\data" -Recurse -Force
}

if (-not $SkipZip) {
    $zipPath = "$PSScriptRoot\publish\mono-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "zip: $zipPath ($zipSize MB)" -ForegroundColor Green
}

Write-Host '>>> Velopack pack' -ForegroundColor Cyan
dotnet tool update -g vpk --version 1.2.0 | Out-Null
if (Test-Path $releasesDir) { Remove-Item $releasesDir -Recurse -Force }
New-Item -ItemType Directory -Path $releasesDir | Out-Null

$vpkArgs = @(
    'pack',
    '--packId', 'Mono',
    '--packVersion', $Version,
    '--packDir', $outDir,
    '--mainExe', 'Mono.Control.exe',
    '--packTitle', 'mono',
    '--packAuthors', 'mono',
    '--outputDir', $releasesDir,
    '--icon', $iconPath,
    '--splashImage', $splashPath,
    '--splashProgressColor', '#6D6DF6',
    '--instWelcome', "$PSScriptRoot\tools\installer\welcome.txt",
    '--instConclusion', "$PSScriptRoot\tools\installer\conclusion.txt",
    '--shortcuts', 'StartMenuRoot',
    '--signTemplate', $signTemplate
)
vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host '>>> 설치 UI' -ForegroundColor Cyan
$payloadDir = "$PSScriptRoot\src\Mono.Setup\Payload"
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
Copy-Item "$releasesDir\Mono-win-Setup.exe" "$payloadDir\VelopackSetup.exe" -Force

$installerOut = "$PSScriptRoot\publish\installer"
if (Test-Path $installerOut) { Remove-Item $installerOut -Recurse -Force }
dotnet publish src/Mono.Setup/Mono.Setup.csproj -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false -o $installerOut
if ($LASTEXITCODE -ne 0) { throw "Setup UI publish failed" }

$branded = Get-ChildItem $installerOut -Filter 'Mono.Setup.exe' | Select-Object -First 1
if (-not $branded) { throw "Mono.Setup.exe missing" }
& $signScript -File $branded.FullName -Thumbprint $thumb
Copy-Item $branded.FullName "$releasesDir\Mono-win-Setup.exe" -Force
Save-GithubTokenToPrefs

if ($GitHubRelease) {
    Write-Host ">>> GitHub Release v$Version" -ForegroundColor Cyan
    $assets = Get-ChildItem $releasesDir -File |
        Where-Object { $_.Name -notmatch 'Portable' } |
        ForEach-Object { $_.FullName }
    if (-not $assets) { throw "no Velopack assets in $releasesDir" }
    $ErrorActionPreference = 'Continue'
    gh release view "v$Version" 2>$null | Out-Null
    $exists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = 'Stop'
    if ($exists) {
        gh release delete "v$Version" --yes
    }
    gh release create "v$Version" @assets --title "mono $Version" --notes "mono $Version — Setup.exe로 설치한 클라이언트용 업데이트 피드."
}

Write-Host "`n=== 완료 ===" -ForegroundColor Green
Write-Host "설치: $releasesDir\Mono-win-Setup.exe"
Write-Host "다음 버전: .\publish.ps1 -Version x.y.z -SkipZip -GitHubRelease"
