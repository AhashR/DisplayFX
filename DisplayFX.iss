; Inno Setup Script for DisplayFX
; Created for DisplayFX Windows Installer

#define MyAppName "DisplayFX"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "AhashR"
#define MyAppURL "https://github.com/AhashR/DisplayFX"
#define MyAppExeName "DisplayFX.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{67FE0A57-27F9-4945-9A94-CEF4E6A12D4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=WindowsDisplayAPI-master\LICENSE
OutputDir=installer_output
OutputBaseFilename=DisplayFX_Setup
SetupIconFile=DisplayFX\Resources\desktop.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Automatically start DisplayFX on Windows startup"; GroupDescription: "Startup options:"

[Files]
Source: "app_files\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Resources\desktop.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
