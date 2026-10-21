#ifndef AppVersion
  #define AppVersion "2.0.1"
#endif
#ifndef ReleaseRoot
  #define ReleaseRoot "D:\Agant\Github_Releases\Gitstar"
#endif
[Setup]
AppId={{84F12204-67B1-41E8-956B-63841E986B89}
AppName=Gitstar
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
AppPublisher=Gitstar
LicenseFile=..\..\LICENSE
DefaultDirName={localappdata}\Programs\Gitstar
DefaultGroupName=Gitstar
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
OutputDir={#ReleaseRoot}
OutputBaseFilename=Gitstar-setup
SetupIconFile=..\Gitstar.Desktop\Assets\Gitstar.ico
UninstallDisplayIcon={app}\Gitstar.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
[Files]
Source: "{#ReleaseRoot}\Gitstar.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseRoot}\使用说明.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ReleaseRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
[InstallDelete]
Type: files; Name: "{app}\Gitstar.dll"
Type: files; Name: "{app}\Gitstar.Core.dll"
Type: files; Name: "{app}\Gitstar.deps.json"
Type: files; Name: "{app}\Gitstar.runtimeconfig.json"
Type: files; Name: "{app}\gitstar-askpass.cmd"
Type: files; Name: "{app}\Gitstar.pdb"
Type: files; Name: "{app}\Gitstar.Core.pdb"
[Icons]
Name: "{userprograms}\Gitstar"; Filename: "{app}\Gitstar.exe"
Name: "{userdesktop}\Gitstar"; Filename: "{app}\Gitstar.exe"; Tasks: desktopicon
[Tasks]
Name: desktopicon; Description: "创建桌面快捷方式"; Flags: unchecked
[Run]
Filename: "{app}\Gitstar.exe"; Description: "启动 Gitstar"; Flags: nowait postinstall skipifsilent
[Code]
function HasDesktop10: Boolean;
var Versions: TArrayOfString; I: Integer; Root: String; Dirs: TFindRec;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
    for I := 0 to GetArrayLength(Versions)-1 do
      if Copy(Versions[I],1,3) = '10.' then Result := True;
  Root := ExpandConstant('{pf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*');
  if FindFirst(Root, Dirs) then begin
    try repeat if (Dirs.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then Result := True; until not FindNext(Dirs); finally FindClose(Dirs); end;
  end;
end;
function InitializeSetup: Boolean;
begin
  Result := HasDesktop10;
  if not Result then MsgBox('Gitstar requires the x64 .NET Desktop Runtime 10.x.' + #13#10 + 'Install it from https://dotnet.microsoft.com/download/dotnet/10.0 then run setup again.' + #13#10 + 'The runtime is not bundled or downloaded automatically.', mbError, MB_OK);
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Value: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Gitstar', Value) then
      if Pos(ExpandConstant('{app}'),Value)>0 then RegDeleteValue(HKCU,'Software\Microsoft\Windows\CurrentVersion\Run','Gitstar');
end;
