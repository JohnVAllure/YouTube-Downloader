[Setup]
AppId={{6933949E-2F58-4D08-B5D4-F705B3CE55A9}
AppName=YouTubeDownloader
AppVersion=1.1.6
AppVerName=YouTubeDownloader 1.1.6
DefaultDirName={autopf}\YouTubeDownloader
DefaultGroupName=YouTubeDownloader
UninstallDisplayIcon={app}\YouTubeDownloader.exe
OutputDir=dist
OutputBaseFilename=YouTubeDownloader-Setup-1.1.6
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Files]
Source:"bin\Release\net8.0-windows\win-x64\publish\*"; DestDir:"{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name:"{autoprograms}\YouTubeDownloader"; Filename:"{app}\YouTubeDownloader.exe"
Name:"{commondesktop}\YouTubeDownloader"; Filename:"{app}\YouTubeDownloader.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\YouTubeDownloader"; Check: ShouldRemoveAppData

[Code]
var
RemoveAppDataFlag: Boolean;

function InitializeUninstall(): Boolean;
var
Res: Integer;
begin
Result := True; // allow uninstall to continue
Res := MsgBox(
'Do you also want to remove application data (settings, tools, caches) in %AppData%\YouTubeDownloader?',
mbConfirmation, MB_YESNO);
RemoveAppDataFlag := (Res = IDYES);
end;
function ShouldRemoveAppData: Boolean;
begin
Result := RemoveAppDataFlag;
end;