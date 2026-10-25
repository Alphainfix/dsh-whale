Set shell = CreateObject("WScript.Shell")
Set fs = CreateObject("Scripting.FileSystemObject")
root = fs.GetParentFolderName(WScript.ScriptFullName)
exe = root & "\DSHWhale-Vibe.exe"
If Not fs.FileExists(exe) Then exe = root & "\build\DSHWhale-Vibe.exe"
shell.Run """" & exe & """ --open", 0, False
