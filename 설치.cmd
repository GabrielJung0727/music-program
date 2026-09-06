@echo off
REM Explorer에서 Setup을 더블클릭하면 SmartScreen이 뜹니다.
REM 이 런처(또는 install.ps1)로 실행하면 로컬 서명본을 바로 엽니다.
setlocal
set "SETUP=%~dp0publish\releases\Mono-win-Setup.exe"
if not exist "%SETUP%" set "SETUP=%USERPROFILE%\Desktop\Mono-win-Setup.exe"
if not exist "%SETUP%" (
  echo Setup 파일이 없습니다. 먼저 publish.ps1 을 실행하세요.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File -LiteralPath '%SETUP%' -ErrorAction SilentlyContinue; Start-Process -FilePath '%SETUP%'"
