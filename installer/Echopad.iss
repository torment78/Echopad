; ============================================================
; Echopad Installer (Inno Setup 6.x) - FULL TEMPLATE
; ============================================================

#define MyAppName        "Echopad"
#define MyAppPublisher   "ElkaSoft"
#define MyAppURL         "https://example.com"
#define MyAppExeName     "Echopad.App.exe"

#define MyAppName "Echopad"
#define MyAppExeName "Echopad.App.exe"

; CHANGE THIS to your REAL publish folder (this is the INPUT)
#define AppBuildDir      "C:\Users\torme\source\repos\Echopad\installer\Output"

; Script-relative asset folder (wizard images / license / etc)
#define AssetDir SourcePath + "\Assets"

; Your .ico (same as csproj icon)
#define InstallerIcon "C:\Users\torme\source\repos\Echopad\installer\Assets\Ecopadc.ico"

; Wizard images (put these files in: C:\Users\torme\source\repos\Echopad\installer\Assets\)
#define WizardSidebarLight   "Assets\wizard_sidebar_light.png"
#define WizardSidebarDark    "Assets\wizard_sidebar_dark.png"
#define WizardHeaderLight    "Assets\wizard_header_light.png"
#define WizardHeaderDark     "Assets\wizard_header_dark.png"

[Setup]
AppId={{A2F2F07E-7A2F-4CE9-9D53-9E4F6B6F2F11}
AppName={#MyAppName}
AppVersion=1.0.0
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; ✅ YOUR installer output folder:
OutputDir=C:\Users\torme\source\repos\Echopad\installer\Output
OutputBaseFilename={#MyAppName}_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64

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