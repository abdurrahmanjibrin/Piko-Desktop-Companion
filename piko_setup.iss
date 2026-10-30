; Inno Setup Script for Piko Desktop Companion
; Produces a standard Windows Installer (Piko-Setup.exe)

#define MyAppName "Piko Desktop Companion"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "Abdurrahman Jibrin"
#define MyAppURL "https://github.com/abdurrahmanjibrin/Piko-Desktop-Companion"
#define MyAppExeName "Piko.exe"

[Setup]
AppId={{5D0E39F5-8C7E-4E6D-9132-732B03CE64B4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=Piko\Piko.ico
WizardStyle=modern
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputDir=dist
OutputBaseFilename=Piko-Setup-v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "autostart"; Description: "Jalankan Piko otomatis saat Windows menyala (Start with Windows)"; GroupDescription: "Pilihan Lainnya (Options):"; Flags: unchecked

[Files]
; Main application files from release_bundle\Piko
Source: "release_bundle\Piko\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Optional Browser Extension & Documentation
Source: "release_bundle\PikoBrowserExtension\*"; DestDir: "{app}\PikoBrowserExtension"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "release_bundle\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "release_bundle\README.id.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
