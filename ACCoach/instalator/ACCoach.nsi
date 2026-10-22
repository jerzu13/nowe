; AC Coach – instalator dla bieżącego użytkownika Windows (bez uprawnień administratora).
; Budowanie: makensis ACCoach.nsi  (NSIS 3, Windows albo Linux)
Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!define APP      "AC Coach"
!define VERSION  "1.3.1"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ACCoach"
!define SRC      ".."

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

Name "${APP}"
OutFile "AC_Coach_Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\AC Coach"
InstallDirRegKey HKCU "Software\ACCoach" "InstallDir"
BrandingText "${APP} ${VERSION}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1045 "ProductName" "${APP}"
VIAddVersionKey /LANG=1045 "FileDescription" "Instalator AC Coach – trener do Assetto Corsa"
VIAddVersionKey /LANG=1045 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1045 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1045 "LegalCopyright" " "

!define MUI_ICON "${SRC}\zrodla\ACCoach.ico"
!define MUI_UNICON "${SRC}\zrodla\ACCoach.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TEXT "Instalator zainstaluje AC Coach – trenera do Assetto Corsa – dla bieżącego użytkownika Windows.$\r$\n$\r$\nNie są potrzebne uprawnienia administratora. Każdy użytkownik Windows ma własne ustawienia i własne dane jazd.$\r$\n$\r$\nPo instalacji otworzy się okno konfiguracji."
!define MUI_FINISHPAGE_RUN "$INSTDIR\ACCoach.exe"
!define MUI_FINISHPAGE_RUN_PARAMETERS "config"
!define MUI_FINISHPAGE_RUN_TEXT "Otwórz konfigurację AC Coach"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\JAK UZYWAC.txt"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Pokaż instrukcję"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Polish"

Function .onInit
  SetShellVarContext current
  ; .NET Framework 4.8 (Release >= 528040) – jest w Windows 10 od 1903 i w Windows 11
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 528040
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "AC Coach potrzebuje .NET Framework 4.8, którego nie ma na tym komputerze.$\r$\n$\r$\nOtworzyć stronę pobierania? (Po instalacji .NET uruchom ten instalator ponownie.)" IDNO +2
      ExecShell "open" "https://dotnet.microsoft.com/download/dotnet-framework/net48"
    Abort
  ${EndIf}
FunctionEnd

Section "AC Coach (wymagane)" SecMain
  SectionIn RO
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "${SRC}\ACCoach.exe"
  File "${SRC}\JAK UZYWAC.txt"
  File "${SRC}\zrodla\ACCoach.ico"
  ; znacznik: zainstalowana kopia trzyma dane w Dokumentach, nie w folderze programu
  FileOpen $0 "$INSTDIR\.zainstalowany" w
  FileWrite $0 "${VERSION}"
  FileClose $0
  SetFileAttributes "$INSTDIR\.zainstalowany" HIDDEN

  CreateDirectory "$SMPROGRAMS\AC Coach"
  CreateShortcut "$SMPROGRAMS\AC Coach\AC Coach.lnk" "$INSTDIR\ACCoach.exe" "" "$INSTDIR\ACCoach.ico" 0 SW_SHOWNORMAL "" "Start trenera (tryb z konfiguracji)"
  CreateShortcut "$SMPROGRAMS\AC Coach\Analiza ostatniej sesji.lnk" "$INSTDIR\ACCoach.exe" "analyze --pause" "$INSTDIR\ACCoach.ico" 0 SW_SHOWNORMAL "" "Raport i mapa toru z ostatniej jazdy"
  CreateShortcut "$SMPROGRAMS\AC Coach\Konfiguracja AC Coach.lnk" "$INSTDIR\ACCoach.exe" "config" "$INSTDIR\ACCoach.ico" 0 SW_SHOWMINIMIZED "" "Ustawienia tego użytkownika"
  CreateShortcut "$SMPROGRAMS\AC Coach\Test głosu.lnk" "$INSTDIR\ACCoach.exe" "voicetest --pause" "$INSTDIR\ACCoach.ico" 0
  CreateShortcut "$SMPROGRAMS\AC Coach\Instrukcja.lnk" "$INSTDIR\JAK UZYWAC.txt"
  CreateShortcut "$SMPROGRAMS\AC Coach\Odinstaluj AC Coach.lnk" "$INSTDIR\Odinstaluj.exe"

  WriteUninstaller "$INSTDIR\Odinstaluj.exe"
  WriteRegStr HKCU "Software\ACCoach" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "AC Coach"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\ACCoach.ico"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" "$\"$INSTDIR\Odinstaluj.exe$\""
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" "$\"$INSTDIR\Odinstaluj.exe$\" /S"
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" $0
SectionEnd

Section "Skrót na pulpicie" SecDesktop
  SetShellVarContext current
  CreateShortcut "$DESKTOP\AC Coach.lnk" "$INSTDIR\ACCoach.exe" "" "$INSTDIR\ACCoach.ico" 0 SW_SHOWNORMAL "" "Start trenera"
  CreateShortcut "$DESKTOP\Konfiguracja AC Coach.lnk" "$INSTDIR\ACCoach.exe" "config" "$INSTDIR\ACCoach.ico" 0 SW_SHOWMINIMIZED "" "Ustawienia AC Coach"
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "Program, skróty w menu Start i odinstalowywanie."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Skróty „AC Coach” i „Konfiguracja AC Coach” na pulpicie."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  SetShellVarContext current
  Delete "$INSTDIR\ACCoach.exe"
  Delete "$INSTDIR\JAK UZYWAC.txt"
  Delete "$INSTDIR\ACCoach.ico"
  Delete "$INSTDIR\.zainstalowany"
  Delete "$INSTDIR\Odinstaluj.exe"
  RMDir "$INSTDIR"
  RMDir /r "$SMPROGRAMS\AC Coach"
  Delete "$DESKTOP\AC Coach.lnk"
  Delete "$DESKTOP\Konfiguracja AC Coach.lnk"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\ACCoach"
  IfSilent done
  MessageBox MB_YESNO|MB_ICONQUESTION "Usunąć też ustawienia AC Coach tego użytkownika?$\r$\n($APPDATA\AC Coach)$\r$\n$\r$\nOkrążenia i raporty w Dokumentach zostają nietknięte." IDNO done
    RMDir /r "$APPDATA\AC Coach"
  done:
SectionEnd
