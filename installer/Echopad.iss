; ============================================================
; Echopad Installer (Inno Setup 6.x) - FULL TEMPLATE
; ============================================================

#define MyAppName        "EchoPad"
#define MyAppPublisher   "ElkaSoft"
#define MyAppURL         "https://github.com/torment78/Echopad"
#define MyAppExeName     "Echopad.App.exe"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0-dev.20260929.2"
#endif
#ifndef AppBuildDir
  #define AppBuildDir SourcePath + "\..\artifacts\publish"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir SourcePath + "\..\artifacts\release"
#endif
#define InstallerIcon SourcePath + "\Assets\Ecopadc.ico"

; Wizard images (put these files in: C:\Users\torme\source\repos\Echopad\installer\Assets\)
#define WizardSidebarLight   "Assets\wizard_sidebar_light.png"
#define WizardSidebarDark    "Assets\wizard_sidebar_dark.png"
#define WizardHeaderLight    "Assets\wizard_header_light.png"
#define WizardHeaderDark     "Assets\wizard_header_dark.png"

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
OutputBaseFilename=EchoPad-{#MyAppVersion}-Unsigned-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Installer EXE icon = your app icon
SetupIconFile={#InstallerIcon}

; Apps & Features icon (uses installed EXE icon)
UninstallDisplayIcon={app}\{#MyAppExeName}

; Modern wizard + dynamic Windows dark mode
WizardStyle=modern dynamic includetitlebar
WizardImageFile={#WizardSidebarLight}
WizardSmallImageFile={#WizardHeaderLight}
WizardImageFileDynamicDark={#WizardSidebarDark}
WizardSmallImageFileDynamicDark={#WizardHeaderDark}

DisableProgramGroupPage=yes
DisableWelcomePage=no
; Move upgrades to the ElkaSoft family folder instead of reusing the old location.
UsePreviousAppDir=no
PrivilegesRequired=admin

; Optional signing (configure in Inno: Tools -> Configure Sign Tools)
; SignTool=mystandard
; SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]

Source: "{#AppBuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2F2F07E-7A2F-4CE9-9D53-9E4F6B6F2F11}_is1';
var
  PreviousInstallDir: String;
  LegacyInstallDirs: TArrayOfString;

function InitializeSetup: Boolean;
begin
  Result := True;
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
