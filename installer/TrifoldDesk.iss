#ifndef AppVersion
#define AppVersion "0.10.12"
#endif
[Setup]
AppId={{8B5A7B77-C1D1-4BE8-BA51-168433E51F26}
AppName=TrifoldDesk
AppVersion={#AppVersion}
AppPublisher=partjava
AppPublisherURL=https://github.com/partjava/TrifoldDesk
DefaultDirName={localappdata}\Programs\TrifoldDesk
DefaultGroupName=TrifoldDesk
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist\installers
OutputBaseFilename=TrifoldDesk-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\TrifoldDesk.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\dist\v{#AppVersion}\TrifoldDesk.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=no
[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: unchecked
Name: "startup"; Description: "开机登录后自动运行"
[Files]
Source: "..\dist\installer-app-v{#AppVersion}\*"; DestDir: "{app}\dist\v{#AppVersion}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\AutoStart.vbs"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\TrifoldDesk"; Filename: "{app}\dist\v{#AppVersion}\TrifoldDesk.exe"; Parameters: "--show"
Name: "{group}\卸载 TrifoldDesk"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TrifoldDesk"; Filename: "{app}\dist\v{#AppVersion}\TrifoldDesk.exe"; Parameters: "--show"; Tasks: desktopicon
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TrifoldDesk"; ValueData: """{sys}\wscript.exe"" ""{app}\AutoStart.vbs"""; Flags: uninsdeletevalue; Tasks: startup
[Run]
Filename: "{app}\dist\v{#AppVersion}\TrifoldDesk.exe"; Parameters: "--show"; Description: "启动 TrifoldDesk"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "{app}\dist\v{#AppVersion}\TrifoldDesk.exe"; Parameters: "--shutdown"; RunOnceId: "CloseTrifoldDesk"; Flags: runhidden waituntilterminated skipifdoesntexist
[UninstallDelete]
Type: files; Name: "{app}\current-version.txt"
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\current-version.txt'), '{#AppVersion}', False);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var ResultCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\dist\v{#AppVersion}\TrifoldDesk.exe')) then
  begin
    Exec(ExpandConstant('{app}\dist\v{#AppVersion}\TrifoldDesk.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
  end;
end;
