@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist ACCoach.exe (
  echo Brak pliku ACCoach.exe w tym folderze. Rozpakuj cale archiwum AC Coach.
  pause
  exit /b 1
)
ACCoach.exe --no-train
pause
