Unicode true
!include "MUI2.nsh"

Name "HSIS"
OutFile "HSIS-${APP_VERSION}-win-x64-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\HSIS"
RequestExecutionLevel user
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1041 "ProductName" "HSIS"
VIAddVersionKey /LANG=1041 "FileDescription" "HSIS Setup"
VIAddVersionKey /LANG=1041 "FileVersion" "${APP_VERSION}"

!define MUI_FINISHPAGE_RUN "$INSTDIR\Hsis.App.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "Japanese"

Section "HSIS"
  SetOutPath "$INSTDIR"
  File /r "${SOURCE_DIR}\*"
  CreateShortcut "$SMPROGRAMS\HSIS.lnk" "$INSTDIR\Hsis.App.exe"
SectionEnd
