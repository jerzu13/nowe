@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist ACCoach.exe (
  echo Brak pliku ACCoach.exe w tym folderze. Rozpakuj cale archiwum AC Coach.
  pause
  exit /b 1
)
rem Fokus na jednym zakrecie. Konkretny zakret: dopisz numer, np.  ACCoach.exe --train --fokus 4
ACCoach.exe --train --fokus
pause
