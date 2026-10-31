Unicode true
!include "MUI2.nsh"
Name "Hello App"
OutFile "Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\Hello App"
RequestExecutionLevel user

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "license.txt"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "Japanese"

Section "Application"
  SetOutPath "$INSTDIR"
  File "hello.txt"
  File "license.txt"
SectionEnd
