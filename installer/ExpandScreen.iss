; ExpandScreen Inno Setup Script
; Build:
;   iscc /DMyAppVersion=0.1.0 installer\ExpandScreen.iss
; Or via PowerShell helper:
;   ./scripts/release/windows/Build-WindowsRelease.ps1 -BuildInstaller -IncludeAdb -AppVersion 0.1.0

#define MyAppName "ExpandScreen"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "ExpandScreen"
#define MyAppExeName "ExpandScreen.UI.exe"

; Source directory defaults to artifacts/windows/stage produced by Build-WindowsRelease.ps1
#define SourceDir GetEnv("EXPANDSCREEN_INSTALLER_SOURCE")
#if SourceDir == ""
  #define SourceDir "..\\artifacts\\windows\\stage"
#endif

[Setup]
AppId={{2AEF52A1-8C5B-4A2C-9C4B-2C6E3B29F20A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=ExpandScreen-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\\app\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\\adb\\*"; DestDir: "{app}\\adb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\\driver\\*"; DestDir: "{app}\\driver"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"
Name: "{group}\\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

