; ============================================================
; Echopad Installer (Inno Setup 6.x) - FULL TEMPLATE
; ============================================================

#define MyAppName        "EchoPad"
#define MyAppPublisher   "ElkaSoft"
#define MyAppURL         "https://github.com/torment78/Echopad"
#define MyAppExeName     "Echopad.App.exe"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0-dev.20260929.1"
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

DefaultDirName={autopf}\{#MyAppName}
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
UsePreviousAppDir=yes
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
