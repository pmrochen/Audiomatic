[Setup]
AppName=Audiomatic
AppVersion=0.0.2
AppPublisher=OhMyCode
DefaultDirName={localappdata}\Programs\Audiomatic
DefaultGroupName=Audiomatic
OutputDir=.\Installer
OutputBaseFilename=Audiomatic-Setup-x64
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\Audiomatic.exe
WizardStyle=modern
SetupIconFile=Audiomatic\app.ico

[Files]
Source: "Audiomatic\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Audiomatic"; Filename: "{app}\Audiomatic.exe"
Name: "{autodesktop}\Audiomatic"; Filename: "{app}\Audiomatic.exe"; Tasks: desktopicon
Name: "{userstartup}\Audiomatic"; Filename: "{app}\Audiomatic.exe"; Tasks: startupicon

[Tasks]
Name: "desktopicon"; Description: "Raccourci sur le Bureau"; GroupDescription: "Raccourcis:"; Flags: checkedonce
Name: "startupicon"; Description: "Lancer au démarrage de Windows"; GroupDescription: "Raccourcis:"; Flags: unchecked

[Run]
Filename: "{app}\Audiomatic.exe"; Description: "Lancer Audiomatic"; Flags: nowait postinstall skipifsilent
