Option Explicit
Dim files, shell, root, version, reader, executable
Set files = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
root = files.GetParentFolderName(WScript.ScriptFullName)
If Not files.FileExists(root & "\current-version.txt") Then WScript.Quit 1
Set reader = files.OpenTextFile(root & "\current-version.txt", 1)
version = Trim(reader.ReadAll)
reader.Close
If InStr(version, "\") > 0 Or InStr(version, "/") > 0 Or InStr(version, "..") > 0 Then WScript.Quit 2
executable = root & "\dist\v" & version & "\TrifoldDesk.exe"
If Not files.FileExists(executable) Then WScript.Quit 3
shell.Run Chr(34) & executable & Chr(34), 0, False
