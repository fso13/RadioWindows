; Inno Setup 6 script for RadioWindows.
; Build with: iscc /DMyAppVersion=1.0.0 installer/RadioWindows.iss
; Expects a published self-contained build in ..\publish.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

; Numeric version for the PE file metadata; release tags may carry a suffix.
#ifndef MyAppVersionInfo
  #define MyAppVersionInfo "0.0.0"
#endif

#define MyAppName "RadioWindows"
#define MyAppPublisher "fso13"
#define MyAppExeName "RadioWindows.exe"
#define MyAppUrl "https://github.com/fso13/RadioWindows"

[Setup]
AppId={{7F2A0C64-5B0E-4B54-9A5E-6D3C8C1A9B41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases/latest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=RadioWindows-Setup
SetupIconFile=..\src\RadioWindows\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersionInfo}.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Интернет-радио в трее
VersionInfoCompany={#MyAppPublisher}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.CreateDesktopIcon=Создать ярлык на рабочем столе
english.CreateDesktopIcon=Create a desktop shortcut
russian.LaunchApp=Запустить RadioWindows
english.LaunchApp=Launch RadioWindows

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; Deep links: radiowindows://play/<id>, /pause, /open, /settings
Root: HKCU; Subkey: "Software\Classes\radiowindows"; ValueType: string; ValueName: ""; ValueData: "URL:RadioWindows"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\radiowindows"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\radiowindows\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\radiowindows\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --url ""%1"""; Flags: uninsdeletekey

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Удалить {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--show"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

