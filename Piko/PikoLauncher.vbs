Set wshShell = CreateObject("WScript.Shell")
Set wshEnv = wshShell.Environment("PROCESS")
wshEnv("DOTNET_ROOT") = "C:\Users\axioo\.dotnet"
wshShell.CurrentDirectory = "D:\Piko_Desktop_Companion_v0.2\PikoStarter\Piko\bin\Release\net8.0-windows"
wshShell.Run """D:\Piko_Desktop_Companion_v0.2\PikoStarter\Piko\bin\Release\net8.0-windows\Piko.exe"" --expand", 0, False
