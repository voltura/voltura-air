Option Explicit

' WScript has no console window.  It starts the updater hidden so the scheduled
' task cannot flash a terminal or display a Microsoft Store window.
Dim fileSystem
Dim shell
Dim scriptDirectory
Dim updaterScript
Dim command
Dim exitCode

Set fileSystem = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

scriptDirectory = fileSystem.GetParentFolderName(WScript.ScriptFullName)
updaterScript = fileSystem.BuildPath(scriptDirectory, "update-chatgpt-codex.ps1")
powerShell = shell.ExpandEnvironmentStrings("%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe")
command = """" & powerShell & """ -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & updaterScript & """ -Scheduled"

exitCode = shell.Run(command, 0, True)
WScript.Quit exitCode
