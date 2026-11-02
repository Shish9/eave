; Eave installer, built with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
;
;   iscc /DAppVersion=0.3.3 installer\Eave.iss                    builds from ..\dist
;   iscc /DAppVersion=0.3.3 /DSourceDir=C:\path\signed installer\Eave.iss
;
; build.ps1 and .github/workflows/release.yml run this for you. The result is dist\Eave-Setup-<version>.exe.
;
; Per-user install (no administrator prompt) into %LOCALAPPDATA%\Eave, the same place the app's own
; "Eave.exe --install" uses, so the hook relay and Eave Studio find everything where they expect it.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist"
#endif

#define AppName "Eave"
#define AppExe "Eave.exe"

[Setup]
; Never change this GUID: it is how Windows (and later versions of this installer) recognise Eave.
AppId={{FE9FBE53-E830-44F7-B727-A7EEB7CCF5D6}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Eave contributors
AppPublisherURL=https://github.com/Shish9/eave
AppSupportURL=https://github.com/Shish9/eave/issues
AppUpdatesURL=https://github.com/Shish9/eave/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Eave
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\ClaudeIsland\eave.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=Eave-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Eave holds this mutex while it runs. Setup asks the user to close it (or closes it) before replacing files.
AppMutex=EaveSingleton
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start Eave when Windows starts"; GroupDescription: "Options:"
Name: "hooks"; Description: "Connect to Claude Code (adds hooks to ~/.claude/settings.json; a backup is saved first)"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\Eave.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\relay.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Eave"; Filename: "{app}\{#AppExe}"
Name: "{userprograms}\Eave Studio"; Filename: "{app}\{#AppExe}"; Parameters: "--studio"; Comment: "Customize Eave"
Name: "{userdesktop}\Eave"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Start with Windows (the same per-user entry Eave Studio's "Start with Windows" switch uses).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Eave"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart
; A copy installed from the zip with "Eave.exe --install" registered its own Settings > Apps entry.
; Remove it so Eave is listed once, by this installer.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Uninstall\Eave"; Flags: deletekey

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--install-hooks"; Flags: runhidden; Tasks: hooks; StatusMsg: "Connecting to Claude Code..."
Filename: "{app}\{#AppExe}"; Description: "Start Eave"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop Eave, then take its hooks out of Claude Code's settings. Your Eave settings (%APPDATA%\Eave) are kept.
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopEave"
Filename: "{app}\{#AppExe}"; Parameters: "--remove-hooks"; Flags: runhidden; RunOnceId: "RemoveHooks"
