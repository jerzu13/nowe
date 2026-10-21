@echo off
rem Builds ACCoach.exe with the C# compiler that ships with Windows (.NET Framework 4.8) - no Visual Studio needed.
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%FW%\csc.exe" (
  echo Nie znaleziono kompilatora C# z .NET Framework 4.8. Zainstaluj .NET Framework 4.8: https://dotnet.microsoft.com/download/dotnet-framework/net48
  exit /b 1
)
set CSC=%FW%\csc.exe
"%CSC%" /nologo /codepage:65001 /optimize /target:exe /out:..\ACCoach.exe ^
  /r:System.Core.dll /r:"%FW%\WPF\System.Speech.dll" /r:System.Web.Extensions.dll ^
  /resource:src\map_template.html,ACCoach.map_template.html src\*.cs
if errorlevel 1 exit /b 1
if "%1"=="test" "%CSC%" /nologo /codepage:65001 /out:tools\AcSim.exe /r:System.Core.dll tools\AcSim.cs
if "%1"=="test" "%CSC%" /nologo /codepage:65001 /out:tools\AcReplay.exe /r:System.Core.dll tools\AcReplay.cs
