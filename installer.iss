; Inno Setup script. Build with: iscc installer.iss /DSourceDir=out-fd /DOutName=FluentVantageToolbar-Setup /DBundled=0
#ifndef SourceDir
  #define SourceDir "out"
#endif
#ifndef OutName
  #define OutName "FluentVantageToolbar-Setup"
#endif
#ifndef Bundled
  #define Bundled 0
#endif
#define AppName "Fluent Vantage Toolbar"
#define AppVersion "0.3.0"

[Setup]
AppId={{6B2D0C0E-5A3F-4E0B-9C61-4F1A7E2D9A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Mohamed Elnaggar
DefaultDirName={autopf}\FluentLegionToolbar
DefaultGroupName={#AppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=installers
OutputBaseFilename={#OutName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SourceDir}\app.ico
UninstallDisplayIcon={app}\FluentLegionToolbar.exe
DisableProgramGroupPage=yes
CloseApplications=force
RestartApplications=no
CloseApplicationsFilter=FluentLegionToolbar.exe

[Tasks]
Name: "startup"; Description: "Start with Windows with administrator rights, without a UAC prompt (scheduled task)"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\FluentLegionToolbar.exe"; Parameters: "--show"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\install-startup.ps1"" -Silent"; Flags: runhidden waituntilterminated; Tasks: startup; StatusMsg: "Registering the startup task..."
Filename: "{app}\FluentLegionToolbar.exe"; Parameters: "--show"; Description: "Open {#AppName}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall-startup.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveStartupTask"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  { New releases signal the tray instance directly. Earlier versions are closed
    by Inno Setup's Restart Manager (force close enabled for this executable). }
  if FileExists(ExpandConstant('{app}\FluentLegionToolbar.exe')) then
    Exec(ExpandConstant('{app}\FluentLegionToolbar.exe'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  { Compatibility with 0.2.x: those releases treat WM_CLOSE as hide-to-tray.
    Limit the fallback to the installed executable path, never all same-name apps. }
  Exec('powershell.exe', '-NoProfile -NonInteractive -Command "' +
    '$target=' + #39 + ExpandConstant('{app}\FluentLegionToolbar.exe') + #39 + '; ' +
    '$apps=@(Get-Process FluentLegionToolbar -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }); ' +
    'foreach($p in $apps) { $p.CloseMainWindow() | Out-Null; if(-not $p.WaitForExit(3000)) { $p.Kill(); $p.WaitForExit(5000) | Out-Null } }"',
    '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if ExitCode <> 0 then Result := 'Could not close the running toolbar. Close it and try the update again.';
end;

#if Str(Bundled) == "0"
function HasDesktopRuntime8: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then Result := True;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not HasDesktopRuntime8 then
    if MsgBox('The .NET 8 Desktop Runtime (x64) was not found. The app will not start without it.' + #13#10 +
              'Install it from https://dotnet.microsoft.com/download/dotnet/8.0 or use the installer that includes .NET.' + #13#10#13#10 +
              'Continue anyway?', mbConfirmation, MB_YESNO) = IDNO then Result := False;
end;
#endif
