Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = Chr(34) & scriptDir & "\ScreenCaptureTool_CN.exe" & Chr(34)
shell.Run cmd, 0, False
