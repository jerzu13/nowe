@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === AC Coach - pierwsze uruchomienie na tym komputerze ===
echo.
rem Pliki pobrane z internetu / rozpakowane z ZIP-a Windows oznacza jako zablokowane - odblokuj caly folder.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0.' -Recurse | Unblock-File" >nul 2>&1
echo [OK] Pliki odblokowane.

reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release >nul 2>&1
if errorlevel 1 (
  echo [!!] Brak .NET Framework 4.8. Pobierz: https://dotnet.microsoft.com/download/dotnet-framework/net48
  pause
  exit /b 1
)
powershell -NoProfile -Command "if ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release -ge 528040) { exit 0 } else { exit 1 }"
if errorlevel 1 (
  echo [!!] Za stary .NET Framework - potrzebny 4.8. Pobierz: https://dotnet.microsoft.com/download/dotnet-framework/net48
  pause
  exit /b 1
)
echo [OK] .NET Framework 4.8.

if not exist ACCoach.exe (
  echo [!!] Brak ACCoach.exe w tym folderze. Rozpakuj cale archiwum AC Coach.
  pause
  exit /b 1
)
echo.
echo Teraz test glosu i pikniec (sprawdz, czy slychac):
echo.
ACCoach.exe voicetest
echo.
echo Gotowe. Dalej uruchamiaj pliki 1-5 (opis w JAK UZYWAC.txt).
pause
