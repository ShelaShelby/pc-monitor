; Inno Setup 6 script for PC Monitor (per-user install, no admin rights needed to install).
; Built by CI after "dotnet publish":
;   ISCC.exe /DAppVersion=<Version from Directory.Build.props> installer\pc-monitor.iss
; AppVersion has no default on purpose: Directory.Build.props is the only place to edit it.

#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z (CI reads it from Directory.Build.props)
#endif
#ifndef SourceExe
  #define SourceExe "..\artifacts\publish\pc-monitor.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{6F2B9C1E-4D7A-4E58-9B3F-2C8E1A5D7F40}
AppName=PC Monitor
AppVersion={#AppVersion}
AppVerName=PC Monitor {#AppVersion}
AppPublisher=ShelaShelby
AppPublisherURL=https://github.com/ShelaShelby/pc-monitor
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\PC Monitor
DisableWelcomePage=no
DisableDirPage=no
DefaultGroupName=PC Monitor
DisableProgramGroupPage=yes
DisableReadyPage=yes
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\pc-monitor.exe
UninstallDisplayName=PC Monitor
OutputDir={#OutputDir}
OutputBaseFilename=pc-monitor-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\PC Monitor"; Filename: "{app}\pc-monitor.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PC Monitor"; Filename: "{app}\pc-monitor.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; shellexec: the exe asks for administrator rights (its manifest), which needs ShellExecute to show the UAC prompt.
Filename: "{app}\pc-monitor.exe"; Description: "Run PC Monitor"; Flags: postinstall nowait skipifsilent shellexec

[UninstallDelete]
Type: files; Name: "{app}\settings.json"
Type: files; Name: "{app}\pc-monitor.log"
Type: files; Name: "{localappdata}\PcMonitor\settings.json"
Type: files; Name: "{localappdata}\PcMonitor\pc-monitor.log"
Type: dirifempty; Name: "{localappdata}\PcMonitor"
Type: dirifempty; Name: "{app}"
