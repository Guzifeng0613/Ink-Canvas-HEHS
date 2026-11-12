; Inno Setup script for the WinUI 3 build.
; Build first:  .\build.ps1 publish -Configuration Release
; then compile this script. Paths are relative to the repo root so it works on any machine
; (the original script hard-coded E:\projects\... and only worked on one developer's box).

#define MyAppName "Ink Canvas WinUI3"
#define MyAppVersion "3.0.0"
#define MyAppPublisher "Ink Canvas contributors"
#define MyAppURL "https://github.com/QiXinia/Ink-Canvas-HEHS"
#define MyAppExeName "InkCanvas.exe"
#define PublishDir "artifacts\app"

[Setup]
AppId={{7C4E1B62-9F3A-4C5D-8E21-5A6B7C8D9E0F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
OutputDir=artifacts\installer
OutputBaseFilename=InkCanvasWinUI3-{#MyAppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; WinUI 3 / Windows App SDK is 64-bit only in this build.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Unpackaged self-contained Windows App SDK output: everything the app needs is in this folder.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent