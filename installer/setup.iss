[Setup]
AppId={{2D61CB27-737E-4480-B74E-9341D9838F23}
AppName=米家 WinUI
AppVersion=0.20.0
DefaultDirName={localappdata}\Programs\MiHomeWinUI
DefaultGroupName=米家 WinUI
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename=MiHomeWinUI-0.20.0-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\MiHome.WinUI\Assets\AppIcon.ico
UninstallDisplayIcon={app}\MiHome.WinUI.exe
CloseApplications=yes

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Excludes: "diagnostics\*,*.pdb,*.pyc,*.pyo,runtime\Lib\site-packages\bin\*.exe"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\米家 WinUI"; Filename: "{app}\MiHome.WinUI.exe"; IconFilename: "{app}\Assets\AppIcon.ico"

[InstallDelete]
Type: files; Name: "{group}\MiHome WinUI.lnk"
Type: files; Name: "{app}\Assets\Devices\*.png"
Type: files; Name: "{app}\Assets\Devices\*.jpg"
Type: files; Name: "{app}\Assets\Devices\*.jpeg"
Type: files; Name: "{app}\Assets\Devices\index.json"

[Run]
Filename: "{app}\MiHome.WinUI.exe"; Description: "启动米家 WinUI"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Delete account data, settings and cache?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\MiHomeWinUI'), True, True, True);
end;
