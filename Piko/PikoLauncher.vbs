' Piko Desktop Companion — Portable Silent Launcher
Set fso = CreateObject("Scripting.FileSystemObject")
Set wshShell = CreateObject("WScript.Shell")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
userProfile = wshShell.ExpandEnvironmentStrings("%USERPROFILE%")

' Ensure DOTNET_ROOT is set if user has local dotnet installed
Set wshEnv = wshShell.Environment("PROCESS")
If fso.FolderExists(userProfile & "\.dotnet") Then
    wshEnv("DOTNET_ROOT") = userProfile & "\.dotnet"
End If

' Search for Piko executable (Release build or direct folder)
pikoExe = scriptDir & "\bin\Release\net8.0-windows\Piko.exe"
If Not fso.FileExists(pikoExe) Then
    pikoExe = scriptDir & "\Piko.exe"
End If

If fso.FileExists(pikoExe) Then
    wshShell.CurrentDirectory = fso.GetParentFolderName(pikoExe)
    wshShell.Run """" & pikoExe & """", 1, False
Else
    MsgBox "Piko.exe not found! Please build the project or run dotnet build first.", 16, "Piko Launcher"
End If
