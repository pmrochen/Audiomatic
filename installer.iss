[Setup]
AppName=Audiomatic
AppVersion=0.2.0
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
ShowLanguageDialog=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Files]
Source: "Audiomatic\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Audiomatic"; Filename: "{app}\Audiomatic.exe"
Name: "{autodesktop}\Audiomatic"; Filename: "{app}\Audiomatic.exe"; Tasks: desktopicon
Name: "{userstartup}\Audiomatic"; Filename: "{app}\Audiomatic.exe"; Tasks: startupicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startupicon"; Description: "{cm:AutoStartProgram,Audiomatic}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\Audiomatic.exe"; Description: "{cm:LaunchProgram,Audiomatic}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
english.AutoStartProgram=Start %1 with Windows
french.AutoStartProgram=Lancer %1 au démarrage de Windows
polish.AutoStartProgram=Uruchamiaj %1 wraz z systemem Windows
