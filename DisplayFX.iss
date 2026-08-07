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
AppVerName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
DirExistsWarning=no
AppendDefaultDirName=no
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

[Files]
Source: "app_files\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Resources\desktop.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function SetForegroundWindow(hWnd: HWND): BOOL;
  external 'SetForegroundWindow@user32.dll stdcall';

function SetWindowPos(hWnd: HWND; hWndInsertAfter: HWND; X, Y, cx, cy: UINT; uFlags: UINT): BOOL;
  external 'SetWindowPos@user32.dll stdcall';

const
  HWND_TOPMOST = -1;
  HWND_NOTOPMOST = -2;
  SWP_NOSIZE = $0001;
  SWP_NOMOVE = $0002;
  SWP_SHOWWINDOW = $0040;

procedure ForceForeground();
begin
  BringToFrontAndRestore;
  if WizardForm <> nil then
  begin
    WizardForm.BringToFront;
    SetForegroundWindow(WizardForm.Handle);
    SetWindowPos(WizardForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_SHOWWINDOW);
    SetWindowPos(WizardForm.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_SHOWWINDOW);
  end;
end;

procedure CustomDirBrowseButtonClick(Sender: TObject);
var
  Dir: String;
begin
  Dir := WizardForm.DirEdit.Text;
  if BrowseForFolder('Select a folder in the list below, then click OK.', Dir, True) then
  begin
    WizardForm.DirEdit.Text := Dir;
  end;
end;

function IsDirEmpty(const DirName: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := True;
  if FindFirst(DirName + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Result := False;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  SelectedDir: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    SelectedDir := WizardDirValue;
    if DirExists(SelectedDir) and not IsDirEmpty(SelectedDir) then
    begin
      if SuppressibleMsgBox(
        'There are already files in this folder:' + #13#10#13#10 +
        SelectedDir + #13#10#13#10 +
        'Are you sure you want to install here anyway?',
        mbConfirmation, MB_YESNO, IDNO) = IDNO then
      begin
        Result := False;
      end;
    end;
  end;
end;

procedure InitializeWizard();
begin
  ForceForeground();
  WizardForm.DirBrowseButton.OnClick := @CustomDirBrowseButtonClick;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
  begin
    ForceForeground();
  end;
end;
