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
!define WEBCAM_SETUP "PhoneWebcam\VolturaAir.WebcamSetup.exe"
!define WEBCAM_PROTECTED_SETUP "$PROGRAMFILES64\Voltura Air Webcam\VolturaAir.WebcamSetup.exe"
!define INSTALL_TRANSACTION_SCRIPT "VolturaAir.InstallTransaction.ps1"
!define INSTALL_TRANSACTION_JOURNAL "$LOCALAPPDATA\Voltura Air\installer-transaction.json"
!define RECOVERY_UNINSTALLER "$LOCALAPPDATA\Voltura Air\Uninstall-Recovery.exe"
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
!define MUI_FINISHPAGE_REBOOTLATER_DEFAULT
!define MUI_CUSTOMFUNCTION_GUIINIT RestoreInstallerWindow

!define MUI_PAGE_CUSTOMFUNCTION_SHOW FinishInstallerWindowActivation
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

!ifdef FRAMEWORK_DEPENDENT
Var PrerequisiteRebootRequired
!endif
Var StagingDirectory
Var WebcamRemovedForUpdate
Var WebcamRollbackHelper

!macro ExecCheckedToStack COMMAND_VAR TOO_LONG_LABEL
  StrLen $3 ${COMMAND_VAR}
  IntCmp $3 ${NSIS_MAX_STRLEN} ${TOO_LONG_LABEL} 0 ${TOO_LONG_LABEL}
  nsExec::ExecToStack '${COMMAND_VAR}'
!macroend

!if ${NSIS_PTR_SIZE} > 4
  !define /math VOLTURA_SHELLEXECUTEINFO_SIZE 14 * ${NSIS_PTR_SIZE}
!else
  !define VOLTURA_SHELLEXECUTEINFO_SIZE 60
!endif

!macro ExecElevatedAndWait FILE_PATH PARAMETERS RESULT_VAR
  System::Store S
  System::Call '*(&i${VOLTURA_SHELLEXECUTEINFO_SIZE})p.r0'
  System::Call '*$0(i ${VOLTURA_SHELLEXECUTEINFO_SIZE}, i 0x40, p $HWNDPARENT, t "runas", t "${FILE_PATH}", t "${PARAMETERS}", t "$PLUGINSDIR", i ${SW_HIDE})p.r0'
  System::Call 'shell32::ShellExecuteEx(t)(p r0)i.r1 ?e'
  Pop $2
  ${If} $1 != 0
    System::Call '*$0(is, i, p, p, p, p, p, p, p, p, p, p, p, p, p.r1)'
    System::Call 'kernel32::WaitForSingleObject(p r1, i -1)i.r2'
    ${If} $2 == 0
      System::Call 'kernel32::GetExitCodeProcess(p r1, *i.r2)i.r3'
      ${If} $3 != 0
        Push $2
      ${Else}
        Push 51002
      ${EndIf}
    ${Else}
      Push 51002
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r1)'
  ${ElseIf} $2 == 1223
    Push 51001
  ${Else}
    Push 51002
  ${EndIf}
  System::Free $0
  System::Store L
  Pop ${RESULT_VAR}
!macroend

Function RestoreInstallerWindow
  ShowWindow $HWNDPARENT ${SW_RESTORE}
  ; SmartScreen can leave the new installer process behind the launching window.
  ; A normal foreground request is then reduced to a flashing taskbar button.
  ; Enter the topmost z-order band now, then finish activation when MUI shows the
  ; interactive welcome page.
  System::Call 'user32::SetWindowPos(p $HWNDPARENT, p -1, i 0, i 0, i 0, i 0, i 0x43) i .r0'
FunctionEnd

Function FinishInstallerWindowActivation
  System::Call 'user32::SetForegroundWindow(p $HWNDPARENT) i .r0'
  System::Call 'user32::SetWindowPos(p $HWNDPARENT, p -2, i 0, i 0, i 0, i 0, i 0x43) i .r0'
FunctionEnd

Function .onInit
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=${INSTALL_TRANSACTION_SCRIPT} "${__FILEDIR__}\InstallTransaction.ps1"
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action Recover -InstallDirectory "$INSTDIR" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air setup found an installation transaction it could not safely recover. No unexpected directory was overwritten."
    Abort "Installer transaction recovery failed."
  ${EndIf}
FunctionEnd

Function un.onInit
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  File /oname=${INSTALL_TRANSACTION_SCRIPT} "${__FILEDIR__}\InstallTransaction.ps1"
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action PrepareUninstall -InstallDirectory "$INSTDIR" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air uninstall found a transaction it could not safely resume."
    Abort "Uninstall transaction recovery failed."
  ${EndIf}
FunctionEnd

Function RollbackPromotedInstall
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action Rollback -InstallDirectory "$INSTDIR" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
  Pop $0
  Pop $1
  Push $0
FunctionEnd

Section "Voltura Air (required)" SEC_CORE
  SectionIn RO
  !ifdef FRAMEWORK_DEPENDENT
  StrCpy $PrerequisiteRebootRequired 0

  Call TestWindowsDesktopRuntime
  Pop $0
  ${If} $0 == 0
    DetailPrint ".NET 10 Windows Desktop runtime is already present."
  ${Else}
    Call InstallWindowsDesktopRuntime
  ${EndIf}

  Call TestAspNetCoreRuntime
  Pop $0
  ${If} $0 == 0
    DetailPrint ".NET 10 ASP.NET Core runtime is already present."
  ${Else}
    Call InstallAspNetCoreRuntime
  ${EndIf}

  ${If} $PrerequisiteRebootRequired == 1
    SetRebootFlag true
  ${EndIf}
  !endif

  System::Call 'kernel32::GetCurrentProcessId()i.r0'
  StrCpy $StagingDirectory "$INSTDIR.staging-$0"
  IfFileExists "$StagingDirectory\*" 0 install_staging_ready
  MessageBox MB_ICONSTOP "Voltura Air setup found an unexpected staging directory and will not overwrite it."
  Abort "Installer staging directory already exists."
install_staging_ready:
  SetOutPath "$StagingDirectory"
  File /r "${PUBLISH_DIR}\*.*"
  WriteUninstaller "$StagingDirectory\Uninstall.exe"
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action Verify -InstallDirectory "$INSTDIR" -StagingDirectory "$StagingDirectory"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air setup could not verify the complete staged payload."
    Abort "Installer payload verification failed."
  ${EndIf}

  Call PromptCloseRunningApp
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action Promote -InstallDirectory "$INSTDIR" -StagingDirectory "$StagingDirectory" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air setup could not promote the staged installation. Its verified recovery state was retained."
    Abort "Installer promotion failed."
  ${EndIf}
  nsExec::ExecToStack '"$INSTDIR\${EXE_NAME}" --installer-health-check --isolated-test-mode'
  Pop $0
  Pop $1
  ${If} $0 != 0
    nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action Rollback -InstallDirectory "$INSTDIR" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
    MessageBox MB_ICONSTOP "The updated host failed its isolated health check. Setup restored the verified previous installation."
    Abort "Installed host health check failed."
  ${EndIf}

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
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 0
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 0
  WriteRegStr HKCU "Software\Classes\voltura-air" "" "URL:Voltura Air custom screen"
  WriteRegStr HKCU "Software\Classes\voltura-air" "URL Protocol" ""
  WriteRegStr HKCU "Software\Classes\voltura-air\DefaultIcon" "" "$INSTDIR\${EXE_NAME},0"
  WriteRegStr HKCU "Software\Classes\voltura-air\shell\open\command" "" "$\"$INSTDIR\${EXE_NAME}$\" $\"%1$\""
SectionEnd

Section /o "Phone Webcam" SEC_PHONE_WEBCAM
  DetailPrint "Preparing the optional protected Phone Webcam component..."
  StrCpy $WebcamRemovedForUpdate 0
  StrCpy $WebcamRollbackHelper "$PLUGINSDIR\VolturaAir.WebcamSetup.Rollback.exe"
  IfFileExists "${WEBCAM_PROTECTED_SETUP}" 0 phone_webcam_install_packaged
  ClearErrors
  CopyFiles /SILENT "${WEBCAM_PROTECTED_SETUP}" "$WebcamRollbackHelper"
  IfErrors 0 phone_webcam_check_existing
  Call RollbackPromotedInstall
  Pop $2
  MessageBox MB_ICONSTOP "Voltura Air could not preserve the installed Phone Webcam component before maintenance."
  Abort "Phone Webcam rollback helper could not be prepared."
phone_webcam_check_existing:
  nsExec::ExecToStack '"${WEBCAM_PROTECTED_SETUP}" cleanup-required'
  Pop $0
  Pop $1
  ${If} $0 == 0
    nsExec::ExecToStack '"${WEBCAM_PROTECTED_SETUP}" remove'
    Pop $0
    Pop $1
    ${If} $0 != 0
      Call RollbackPromotedInstall
      Pop $2
      MessageBox MB_ICONSTOP "Voltura Air could not safely remove the existing Phone Webcam component. Approve the administrator request and run setup again."
      Abort "Phone Webcam maintenance removal did not complete."
    ${EndIf}
    StrCpy $WebcamRemovedForUpdate 1
  ${ElseIf} $0 != 1
    Call RollbackPromotedInstall
    Pop $2
    MessageBox MB_ICONSTOP "Voltura Air could not verify the existing Phone Webcam component."
    Abort "Phone Webcam component state is unavailable."
  ${EndIf}
phone_webcam_install_packaged:
  SetOutPath "$PLUGINSDIR"
  File /oname=VolturaAir.WebcamSetup.exe "${PUBLISH_DIR}\${WEBCAM_SETUP}"
  nsExec::ExecToStack '"$PLUGINSDIR\VolturaAir.WebcamSetup.exe" install'
  Pop $0
  Pop $1
  ${If} $0 != 0
    ${If} $WebcamRemovedForUpdate == 1
      nsExec::ExecToStack '"$WebcamRollbackHelper" install'
      Pop $2
      Pop $3
      ${If} $2 != 0
        MessageBox MB_ICONSTOP "Phone Webcam installation and restoration did not complete. Setup retained its recovery journal; approve the administrator request and run setup again."
        Abort "Phone Webcam component restoration did not complete."
      ${EndIf}
    ${EndIf}
    Call RollbackPromotedInstall
    Pop $2
    ${If} $2 != 0
      MessageBox MB_ICONSTOP "Phone Webcam installation did not complete and the previous app could not be restored. Setup retained its recovery journal; run setup again."
      Abort "Phone Webcam installation and app rollback did not complete."
    ${EndIf}
    MessageBox MB_ICONSTOP "Phone Webcam installation did not complete. The previous app and component were restored. Approve the administrator request and run setup again."
    Abort "Phone Webcam component installation failed."
  ${EndIf}
SectionEnd

Section -FinalizeInstall
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action Commit -InstallDirectory "$INSTDIR" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air was installed, but setup could not finish transaction cleanup. Run setup again to recover it."
    Abort "Installer transaction cleanup failed."
  ${EndIf}
SectionEnd

Function .onInstSuccess
  IfRebootFlag 0 success_without_reboot
  SetErrorLevel 3010
  Return

success_without_reboot:
  SetErrorLevel 0
FunctionEnd

Section "Uninstall"
  Call un.PromptCloseRunningApp
  Call un.RemovePhoneWebcam

  IfFileExists "${RECOVERY_UNINSTALLER}" uninstall_recovery_ready 0
  CreateDirectory "$LOCALAPPDATA\Voltura Air"
  CopyFiles /SILENT "$EXEPATH" "${RECOVERY_UNINSTALLER}"
  IfErrors 0 uninstall_recovery_ready
  MessageBox MB_ICONSTOP "Voltura Air could not prepare its uninstall recovery entry."
  Abort "The recovery uninstaller could not be created."
uninstall_recovery_ready:
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" "$\"${RECOVERY_UNINSTALLER}$\""
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" "$\"${RECOVERY_UNINSTALLER}$\" /S"

  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action StageRemoval -InstallDirectory "$INSTDIR" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air could not stage its verified installation for removal."
    Abort "The installation directory could not be staged for removal."
  ${EndIf}
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action CompleteRemoval -InstallDirectory "$INSTDIR" -JournalPath "${INSTALL_TRANSACTION_JOURNAL}"'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air cleanup did not finish. The uninstall entry and recovery journal were retained; run uninstall again."
    Abort "The owned removal directory could not be deleted."
  ${EndIf}

  Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\${APP_NAME}\Uninstall ${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\${APP_NAME}"

  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  DeleteRegKey HKCU "Software\Classes\voltura-air"
  DeleteRegValue HKCU "${RUN_KEY}" "${APP_NAME}"
  Delete "${RECOVERY_UNINSTALLER}"

SectionEnd

Function un.RemovePhoneWebcam
  IfFileExists "${WEBCAM_PROTECTED_SETUP}" phone_webcam_helper_available phone_webcam_done

phone_webcam_helper_available:
  nsExec::ExecToStack '"${WEBCAM_PROTECTED_SETUP}" cleanup-required'
  Pop $0
  Pop $1
  ${If} $0 == 1
    Goto phone_webcam_done
  ${ElseIf} $0 != 0
    MessageBox MB_ICONSTOP "Voltura Air could not determine whether Phone webcam cleanup is required. Run uninstall again."
    Abort "Phone webcam cleanup state is unavailable."
  ${EndIf}
  DetailPrint "Removing Voltura Air Webcam..."
  nsExec::ExecToStack '"${WEBCAM_PROTECTED_SETUP}" remove'
  Pop $0
  Pop $1
  ${If} $0 != 0
    DetailPrint "Phone webcam removal retained its recoverable installation (exit code $0)."
    MessageBox MB_ICONSTOP "Voltura Air could not safely remove Voltura Air Webcam. Close apps using the camera, approve the administrator request, and run uninstall again."
    Abort "Phone webcam removal did not complete."
  ${EndIf}

phone_webcam_done:
FunctionEnd

!ifdef FRAMEWORK_DEPENDENT
Function TestWindowsDesktopRuntime
  DetailPrint "Checking for the .NET 10 Windows Desktop runtime..."
  StrCpy $2 '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$$d=$\'$PROGRAMFILES64\dotnet\dotnet.exe$\';if(-not(Test-Path -LiteralPath $$d -PathType Leaf)){exit 1};$$r=& $$d --list-runtimes;if($$LASTEXITCODE-ne 0){exit 1};if($$r-match $\'^Microsoft\.WindowsDesktop\.App 10\.0\.$\'){exit 0};exit 1"'
  !insertmacro ExecCheckedToStack $2 windows_desktop_detection_too_long
  Pop $0
  Pop $1
  Push $0
  Return

windows_desktop_detection_too_long:
  DetailPrint "Controlled failure: the Windows Desktop detection command exceeded the NSIS command capacity."
  MessageBox MB_ICONSTOP "Voltura Air setup encountered an internal error while checking the .NET 10 Windows Desktop runtime."
  Abort "The Windows Desktop runtime command was too long."
FunctionEnd

Function TestAspNetCoreRuntime
  DetailPrint "Checking for the .NET 10 ASP.NET Core runtime..."
  StrCpy $2 '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$$d=$\'$PROGRAMFILES64\dotnet\dotnet.exe$\';if(-not(Test-Path -LiteralPath $$d -PathType Leaf)){exit 1};$$r=& $$d --list-runtimes;if($$LASTEXITCODE-ne 0){exit 1};if($$r-match $\'^Microsoft\.AspNetCore\.App 10\.0\.$\'){exit 0};exit 1"'
  !insertmacro ExecCheckedToStack $2 aspnet_core_detection_too_long
  Pop $0
  Pop $1
  Push $0
  Return

aspnet_core_detection_too_long:
  DetailPrint "Controlled failure: the ASP.NET Core detection command exceeded the NSIS command capacity."
  MessageBox MB_ICONSTOP "Voltura Air setup encountered an internal error while checking the .NET 10 ASP.NET Core runtime."
  Abort "The ASP.NET Core runtime command was too long."
FunctionEnd

Function InstallWindowsDesktopRuntime
  DetailPrint "Downloading the .NET 10 Windows Desktop runtime..."
  StrCpy $2 '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$$ErrorActionPreference=$\'Stop$\';$$ProgressPreference=$\'SilentlyContinue$\';$$p=$\'$PLUGINSDIR\VolturaAir-WindowsDesktop.exe$\';try{Invoke-WebRequest -UseBasicParsing -TimeoutSec 300 -Uri $\'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe$\' -OutFile $$p;if((Get-Item -LiteralPath $$p).Length-le 0){exit 12};exit 0}catch{exit 11}"'
  !insertmacro ExecCheckedToStack $2 windows_desktop_internal_failure
  Pop $0
  Pop $1
  ${If} $0 != 0
    Call CleanupWindowsDesktopRuntime
    DetailPrint "Controlled failure: the .NET 10 Windows Desktop runtime download failed or was empty."
    MessageBox MB_ICONSTOP "Voltura Air could not download the required .NET 10 Windows Desktop runtime. Check the internet connection and try again."
    Abort "The Windows Desktop runtime download failed."
  ${EndIf}

  DetailPrint "Verifying the .NET 10 Windows Desktop runtime signature and Microsoft signer..."
  StrCpy $2 '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$$s=Get-AuthenticodeSignature -LiteralPath $\'$PLUGINSDIR\VolturaAir-WindowsDesktop.exe$\';if($$s.Status-ne [System.Management.Automation.SignatureStatus]::Valid){exit 21};if($$null-eq $$s.SignerCertificate){exit 22};$$n=$$s.SignerCertificate.SubjectName.Name;if($$n-notmatch $\'(?:^|,\s*)O=Microsoft Corporation(?:,|$$)$\'){exit 23};exit 0"'
  !insertmacro ExecCheckedToStack $2 windows_desktop_internal_failure
  Pop $0
  Pop $1
  ${If} $0 != 0
    StrCpy $4 $0
    Call CleanupWindowsDesktopRuntime
    ${If} $4 == 21
      DetailPrint "Controlled failure: the .NET 10 Windows Desktop runtime signature was not valid."
      MessageBox MB_ICONSTOP "Voltura Air rejected the downloaded .NET 10 Windows Desktop runtime because its digital signature was not valid."
    ${ElseIf} $4 == 22
      DetailPrint "Controlled failure: the .NET 10 Windows Desktop runtime had no signer certificate."
      MessageBox MB_ICONSTOP "Voltura Air rejected the downloaded .NET 10 Windows Desktop runtime because it had no signer certificate."
    ${Else}
      DetailPrint "Controlled failure: the .NET 10 Windows Desktop runtime signer was not Microsoft Corporation."
      MessageBox MB_ICONSTOP "Voltura Air rejected the downloaded .NET 10 Windows Desktop runtime because the authenticated publisher was not Microsoft Corporation."
    ${EndIf}
    Abort "The Windows Desktop runtime verification failed."
  ${EndIf}

  DetailPrint "Requesting elevation for the .NET 10 Windows Desktop runtime..."
  DetailPrint "Installing the .NET 10 Windows Desktop runtime..."
  SetDetailsPrint listonly
  DetailPrint "The Microsoft runtime installer can take several minutes."
  DetailPrint "Voltura Air setup continues automatically when it finishes."
  SetDetailsPrint both
  !insertmacro ExecElevatedAndWait "$PLUGINSDIR\VolturaAir-WindowsDesktop.exe" "/install /quiet /norestart" $0
  StrCpy $4 $0
  Call CleanupWindowsDesktopRuntime
  StrCpy $0 $4

  ${If} $0 == 0
    DetailPrint "Validating the .NET 10 Windows Desktop runtime..."
    Call TestWindowsDesktopRuntime
    Pop $0
    ${If} $0 != 0
      DetailPrint "Controlled failure: the .NET 10 Windows Desktop runtime was absent after a successful child installer result."
      MessageBox MB_ICONSTOP "The .NET 10 Windows Desktop runtime was not available after its installer reported success."
      Abort "The Windows Desktop runtime validation failed."
    ${EndIf}
    Return
  ${EndIf}

  ${If} $0 == 3010
    DetailPrint "The .NET 10 Windows Desktop runtime requires a restart."
    StrCpy $PrerequisiteRebootRequired 1
    SetRebootFlag true
    DetailPrint "Validating the .NET 10 Windows Desktop runtime..."
    Call TestWindowsDesktopRuntime
    Pop $0
    ${If} $0 != 0
      DetailPrint "The .NET 10 Windows Desktop runtime is provisionally complete pending restart."
    ${EndIf}
    Return
  ${EndIf}

  ${If} $0 == 51001
    DetailPrint "Controlled failure: elevation was denied for the .NET 10 Windows Desktop runtime."
    MessageBox MB_ICONSTOP "Voltura Air could not install the .NET 10 Windows Desktop runtime because administrator approval was denied."
  ${ElseIf} $0 == 51002
    DetailPrint "Controlled failure: the .NET 10 Windows Desktop runtime process could not be started."
    MessageBox MB_ICONSTOP "Voltura Air could not start the .NET 10 Windows Desktop runtime installer."
  ${Else}
    DetailPrint "Controlled failure: the .NET 10 Windows Desktop runtime installer returned exit code $0."
    MessageBox MB_ICONSTOP "The .NET 10 Windows Desktop runtime installer failed with exit code $0."
  ${EndIf}
  Abort "The Windows Desktop runtime installation failed."

windows_desktop_internal_failure:
  Call CleanupWindowsDesktopRuntime
  DetailPrint "Controlled failure: a Windows Desktop prerequisite command exceeded the NSIS command capacity."
  MessageBox MB_ICONSTOP "Voltura Air setup encountered an internal error while preparing the .NET 10 Windows Desktop runtime."
  Abort "The Windows Desktop runtime command was too long."
FunctionEnd

Function InstallAspNetCoreRuntime
  DetailPrint "Downloading the .NET 10 ASP.NET Core runtime..."
  StrCpy $2 '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$$ErrorActionPreference=$\'Stop$\';$$ProgressPreference=$\'SilentlyContinue$\';$$p=$\'$PLUGINSDIR\VolturaAir-AspNetCore.exe$\';try{Invoke-WebRequest -UseBasicParsing -TimeoutSec 300 -Uri $\'https://aka.ms/dotnet/10.0/aspnetcore-runtime-win-x64.exe$\' -OutFile $$p;if((Get-Item -LiteralPath $$p).Length-le 0){exit 12};exit 0}catch{exit 11}"'
  !insertmacro ExecCheckedToStack $2 aspnet_core_internal_failure
  Pop $0
  Pop $1
  ${If} $0 != 0
    Call CleanupAspNetCoreRuntime
    DetailPrint "Controlled failure: the .NET 10 ASP.NET Core runtime download failed or was empty."
    MessageBox MB_ICONSTOP "Voltura Air could not download the required .NET 10 ASP.NET Core runtime. Check the internet connection and try again."
    Abort "The ASP.NET Core runtime download failed."
  ${EndIf}

  DetailPrint "Verifying the .NET 10 ASP.NET Core runtime signature and Microsoft signer..."
  StrCpy $2 '"$WINDIR\Sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$$s=Get-AuthenticodeSignature -LiteralPath $\'$PLUGINSDIR\VolturaAir-AspNetCore.exe$\';if($$s.Status-ne [System.Management.Automation.SignatureStatus]::Valid){exit 21};if($$null-eq $$s.SignerCertificate){exit 22};$$n=$$s.SignerCertificate.SubjectName.Name;if($$n-notmatch $\'(?:^|,\s*)O=Microsoft Corporation(?:,|$$)$\'){exit 23};exit 0"'
  !insertmacro ExecCheckedToStack $2 aspnet_core_internal_failure
  Pop $0
  Pop $1
  ${If} $0 != 0
    StrCpy $4 $0
    Call CleanupAspNetCoreRuntime
    ${If} $4 == 21
      DetailPrint "Controlled failure: the .NET 10 ASP.NET Core runtime signature was not valid."
      MessageBox MB_ICONSTOP "Voltura Air rejected the downloaded .NET 10 ASP.NET Core runtime because its digital signature was not valid."
    ${ElseIf} $4 == 22
      DetailPrint "Controlled failure: the .NET 10 ASP.NET Core runtime had no signer certificate."
      MessageBox MB_ICONSTOP "Voltura Air rejected the downloaded .NET 10 ASP.NET Core runtime because it had no signer certificate."
    ${Else}
      DetailPrint "Controlled failure: the .NET 10 ASP.NET Core runtime signer was not Microsoft Corporation."
      MessageBox MB_ICONSTOP "Voltura Air rejected the downloaded .NET 10 ASP.NET Core runtime because the authenticated publisher was not Microsoft Corporation."
    ${EndIf}
    Abort "The ASP.NET Core runtime verification failed."
  ${EndIf}

  DetailPrint "Requesting elevation for the .NET 10 ASP.NET Core runtime..."
  DetailPrint "Installing the .NET 10 ASP.NET Core runtime..."
  SetDetailsPrint listonly
  DetailPrint "The Microsoft runtime installer can take several minutes."
  DetailPrint "Voltura Air setup continues automatically when it finishes."
  SetDetailsPrint both
  !insertmacro ExecElevatedAndWait "$PLUGINSDIR\VolturaAir-AspNetCore.exe" "/install /quiet /norestart" $0
  StrCpy $4 $0
  Call CleanupAspNetCoreRuntime
  StrCpy $0 $4

  ${If} $0 == 0
    DetailPrint "Validating the .NET 10 ASP.NET Core runtime..."
    Call TestAspNetCoreRuntime
    Pop $0
    ${If} $0 != 0
      DetailPrint "Controlled failure: the .NET 10 ASP.NET Core runtime was absent after a successful child installer result."
      MessageBox MB_ICONSTOP "The .NET 10 ASP.NET Core runtime was not available after its installer reported success."
      Abort "The ASP.NET Core runtime validation failed."
    ${EndIf}
    Return
  ${EndIf}

  ${If} $0 == 3010
    DetailPrint "The .NET 10 ASP.NET Core runtime requires a restart."
    StrCpy $PrerequisiteRebootRequired 1
    SetRebootFlag true
    DetailPrint "Validating the .NET 10 ASP.NET Core runtime..."
    Call TestAspNetCoreRuntime
    Pop $0
    ${If} $0 != 0
      DetailPrint "The .NET 10 ASP.NET Core runtime is provisionally complete pending restart."
    ${EndIf}
    Return
  ${EndIf}

  ${If} $0 == 51001
    DetailPrint "Controlled failure: elevation was denied for the .NET 10 ASP.NET Core runtime."
    MessageBox MB_ICONSTOP "Voltura Air could not install the .NET 10 ASP.NET Core runtime because administrator approval was denied."
  ${ElseIf} $0 == 51002
    DetailPrint "Controlled failure: the .NET 10 ASP.NET Core runtime process could not be started."
    MessageBox MB_ICONSTOP "Voltura Air could not start the .NET 10 ASP.NET Core runtime installer."
  ${Else}
    DetailPrint "Controlled failure: the .NET 10 ASP.NET Core runtime installer returned exit code $0."
    MessageBox MB_ICONSTOP "The .NET 10 ASP.NET Core runtime installer failed with exit code $0."
  ${EndIf}
  Abort "The ASP.NET Core runtime installation failed."

aspnet_core_internal_failure:
  Call CleanupAspNetCoreRuntime
  DetailPrint "Controlled failure: an ASP.NET Core prerequisite command exceeded the NSIS command capacity."
  MessageBox MB_ICONSTOP "Voltura Air setup encountered an internal error while preparing the .NET 10 ASP.NET Core runtime."
  Abort "The ASP.NET Core runtime command was too long."
FunctionEnd

Function CleanupWindowsDesktopRuntime
  DetailPrint "Cleaning up the .NET 10 Windows Desktop runtime installer."
  Delete "$PLUGINSDIR\VolturaAir-WindowsDesktop.exe"
FunctionEnd

Function CleanupAspNetCoreRuntime
  DetailPrint "Cleaning up the .NET 10 ASP.NET Core runtime installer."
  Delete "$PLUGINSDIR\VolturaAir-AspNetCore.exe"
FunctionEnd
!endif

Function PromptCloseRunningApp
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action HostRunning -InstallDirectory "$INSTDIR"'
  Pop $0
  Pop $1

  ${If} $0 != 0
    Return
  ${EndIf}

  MessageBox MB_ICONEXCLAMATION|MB_OKCANCEL "${APP_NAME} is currently running. Setup needs to close it before continuing." IDOK install_close IDCANCEL install_cancel

install_close:
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action StopHost -InstallDirectory "$INSTDIR"'
  Pop $0
  Pop $1

  ${If} $0 != 0
    MessageBox MB_ICONSTOP|MB_RETRYCANCEL "${APP_NAME} is still running. Close it manually, then retry." IDRETRY install_close IDCANCEL install_cancel
  ${EndIf}

  Return

install_cancel:
  Abort "Setup was canceled because ${APP_NAME} is still running."
FunctionEnd

Function un.PromptCloseRunningApp
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action HostRunning -InstallDirectory "$INSTDIR"'
  Pop $0
  Pop $1

  ${If} $0 != 0
    Return
  ${EndIf}

  MessageBox MB_ICONEXCLAMATION|MB_OKCANCEL "${APP_NAME} is currently running. Uninstall needs to close it before continuing." IDOK uninstall_close IDCANCEL uninstall_cancel

uninstall_close:
  nsExec::ExecToStack '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\${INSTALL_TRANSACTION_SCRIPT}" -Action StopHost -InstallDirectory "$INSTDIR"'
  Pop $0
  Pop $1

  ${If} $0 != 0
    MessageBox MB_ICONSTOP|MB_RETRYCANCEL "${APP_NAME} is still running. Close it manually, then retry." IDRETRY uninstall_close IDCANCEL uninstall_cancel
  ${EndIf}

  Return

uninstall_cancel:
  Abort "Uninstall was canceled because ${APP_NAME} is still running."
FunctionEnd
