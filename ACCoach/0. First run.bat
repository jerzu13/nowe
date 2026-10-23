@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === AC Coach - first run on this PC / pierwsze uruchomienie ===
echo.
rem Windows marks files downloaded from the internet / extracted from a ZIP as blocked - unblock the whole folder.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0.' -Recurse | Unblock-File" >nul 2>&1
echo [OK] Files unblocked / pliki odblokowane.

reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release >nul 2>&1
if errorlevel 1 (
  echo [!!] .NET Framework 4.8 missing / brak. Download: https://dotnet.microsoft.com/download/dotnet-framework/net48
  pause
  exit /b 1
)
powershell -NoProfile -Command "if ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release -ge 528040) { exit 0 } else { exit 1 }"
if errorlevel 1 (
  echo [!!] .NET Framework too old, 4.8 needed / za stary. Download: https://dotnet.microsoft.com/download/dotnet-framework/net48
  pause
  exit /b 1
)
echo [OK] .NET Framework 4.8.

if not exist ACCoach.exe (
  echo [!!] ACCoach.exe not found in this folder - extract the whole AC Coach archive.
  pause
  exit /b 1
)
echo.
echo Done. Starting AC Coach - from now on simply open ACCoach.exe.
start "" ACCoach.exe
