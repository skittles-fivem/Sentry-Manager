; Sentry Six Manager for Windows: the one file people download and run,
; SentrySixManager-Setup-<version>.exe. Inno Setup 6 (pinned in
; windows\versions.json); windows\build.ps1 compiles it right after the zip,
; from the same stage folder, so both carry the same files.
;
; The Setup holds the release zip's contents (node\ ffmpeg\ bin\ app-ui\ app\
; and the scripts), unpacks them to its own temporary folder and runs that
; folder's install.ps1 hidden, as Install.cmd runs it from an unpacked zip:
; install.ps1 stays the one place that copies the program files to
; %ProgramFiles%\SentrySixManager, sets up the service or app mode, the
; permissions, the firewall rule, the Start menu entry and the start at
; sign-in. Inno adds only the entry in Settings > Apps (install.ps1 writes
; none, so there is exactly one) and its uninstaller, which runs
; uninstall.ps1 the same way. Running a newer Setup over an installed copy
; upgrades it in place (same AppId, always); the data stays.
;
; Command line (CI, scripts, a future updater):
;   /VERYSILENT /SUPPRESSMSGBOXES /NORESTART   no window, no questions
;   /MODE=service   a Windows service: always on, also with nobody signed in
;   /MODE=app       the app runs the server while its user is signed in
;                   (no /MODE: an installed copy keeps its mode; a first
;                   install is app mode, as install.ps1 does with no answer)
;   /LOG="file"     Inno's log, with every line install.ps1 printed
;   Exit code 0 when installed; 7 when install.ps1 failed (its last lines
;   are in the log and in %TEMP%\SentrySixManager-Setup.log).
; Uninstaller (Settings > Apps, or unins000.exe in
; %ProgramFiles%\SentrySixManager-Uninstall):
;   /VERYSILENT /SUPPRESSMSGBOXES   keeps the data (%ProgramData%\SentrySixManager)
;   /REMOVEDATA=1                   deletes the data too

#ifndef AppVersion
  #error Build this with windows\build.ps1: it passes /DAppVersion, /DAppNumVersion, /DPayloadDir, /DPayloadBytes and /DRepo
#endif
#ifndef AppNumVersion
  #error /DAppNumVersion=a.b.c.d is missing (windows\build.ps1 passes it)
#endif
#ifndef PayloadDir
  #error /DPayloadDir=<stage folder> is missing (windows\build.ps1 passes it)
#endif
#ifndef PayloadBytes
  #error /DPayloadBytes is missing (windows\build.ps1 passes it)
#endif
#ifndef Repo
  #error /DRepo=owner/name is missing (windows\build.ps1 passes it)
#endif

[Setup]
; Never change the AppId: it is how a newer Setup finds the installed copy
; (and install.ps1 / uninstall.ps1 find the Settings > Apps entry by it).
AppId={{37874499-7900-4DA9-8103-133AFC180A56}
AppName=Sentry Six Manager
AppVersion={#AppVersion}
AppVerName=Sentry Six Manager {#AppVersion}
AppPublisher=Sentry Six
AppPublisherURL=https://github.com/{#Repo}
AppSupportURL=https://github.com/{#Repo}/issues
AppUpdatesURL=https://github.com/{#Repo}/releases
VersionInfoVersion={#AppNumVersion}
VersionInfoProductName=Sentry Six Manager
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany=Sentry Six
VersionInfoDescription=Sentry Six Manager Setup
; install.ps1 always installs here; the folder is not a choice
DefaultDirName={commonpf64}\SentrySixManager
DisableDirPage=yes
UsePreviousAppDir=no
DirExistsWarning=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
; The uninstaller lives beside the program folder, not in it: install.ps1
; (and update.ps1's run of it) replaces that whole folder on every upgrade,
; which would take unins000.exe with it. Never change this either: a newer
; Setup looks for the previous uninstall log here.
UninstallFilesDir={commonpf64}\SentrySixManager-Uninstall
UninstallDisplayName=Sentry Six Manager
UninstallDisplayIcon={app}\app-ui\SentrySix.exe
UninstallDisplaySize={#PayloadBytes}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; install.ps1 asks the app to quit and waits for it; Inno replaces no files there
CloseApplications=no
RestartIfNeededByRun=no
SetupMutex=SentrySixManagerSetup,Global\SentrySixManagerSetup
SetupIconFile={#PayloadDir}\app-ui\SentrySix.ico
WizardSmallImageFile={#SourcePath}..\..\web\icon.png
WizardStyle=modern
ShowLanguageDialog=no
UsedUserAreasWarning=no
OutputDir={#SourcePath}..\..\dist
OutputBaseFilename=SentrySixManager-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
UninstallLogging=yes
; Code signing: not set up yet. Once there is a certificate (Azure Artifact
; Signing or another), build.ps1 passes a sign tool to ISCC, for example
;   /Ssigntool="signtool.exe sign /fd SHA256 /tr <timestamp url> /td SHA256 /d $qSentry Six Manager$q $f"
; and these two lines are uncommented, which signs the Setup and its uninstaller:
;SignTool=signtool
;SignedUninstaller=yes

[Messages]
WelcomeLabel2=This installs [name/ver]: the Sentry Studio viewer and converter for your Tesla dashcam footage, as a web app on this PC.%n%nIf it is installed already, it is upgraded in place and your settings, accounts and footage stay.
FinishedLabelNoIcons=Sentry Six Manager is installed. It is in the Start menu and starts in the tray when you sign in. The addresses below are what you need the first time (on this PC itself no setup code is needed).
FinishedLabel=Sentry Six Manager is installed. It is in the Start menu and starts in the tray when you sign in. The addresses below are what you need the first time (on this PC itself no setup code is needed).

[Files]
; (Small files first: solid compression unpacks in this order, so nothing
; after the payload has to wait for it twice.)
; A copy of uninstall.ps1 beside the uninstaller, for when the program
; folder is already gone; the one in the program folder is used when there.
Source: "{#PayloadDir}\uninstall.ps1"; DestDir: "{commonpf64}\SentrySixManager-Uninstall"; Flags: ignoreversion
Source: "{#SourcePath}setup-user.ps1"; DestDir: "{tmp}"; Flags: dontcopy
; The payload goes nowhere by itself: PrepareToInstall unpacks it to
; {tmp}\payload, laid out exactly as the zip, and install.ps1 copies it from
; there into the program folder.
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\payload"; Flags: dontcopy recursesubdirs createallsubdirs

[Run]
Filename: "{commonpf64}\SentrySixManager\app-ui\SentrySix.exe"; WorkingDir: "{commonpf64}\SentrySixManager\app-ui"; Description: "Open Sentry Six Manager"; Flags: postinstall nowait skipifsilent

[Code]
const
  ModeKey = 'SOFTWARE\SentrySixManager';
  ServiceKey = 'SYSTEM\CurrentControlSet\Services\SentrySixManager';
  TailMax = 12;

var
  ModePage: TInputOptionWizardPage;
  RunPage: TOutputMarqueeProgressWizardPage;
  SummaryMemo: TNewMemo;
  { the installed copy's mode: 'service', 'app' or '' (none installed) }
  PrevMode: String;
  { /MODE= on the command line, '' without one }
  CmdMode: String;
  { every line the script printed goes here as well as to Inno's log }
  OutputLog: String;
  { its last lines, for the error message }
  Tail: TArrayOfString;
  { install.ps1's closing lines: the addresses, the setup code, the fingerprint }
  Summary: String;
  SummaryState: Integer;
  ShowStatus: Boolean;
  InUninstall: Boolean;

{ 64-bit Windows PowerShell 5.1 (Setup itself is a 32-bit program) }
function PowerShellExe: String;
begin
  Result := ExpandConstant('{sysnative}\WindowsPowerShell\v1.0\powershell.exe');
end;

function InstalledMode: String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64, ModeKey, 'Mode', S) then begin
    S := Lowercase(Trim(S));
    if (S = 'service') or (S = 'app') then begin
      Result := S;
      Exit;
    end;
  end;
  { installs from before the two modes had the service and no record }
  if RegKeyExists(HKLM64, ServiceKey) then
    Result := 'service';
end;

function ChosenMode: String;
begin
  if WizardSilent then begin
    if CmdMode <> '' then
      Result := CmdMode
    else if PrevMode <> '' then
      Result := PrevMode
    else
      Result := 'app';
  end
  else if ModePage.SelectedValueIndex = 1 then
    Result := 'app'
  else
    Result := 'service';
end;

procedure AddTail(const Line: String);
var
  I, N: Integer;
begin
  N := GetArrayLength(Tail);
  if N < TailMax then begin
    SetArrayLength(Tail, N + 1);
    Tail[N] := Line;
  end
  else begin
    for I := 0 to TailMax - 2 do
      Tail[I] := Tail[I + 1];
    Tail[TailMax - 1] := Line;
  end;
end;

function TailText: String;
var
  I: Integer;
  L: String;
begin
  Result := '';
  for I := 0 to GetArrayLength(Tail) - 1 do begin
    L := Tail[I];
    if Length(L) > 160 then
      L := Copy(L, 1, 157) + '...';
    Result := Result + L + #13#10;
  end;
end;

{ Each line install.ps1 / uninstall.ps1 prints, as it prints it }
procedure OnOutputLine(const S: String; const Error, FirstLine: Boolean);
var
  Line: String;
begin
  if Error then
    Line := '(reading the output failed: ' + S + ')'
  else
    Line := S;
  Log('ps> ' + Line);
  SaveStringToFile(OutputLog, Line + #13#10, True);
  if Trim(Line) = '' then
    Exit;
  AddTail(Line);
  if ShowStatus then begin
    RunPage.SetText('Installing Sentry Six Manager...', Line);
    RunPage.Animate;
  end
  else if InUninstall then begin
    { (UninstallSilent only exists in the uninstaller: tested inside) }
    if not UninstallSilent then
      UninstallProgressForm.StatusLabel.Caption := Line;
  end;
  if SummaryState = 0 then begin
    if Pos('Sentry Six Manager is installed', Line) = 1 then
      SummaryState := 1;
  end;
  if SummaryState = 1 then begin
    if Pos('Mode:', Line) = 1 then
      SummaryState := 2
    else
      Summary := Summary + Line + #13#10;
  end;
end;

{ Runs one of the package's scripts in 64-bit Windows PowerShell, hidden;
  its output goes to OnOutputLine. }
function RunScript(const Script, Params: String; var ResultCode: Integer): Boolean;
begin
  SetArrayLength(Tail, 0);
  Log('Running ' + Script + ' ' + Params);
  Result := ExecAndLogOutput(PowerShellExe,
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Script + '" ' + Params,
    ExpandConstant('{win}'), SW_HIDE, ewWaitUntilTerminated, ResultCode, @OnOutputLine);
end;

{ DOMAIN\name of the person who started Setup, for install.ps1 -ForUser (in
  app mode the server runs as that person). Windows may have asked for
  another account's password to run Setup as administrator, so this is not
  necessarily the account Setup runs as now. '' when it cannot tell:
  install.ps1 then takes the account running it. }
function SetupUser: String;
var
  Output: TExecOutput;
  ResultCode, I: Integer;
  S, Stem: String;
begin
  Result := '';
  Stem := ChangeFileExt(ExtractFileName(ExpandConstant('{srcexe}')), '');
  try
    ExtractTemporaryFile('setup-user.ps1');
    if ExecAndCaptureOutput(PowerShellExe,
         '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\setup-user.ps1') + '" -SetupName "' + Stem + '"',
         ExpandConstant('{win}'), SW_HIDE, ewWaitUntilTerminated, ResultCode, Output) and (ResultCode = 0) then begin
      for I := 0 to GetArrayLength(Output.StdOut) - 1 do begin
        S := Trim(Output.StdOut[I]);
        if S <> '' then
          Result := S;
      end;
    end;
  except
    Log('Could not tell who started Setup: ' + GetExceptionMessage);
  end;
  if (Pos('"', Result) > 0) or (Pos('\', Result) = 0) or (Length(Result) > 256) then
    Result := '';
  Log('Setup was started by: ' + Result);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  CmdMode := Lowercase(Trim(ExpandConstant('{param:MODE|}')));
  if (CmdMode <> '') and (CmdMode <> 'service') and (CmdMode <> 'app') then begin
    Log('Unknown /MODE=' + CmdMode);
    SuppressibleMsgBox('Unknown /MODE=' + CmdMode + '. Use /MODE=service or /MODE=app.', mbCriticalError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  PrevMode := InstalledMode;
  Log('Installed mode: ''' + PrevMode + '''; /MODE: ''' + CmdMode + '''');
end;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpWelcome,
    'How should Sentry Six Manager run?',
    'You can change this later from the app''s tray menu.',
    'Choose one, then click Next.', True, False);
  ModePage.Add('Run in the background as a Windows service - always on, even when nobody is signed in (recommended for a dedicated PC)');
  ModePage.Add('Run as an app when I sign in - it converts while I am signed in and stops when I quit it');
  if CmdMode = 'app' then
    ModePage.SelectedValueIndex := 1
  else if CmdMode = 'service' then
    ModePage.SelectedValueIndex := 0
  else if PrevMode = 'app' then
    ModePage.SelectedValueIndex := 1
  else
    ModePage.SelectedValueIndex := 0;

  RunPage := CreateOutputMarqueeProgressPage('Installing Sentry Six Manager',
    'This takes a minute or two. Please wait.');

  SummaryMemo := TNewMemo.Create(WizardForm);
  SummaryMemo.Parent := WizardForm.FinishedPage;
  SummaryMemo.ReadOnly := True;
  SummaryMemo.WordWrap := True;
  SummaryMemo.ScrollBars := ssVertical;
  SummaryMemo.Visible := False;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  if ChosenMode = 'service' then
    Result := 'Runs as:' + NewLine + Space + 'a Windows service (always on, also when nobody is signed in)'
  else
    Result := 'Runs as:' + NewLine + Space + 'an app when you sign in';
  Result := Result + NewLine + NewLine + 'Program files:' + NewLine + Space + ExpandConstant('{commonpf64}\SentrySixManager');
  Result := Result + NewLine + NewLine + 'Settings and data (kept on upgrades):' + NewLine + Space + ExpandConstant('{commonappdata}\SentrySixManager');
  if PrevMode <> '' then
    Result := Result + NewLine + NewLine + 'Sentry Six Manager is installed already: it is upgraded in place.';
end;

{ install.ps1 runs here, before Inno writes anything: when it fails, Setup
  stops on this page with its last lines, and there is no Settings > Apps
  entry for a copy that is not there (silent: exit code 7). A first install
  that failed after its program files were in place (the service, Start menu
  entry and firewall rule may be there too) is rolled back with its
  uninstall.ps1, keeping the data, so nothing is left that has no entry. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Mode, Params, ForUser, Failed, Undo: String;
  ResultCode, UndoCode: Integer;
  Ok, HadFolder: Boolean;
begin
  Result := '';
  Mode := ChosenMode;
  HadFolder := DirExists(ExpandConstant('{commonpf64}\SentrySixManager'));
  OutputLog := ExpandConstant('{%TEMP}\SentrySixManager-Setup.log');
  DeleteFile(OutputLog);
  Summary := '';
  SummaryState := 0;
  ShowStatus := not WizardSilent;
  if ShowStatus then begin
    RunPage.SetText('Unpacking...', '');
    RunPage.Show;
  end;
  try
    try
      ExtractTemporaryFiles('{tmp}\payload\*');
      if Mode = 'service' then
        Params := '-Service'
      else
        Params := '-NoService';
      { an app-mode upgrade keeps the person it was installed for }
      if (Mode = 'app') and (PrevMode <> 'app') then begin
        ForUser := SetupUser;
        if ForUser <> '' then
          Params := Params + ' -ForUser "' + ForUser + '"';
      end;
      ResultCode := 0;
      Ok := RunScript(ExpandConstant('{tmp}\payload\install.ps1'), Params, ResultCode);
      if not Ok then
        Result := 'Windows PowerShell could not be started (' + SysErrorMessage(ResultCode) + '). Nothing was installed.'
      else if ResultCode <> 0 then
        Result := 'Sentry Six Manager could not be installed. The last lines it wrote:' + #13#10#13#10 +
          TailText + #13#10 + 'All of it is in ' + OutputLog + '.';
    except
      Result := 'Sentry Six Manager could not be installed: ' + GetExceptionMessage;
    end;
  finally
    if ShowStatus then
      RunPage.Hide;
  end;
  if Result <> '' then begin
    Log('Install failed: ' + Result);
    Undo := ExpandConstant('{commonpf64}\SentrySixManager\uninstall.ps1');
    if (PrevMode = '') and not HadFolder and FileExists(Undo) then begin
      Failed := Result;
      SaveStringToFile(OutputLog, #13#10 + Failed + #13#10 + #13#10 + 'Removing what this install put in place (your data is kept)...' + #13#10, True);
      if ShowStatus then begin
        RunPage.SetText('Removing what was installed...', '');
        RunPage.Show;
      end;
      UndoCode := 0;
      Ok := False;
      try
        try
          Ok := RunScript(Undo, '-Force -FromSetup', UndoCode);
        except
          Log('Could not run uninstall.ps1: ' + GetExceptionMessage);
        end;
      finally
        if ShowStatus then
          RunPage.Hide;
      end;
      if Ok and (UndoCode = 0) then
        Result := Failed + #13#10#13#10 + 'What this install had put in place was removed again (your data in ' +
          ExpandConstant('{commonappdata}\SentrySixManager') + ' is kept).'
      else
        Result := Failed + #13#10#13#10 + 'Removing what this install had put in place did not finish either. Run ' +
          Undo + ' as administrator to remove it.';
      Log('Rollback: ' + IntToStr(UndoCode));
    end;
    SaveStringToFile(OutputLog, #13#10 + Result + #13#10, True);
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and (Summary <> '') then begin
    SummaryMemo.Left := WizardForm.FinishedLabel.Left;
    SummaryMemo.Width := WizardForm.FinishedLabel.Width;
    SummaryMemo.Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(8);
    SummaryMemo.Height := ScaleY(110);
    SummaryMemo.Text := Summary;
    SummaryMemo.Visible := True;
    WizardForm.RunList.Top := SummaryMemo.Top + SummaryMemo.Height + ScaleY(8);
  end;
end;

{ uninstall.ps1 runs before Inno removes its own entry: when it fails, the
  uninstall stops and the entry stays, so it can be tried again. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Script, Params: String;
  ResultCode: Integer;
  RemoveData, Ok: Boolean;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  InUninstall := True;
  OutputLog := ExpandConstant('{%TEMP}\SentrySixManager-Uninstall.log');
  DeleteFile(OutputLog);
  { the program folder's own (an update may have brought a newer one), or
    the copy beside the uninstaller when that folder is gone }
  Script := ExpandConstant('{app}\uninstall.ps1');
  if not FileExists(Script) then
    Script := ExpandConstant('{commonpf64}\SentrySixManager-Uninstall\uninstall.ps1');
  if not FileExists(Script) then begin
    Log('No uninstall.ps1 to run; removing the Settings > Apps entry only.');
    Exit;
  end;

  RemoveData := ExpandConstant('{param:REMOVEDATA|0}') = '1';
  if not RemoveData and not UninstallSilent then
    RemoveData := MsgBox('Also delete your Sentry Six Manager data?' + #13#10#13#10 +
      ExpandConstant('{commonappdata}\SentrySixManager') + ': accounts, settings, certificates, logs, ' +
      'converted files, the Recycle Bin and everything in its footage folder. Footage on your NAS shares, ' +
      'and whatever a link in there points to, is not touched. This cannot be undone.' + #13#10#13#10 +
      'Choose No to keep it: installing again carries on where you left off.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;

  { -Force: ends the app if it is still running after 2 minutes (the
    uninstall was asked for), and asks nothing more about the data }
  Params := '-Force -FromSetup';
  if RemoveData then
    Params := Params + ' -RemoveData';
  ResultCode := 0;
  Ok := False;
  try
    Ok := RunScript(Script, Params, ResultCode);
  except
    Log('Could not run uninstall.ps1: ' + GetExceptionMessage);
  end;
  if not Ok or (ResultCode <> 0) then begin
    Log('uninstall.ps1 failed (exit code ' + IntToStr(ResultCode) + ')');
    SuppressibleMsgBox('Sentry Six Manager could not be removed completely. It stays in Settings > Apps, so you can try again. The last lines it wrote:' +
      #13#10#13#10 + TailText + #13#10 + 'All of it is in ' + OutputLog + '.', mbCriticalError, MB_OK, IDOK);
    Abort;
  end;
end;
