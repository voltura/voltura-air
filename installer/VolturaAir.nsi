!ifndef APP_VERSION
  !error "APP_VERSION must be provided"
!endif

!ifndef APP_VERSION_QUAD
  !error "APP_VERSION_QUAD must be provided"
!endif

!ifndef APP_ESTIMATED_SIZE_KB
  !error "APP_ESTIMATED_SIZE_KB must be provided"
!endif

!ifndef RUNTIME
  !define RUNTIME "win-x64"
!endif

!ifndef PUBLISH_DIR
  !error "PUBLISH_DIR must be provided"
!endif

!ifndef OUTPUT_FILE
  !error "OUTPUT_FILE must be provided"
!endif

!define APP_NAME "Voltura Air"
!define EXE_NAME "VolturaAir.Host.exe"
!define PUBLISHER "Voltura AB"
!define DEVELOPER "Joakim Skoglund"
!define PRODUCT_URL "https://voltura.se/air"
!define SUPPORT_EMAIL "air@voltura.se"
!define POSTAL_ADDRESS "Voltura AB, H${U+00E4}stholmsv${U+00E4}gen 33, SE-131 71 Nacka, Sweden"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Voltura Air"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"

!ifdef TEST_NO_INSTALLER_COMPRESSION
!define INSTALLER_TEST_SUFFIX "-test-uncompressed"
!else
!define INSTALLER_TEST_SUFFIX ""
!endif

!ifdef FRAMEWORK_DEPENDENT
!define INSTALLER_FILE_SUFFIX ""
!define INSTALLER_KIND "Framework-dependent"
!define MUI_WELCOME_TEXT "Set up ${APP_NAME} on this Windows PC to control it from your phone or tablet over your local network.$\r$\n$\r$\nIf the required .NET 10 Windows Desktop and ASP.NET Core runtimes are missing, setup downloads and installs them automatically. An internet connection and Windows administrator approval may be required."
!else
!define INSTALLER_FILE_SUFFIX "-full"
!define INSTALLER_KIND "Full"
!define MUI_WELCOME_TEXT "Set up ${APP_NAME} on this Windows PC to control it from your phone or tablet over your local network.$\r$\n$\r$\nThis installer includes all required components, adds a Start Menu shortcut, and keeps everything in your user profile."
!endif

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"

Unicode true
Name "${APP_NAME}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_NAME}"
RequestExecutionLevel user
XPStyle on
ManifestDPIAware true
ManifestSupportedOS all
!ifdef TEST_NO_INSTALLER_COMPRESSION
SetDatablockOptimize off
SetCompress off
!else
SetCompressor lzma
!endif
VIProductVersion "${APP_VERSION_QUAD}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "CompanyName" "${PUBLISHER}"
VIAddVersionKey "FileDescription" "${APP_NAME} Installer"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "OriginalFilename" "VolturaAir-Setup-${APP_VERSION}-${RUNTIME}${INSTALLER_FILE_SUFFIX}${INSTALLER_TEST_SUFFIX}.exe"
VIAddVersionKey "InternalName" "VolturaAirSetup"
VIAddVersionKey "LegalCopyright" "Copyright (c) ${PUBLISHER}"
VIAddVersionKey "Comments" "Developer: ${DEVELOPER}; Website: ${PRODUCT_URL}; Email: ${SUPPORT_EMAIL}; Address: ${POSTAL_ADDRESS}"

!define MUI_ABORTWARNING
!define MUI_ICON "${__FILEDIR__}\..\apps\windows-host\Assets\VolturaAir.ico"
!define MUI_UNICON "${__FILEDIR__}\..\apps\windows-host\Assets\VolturaAir.ico"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "${__FILEDIR__}\assets\installer-header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "${__FILEDIR__}\assets\installer-header.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${__FILEDIR__}\assets\installer-welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${__FILEDIR__}\assets\installer-welcome.bmp"
!define MUI_WELCOMEPAGE_TITLE "Install ${APP_NAME}"
!define MUI_WELCOMEPAGE_TEXT "${MUI_WELCOME_TEXT}"
!define MUI_FINISHPAGE_TITLE "${APP_NAME} is ready"
!define MUI_FINISHPAGE_TEXT "Start ${APP_NAME}, scan the pairing code from your phone or tablet, and control your PC from the sofa."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXE_NAME}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APP_NAME}"
!define MUI_CUSTOMFUNCTION_GUIINIT RestoreInstallerWindow

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Function RestoreInstallerWindow
  ShowWindow $HWNDPARENT ${SW_RESTORE}
  BringToFront
FunctionEnd

Section "Install"
  Call PromptCloseRunningApp

  !ifdef FRAMEWORK_DEPENDENT
  InitPluginsDir
  File /oname=$PLUGINSDIR\Install-FrameworkRuntime.ps1 "${__FILEDIR__}\Install-FrameworkRuntime.ps1"
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\Install-FrameworkRuntime.ps1"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air could not install the required .NET 10 runtimes. Check your internet connection and approve the Windows administrator prompt.$\r$\n$\r$\nDetails:$\r$\n$1"
    Abort "The required .NET 10 runtimes were not installed."
  ${EndIf}
  !endif

  RMDir /r "$INSTDIR"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${EXE_NAME}" "" "$INSTDIR\${EXE_NAME}" 0
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "${PRODUCT_URL}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "HelpLink" "${PRODUCT_URL}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Contact" "${SUPPORT_EMAIL}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Comments" "${INSTALLER_KIND} installer. Developer: ${DEVELOPER}; Address: ${POSTAL_ADDRESS}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${EXE_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" "$\"$INSTDIR\Uninstall.exe$\" /S"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${APP_ESTIMATED_SIZE_KB}
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Call un.PromptCloseRunningApp

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegValue HKCU "${RUN_KEY}" "${APP_NAME}"

  RMDir /r "$INSTDIR"
SectionEnd

Function PromptCloseRunningApp
  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Get-Process -Name VolturaAir.Host -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"'
  Pop $0
  Pop $1

  ${If} $0 != 0
    Return
  ${EndIf}

  MessageBox MB_ICONEXCLAMATION|MB_OKCANCEL "${APP_NAME} is currently running. Setup needs to close it before continuing." IDOK install_close IDCANCEL install_cancel

install_close:
  nsExec::ExecToLog 'powershell -NoProfile -ExecutionPolicy Bypass -Command "Stop-Process -Name VolturaAir.Host -Force -ErrorAction SilentlyContinue"'
  Sleep 1000

  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Get-Process -Name VolturaAir.Host -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"'
  Pop $0
  Pop $1

  ${If} $0 == 0
    MessageBox MB_ICONSTOP|MB_RETRYCANCEL "${APP_NAME} is still running. Close it manually, then retry." IDRETRY install_close IDCANCEL install_cancel
  ${EndIf}

  Return

install_cancel:
  Abort "Setup was canceled because ${APP_NAME} is still running."
FunctionEnd

Function un.PromptCloseRunningApp
  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Get-Process -Name VolturaAir.Host -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"'
  Pop $0
  Pop $1

  ${If} $0 != 0
    Return
  ${EndIf}

  MessageBox MB_ICONEXCLAMATION|MB_OKCANCEL "${APP_NAME} is currently running. Uninstall needs to close it before continuing." IDOK uninstall_close IDCANCEL uninstall_cancel

uninstall_close:
  nsExec::ExecToLog 'powershell -NoProfile -ExecutionPolicy Bypass -Command "Stop-Process -Name VolturaAir.Host -Force -ErrorAction SilentlyContinue"'
  Sleep 1000

  nsExec::ExecToStack 'powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Get-Process -Name VolturaAir.Host -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"'
  Pop $0
  Pop $1

  ${If} $0 == 0
    MessageBox MB_ICONSTOP|MB_RETRYCANCEL "${APP_NAME} is still running. Close it manually, then retry." IDRETRY uninstall_close IDCANCEL uninstall_cancel
  ${EndIf}

  Return

uninstall_cancel:
  Abort "Uninstall was canceled because ${APP_NAME} is still running."
FunctionEnd
