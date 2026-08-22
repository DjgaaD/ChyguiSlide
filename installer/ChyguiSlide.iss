; ChyguiSlide — Inno Setup 6 script (per-machine / Program Files)
; Compile via scripts\Build-Installer.ps1
; Encoding: UTF-8 with BOM (required for Cyrillic AppName / shortcuts)

#ifexist "GeneratedDefines.iss"
  #include "GeneratedDefines.iss"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "0.0.4"
#endif
#ifndef MyAppVersionLabel
  #define MyAppVersionLabel "0.0.4-beta"
#endif
#ifndef MyAppDisplayName
  #define MyAppDisplayName "Чугуй Слайды (beta)"
#endif
#ifndef MyAppPublisher
  #define MyAppPublisher "Чугуй Слайды"
#endif
#ifndef MyAppGroupName
  #define MyAppGroupName "Чугуй Слайды"
#endif

#define MyAppExeName "ChyguiSlide.exe"
; Stable across versions — required for upgrades / Add/Remove Programs
; Double brace → single brace in AppId
#define MyAppId "{{C8D1A4E7-2B5F-4C9A-8E3D-7F0A1B6C4D92}"
#define MyAppUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{C8D1A4E7-2B5F-4C9A-8E3D-7F0A1B6C4D92}_is1"

[Setup]
AppId={#MyAppId}
AppName={#MyAppDisplayName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppDisplayName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf64}\ChyguiSlide
DefaultGroupName={#MyAppGroupName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\release
OutputBaseFilename=ChyguiSlide-{#MyAppVersionLabel}-Setup
; #ifexist "..\Assets\AppIcon.ico"
; SetupIconFile=..\Assets\AppIcon.ico
; #endif
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppDisplayName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppDisplayName}
VersionInfoProductVersion={#MyAppVersion}
DisableDirPage=no
AllowNoIcons=yes
; Не подтягивать старые tasks (desktopicon) при обновлении
UsePreviousTasks=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Published layout: launcher + app\ + README
Source: "..\artifacts\release\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Ярлыки только при первой установке — при обновлении не трогаем (пользователь мог перенести)
Name: "{group}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"; Check: not IsAppUpgrade
Name: "{group}\{cm:UninstallProgram,{#MyAppDisplayName}}"; Filename: "{uninstallexe}"; Check: not IsAppUpgrade
Name: "{autodesktop}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Check: not IsAppUpgrade

[Run]
; Без skipifsilent — после тихого обновления тоже запускаем.
; runasoriginaluser — не от администратора (UAC installer).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppDisplayName}}"; Flags: nowait postinstall runasoriginaluser

[Code]
function IsAppUpgrade: Boolean;
begin
  Result :=
    RegKeyExists(HKLM64, '{#MyAppUninstallKey}') or
    RegKeyExists(HKLM32, '{#MyAppUninstallKey}') or
    RegKeyExists(HKCU, '{#MyAppUninstallKey}');
end;
