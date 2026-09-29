; ============================================================
; Echopad Installer (Inno Setup 6.x) - FULL TEMPLATE
; ============================================================

#define MyAppName        "EchoPad"
#define MyAppPublisher   "ElkaSoft"
#define MyAppURL         "https://github.com/torment78/Echopad"
#define MyAppExeName     "Echopad.App.exe"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0-dev.20260929.3"
#endif
#ifndef AppBuildDir
  #define AppBuildDir SourcePath + "\..\artifacts\publish"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir SourcePath + "\..\artifacts\release"
#endif
#define InstallerIcon SourcePath + "\Assets\Ecopadc.ico"

; Use the approved EchoPad artwork without cropping or generating a separate variant.
#define WizardPortrait "..\graphics\echopad-vertical.png"
#define WizardHeader "..\graphics\references\echopad-icon.png"

[Setup]
AppId={{A2F2F07E-7A2F-4CE9-9D53-9E4F6B6F2F11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\ElkaSoft\{#MyAppName}
DefaultGroupName={#MyAppName}

; ✅ YOUR installer output folder:
OutputDir={#InstallerOutputDir}
#ifdef BrandingPreview
OutputBaseFilename=EchoPad-Installer-Preview
#else
OutputBaseFilename=EchoPad-{#MyAppVersion}-Unsigned-Setup
#endif
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Installer EXE icon = your app icon
SetupIconFile={#InstallerIcon}

; Apps & Features icon (uses installed EXE icon)
UninstallDisplayIcon={app}\{#MyAppExeName}

; Match the Throttle / VBAN Stream / VBAN Plug installer family.
WizardStyle=modern dark polar includetitlebar
WizardSizePercent=120,120
WizardImageFile={#WizardPortrait}
WizardSmallImageFile={#WizardHeader}

DisableProgramGroupPage=yes
DisableWelcomePage=no
; Move upgrades to the ElkaSoft family folder instead of reusing the old location.
UsePreviousAppDir=no
#ifdef BrandingPreview
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=no
CreateUninstallRegKey=no
#else
PrivilegesRequired=admin
#endif

; Optional signing (configure in Inno: Tools -> Configure Sign Tools)
; SignTool=mystandard
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]

#ifndef BrandingPreview
Source: "{#AppBuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif
Source: "Assets\ElkaSoft.png"; Flags: dontcopy

[Icons]
#ifndef BrandingPreview
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
#endif

[Run]
#ifndef BrandingPreview
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
#endif

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2F2F07E-7A2F-4CE9-9D53-9E4F6B6F2F11}_is1';
var
  PreviousInstallDir: String;
  LegacyInstallDirs: TArrayOfString;
  WelcomeLogo, FinishedLogo: TBitmapImage;

procedure FitWizardArtwork(Image: TBitmapImage);
var
  ArtworkHeight: Integer;
begin
  { Preserve the 941 x 1672 portrait's aspect ratio and all of its lettering. }
  ArtworkHeight := (Image.Width * 1672) div 941;
  Image.Top := Image.Top + (Image.Height - ArtworkHeight) div 2;
  Image.Height := ArtworkHeight;
end;

procedure AddBrandLogo(var Logo: TBitmapImage; ParentPage: TNewNotebookPage; LeftEdge: Integer);
begin
  Logo := TBitmapImage.Create(WizardForm);
  Logo.Parent := ParentPage;
  Logo.SetBounds(LeftEdge, ParentPage.Height - ScaleY(120), ScaleX(108), ScaleY(108));
  Logo.Stretch := True;
  Logo.PngImage.LoadFromFile(ExpandConstant('{tmp}\ElkaSoft.png'));
end;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('ElkaSoft.png');
  FitWizardArtwork(WizardForm.WizardBitmapImage);
  FitWizardArtwork(WizardForm.WizardBitmapImage2);
  WizardForm.WelcomeLabel1.Caption := 'EchoPad';
  WizardForm.WelcomeLabel2.Caption :=
    'Capture the moment. Play it your way.' + #13#10#13#10 +
    '16 pads with 15-second rolling capture, local and VBAN audio, MIDI control and custom pad artwork.' + #13#10#13#10 +
    'Version {#MyAppVersion}  /  Windows x64' + #13#10 +
    'Unsigned development release' + #13#10#13#10 +
    'Close EchoPad before continuing. Your saved profiles and media are kept.';
  WizardForm.WelcomeLabel2.Height := ScaleY(180);
  AddBrandLogo(WelcomeLogo, WizardForm.WelcomePage, WizardForm.WelcomeLabel2.Left);
  AddBrandLogo(FinishedLogo, WizardForm.FinishedPage, WizardForm.FinishedLabel.Left);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'EchoPad is ready';
    WizardForm.FinishedLabel.Caption :=
      'EchoPad is installed in:' + #13#10#13#10 + WizardDirValue + #13#10#13#10 +
      'Open Settings > General for Windows startup and tray options.' + #13#10 +
      'If EchoPad opens to the tray, double-click its notification-area icon to show it.';
    WizardForm.FinishedLabel.Height := ScaleY(150);
    WizardForm.RunList.Top := WizardForm.FinishedLabel.Top + ScaleY(154);
    WizardForm.RunList.Height := ScaleY(30);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
#ifdef BrandingPreview
  Exit;
#endif
  if not RegQueryStringValue(HKLM64, UninstallKey, 'InstallLocation', PreviousInstallDir) then
    if not RegQueryStringValue(HKLM32, UninstallKey, 'InstallLocation', PreviousInstallDir) then
      if not RegQueryStringValue(HKCU64, UninstallKey, 'InstallLocation', PreviousInstallDir) then
        RegQueryStringValue(HKCU32, UninstallKey, 'InstallLocation', PreviousInstallDir);
  if PreviousInstallDir <> '' then
  begin
    LoadStringsFromFile(AddBackslash(PreviousInstallDir) + 'legacy-install-paths.txt', LegacyInstallDirs);
    SetArrayLength(LegacyInstallDirs, GetArrayLength(LegacyInstallDirs) + 1);
    LegacyInstallDirs[GetArrayLength(LegacyInstallDirs) - 1] := PreviousInstallDir;
  end;
end;

#ifdef BrandingPreview
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := 'This build previews the installer artwork only. Installation is disabled. Close Setup when finished.';
end;
#endif

procedure RegisterExtraCloseApplicationsResources;
begin
  if PreviousInstallDir <> '' then
    RegisterExtraCloseApplicationsResource(False, AddBackslash(PreviousInstallDir) + '{#MyAppExeName}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (GetArrayLength(LegacyInstallDirs) > 0) then
    if not SaveStringsToUTF8File(ExpandConstant('{app}\legacy-install-paths.txt'), LegacyInstallDirs, False) then
      RaiseException('Could not retain the previous EchoPad data location. Original files have not been changed.');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usPostUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ElkaSoft.EchoPad', StartupCommand) then
      if SameText(StartupCommand, '"' + ExpandConstant('{app}\{#MyAppExeName}') + '" --startup') then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ElkaSoft.EchoPad');
end;
