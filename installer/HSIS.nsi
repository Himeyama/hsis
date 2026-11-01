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
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "DisplayName" "HSIS"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "DisplayIcon" "$INSTDIR\Hsis.App.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\HSIS.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HSIS"
SectionEnd
