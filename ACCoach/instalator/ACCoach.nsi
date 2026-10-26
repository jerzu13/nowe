; AC Coach – installer for the current Windows user (no administrator rights), English or Polish.
; Build: makensis ACCoach.nsi  (NSIS 3, Windows or Linux)
Unicode true
; zlib, not solid lzma: a solid lzma block looks like a packed payload to antivirus heuristics
SetCompressor zlib
RequestExecutionLevel user

!define APP      "AC Coach"
!define VERSION  "1.7.1"
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
VIAddVersionKey /LANG=1033 "ProductName" "${APP}"
VIAddVersionKey /LANG=1033 "FileDescription" "AC Coach setup – a coach for Assetto Corsa"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "CompanyName" "AC Coach"
VIAddVersionKey /LANG=1033 "LegalCopyright" "© 2026 AC Coach"
VIAddVersionKey /LANG=1033 "OriginalFilename" "AC_Coach_Setup.exe"

!define MUI_ICON "${SRC}\zrodla\ACCoach.ico"
!define MUI_UNICON "${SRC}\zrodla\ACCoach.ico"
!define MUI_ABORTWARNING
; the chosen language is remembered for the uninstaller (and written to language.txt for the app)
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "Software\ACCoach"
!define MUI_LANGDLL_REGISTRY_VALUENAME "Installer Language"
!define MUI_LANGDLL_ALLLANGUAGES
!define MUI_WELCOMEPAGE_TEXT "$(Welcome)"
!define MUI_FINISHPAGE_RUN "$INSTDIR\ACCoach.exe"
!define MUI_FINISHPAGE_RUN_TEXT "$(RunApp)"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\$(ManualFile)"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "$(ShowManual)"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Polish"
!insertmacro MUI_RESERVEFILE_LANGDLL

LangString Welcome ${LANG_ENGLISH} "This will install AC Coach – a coach for Assetto Corsa – for the current Windows user.$\r$\n$\r$\nNo administrator rights are needed. Every Windows user has their own settings and their own driving data.$\r$\n$\r$\nEverything – driving, settings, analysis and help – is in one AC Coach window."
LangString Welcome ${LANG_POLISH} "Instalator zainstaluje AC Coach – trenera do Assetto Corsa – dla bieżącego użytkownika Windows.$\r$\n$\r$\nNie są potrzebne uprawnienia administratora. Każdy użytkownik Windows ma własne ustawienia i własne dane jazd.$\r$\n$\r$\nWszystko – jazda, ustawienia, analiza i pomoc – jest w jednym oknie AC Coach."
LangString RunApp ${LANG_ENGLISH} "Start AC Coach"
LangString RunApp ${LANG_POLISH} "Uruchom AC Coach"
LangString ShowManual ${LANG_ENGLISH} "Show the manual"
LangString ShowManual ${LANG_POLISH} "Pokaż instrukcję"
LangString ManualFile ${LANG_ENGLISH} "HOW TO USE.txt"
LangString ManualFile ${LANG_POLISH} "JAK UZYWAC.txt"
LangString ManualLink ${LANG_ENGLISH} "Manual"
LangString ManualLink ${LANG_POLISH} "Instrukcja"
LangString UninstLink ${LANG_ENGLISH} "Uninstall AC Coach"
LangString UninstLink ${LANG_POLISH} "Odinstaluj AC Coach"
LangString AppDesc ${LANG_ENGLISH} "A coach for Assetto Corsa"
LangString AppDesc ${LANG_POLISH} "Trener do Assetto Corsa"
LangString NeedNet ${LANG_ENGLISH} "AC Coach needs .NET Framework 4.8, which is not installed on this PC.$\r$\n$\r$\nOpen the download page? (After installing .NET run this installer again.)"
LangString NeedNet ${LANG_POLISH} "AC Coach potrzebuje .NET Framework 4.8, którego nie ma na tym komputerze.$\r$\n$\r$\nOtworzyć stronę pobierania? (Po instalacji .NET uruchom ten instalator ponownie.)"
LangString SecMainName ${LANG_ENGLISH} "AC Coach (required)"
LangString SecMainName ${LANG_POLISH} "AC Coach (wymagane)"
LangString SecDeskName ${LANG_ENGLISH} "Desktop shortcut"
LangString SecDeskName ${LANG_POLISH} "Skrót na pulpicie"
LangString SecMainDesc ${LANG_ENGLISH} "The program, Start menu shortcuts and the uninstaller."
LangString SecMainDesc ${LANG_POLISH} "Program, skróty w menu Start i odinstalowywanie."
LangString SecDeskDesc ${LANG_ENGLISH} "An “AC Coach” shortcut on the desktop."
LangString SecDeskDesc ${LANG_POLISH} "Skrót „AC Coach” na pulpicie."
LangString AskSettings ${LANG_ENGLISH} "Also remove the AC Coach settings of this user?$\r$\n($APPDATA\AC Coach)$\r$\n$\r$\nLaps and reports in Documents are left untouched."
LangString AskSettings ${LANG_POLISH} "Usunąć też ustawienia AC Coach tego użytkownika?$\r$\n($APPDATA\AC Coach)$\r$\n$\r$\nOkrążenia i raporty w Dokumentach zostają nietknięte."

Function .onInit
  SetShellVarContext current
  StrCpy $LANGUAGE ${LANG_ENGLISH}   ; English is preselected in the language dialog
  !insertmacro MUI_LANGDLL_DISPLAY
  ; .NET Framework 4.8 (Release >= 528040) – part of Windows 10 1903+ and Windows 11
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 528040
    MessageBox MB_YESNO|MB_ICONEXCLAMATION "$(NeedNet)" IDNO +2
      ExecShell "open" "https://dotnet.microsoft.com/download/dotnet-framework/net48"
    Abort
  ${EndIf}
FunctionEnd

Function un.onInit
  !insertmacro MUI_UNGETLANGUAGE
FunctionEnd

Section "$(SecMainName)" SecMain
  SectionIn RO
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  SetOverwrite on
  File "${SRC}\ACCoach.exe"
  File "${SRC}\HOW TO USE.txt"
  File "${SRC}\JAK UZYWAC.txt"
  File "${SRC}\zrodla\ACCoach.ico"
  ; the app starts in the installer's language until the user picks one in Settings
  FileOpen $0 "$INSTDIR\language.txt" w
  ${If} $LANGUAGE == ${LANG_POLISH}
    FileWrite $0 "pl"
  ${Else}
    FileWrite $0 "en"
  ${EndIf}
  FileClose $0
  ; marker: an installed copy keeps its data in Documents, not in the program folder
  FileOpen $0 "$INSTDIR\.zainstalowany" w
  FileWrite $0 "${VERSION}"
  FileClose $0
  SetFileAttributes "$INSTDIR\.zainstalowany" HIDDEN
  Delete "$INSTDIR\Odinstaluj.exe"   ; uninstaller name up to 1.5

  ; fresh Start menu folder: drops the shortcuts of older versions and of the other language
  RMDir /r "$SMPROGRAMS\AC Coach"
  Delete "$DESKTOP\Konfiguracja AC Coach.lnk"
  CreateDirectory "$SMPROGRAMS\AC Coach"
  CreateShortcut "$SMPROGRAMS\AC Coach\AC Coach.lnk" "$INSTDIR\ACCoach.exe" "" "$INSTDIR\ACCoach.ico" 0 SW_SHOWNORMAL "" "$(AppDesc)"
  CreateShortcut "$SMPROGRAMS\AC Coach\$(ManualLink).lnk" "$INSTDIR\$(ManualFile)"
  CreateShortcut "$SMPROGRAMS\AC Coach\$(UninstLink).lnk" "$INSTDIR\Uninstall.exe"

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\ACCoach" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APP}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "AC Coach"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\ACCoach.ico"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" $0
SectionEnd

Section "$(SecDeskName)" SecDesktop
  SetShellVarContext current
  CreateShortcut "$DESKTOP\AC Coach.lnk" "$INSTDIR\ACCoach.exe" "" "$INSTDIR\ACCoach.ico" 0 SW_SHOWNORMAL "" "$(AppDesc)"
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} "$(SecMainDesc)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "$(SecDeskDesc)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Section "Uninstall"
  SetShellVarContext current
  Delete "$INSTDIR\ACCoach.exe"
  Delete "$INSTDIR\HOW TO USE.txt"
  Delete "$INSTDIR\JAK UZYWAC.txt"
  Delete "$INSTDIR\ACCoach.ico"
  Delete "$INSTDIR\language.txt"
  Delete "$INSTDIR\.zainstalowany"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  RMDir /r "$SMPROGRAMS\AC Coach"
  Delete "$DESKTOP\AC Coach.lnk"
  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\ACCoach"
  IfSilent done
  MessageBox MB_YESNO|MB_ICONQUESTION "$(AskSettings)" IDNO done
    RMDir /r "$APPDATA\AC Coach"
  done:
SectionEnd
