; This Source Code Form is subject to the terms of the Mozilla Public
; License, v. 2.0. If a copy of the MPL was not distributed with this
; file, You can obtain one at https://mozilla.org/MPL/2.0/.
;
; Copyright (c) 2026 ClearCode Inc.

; PopGuard Setup --
;
; PopGuard is a standalone system-tray application (PopGuard.exe) that pushes
; intrusive top-most pop-up windows to the back. It has no browser extension and
; no native messaging host, so this installer only deploys the published app and
; (optionally) registers it to start at logon.
;
; Build the payload first:
;   dotnet publish PopGuard\PopGuard.csproj -c Release -r win-x64 --self-contained false
;   (self-contained false requires the .NET 9 Desktop Runtime on the target;
;    pass --self-contained true to bundle the runtime instead.)
; Then compile this script with Inno Setup (ISCC.exe PopGuard.iss).

#define AppVersion "1.0.0.0"
#define AppExeName "PopGuard.exe"
; Published output (same folder for self-contained and framework-dependent).
#define PublishDir "PopGuard\bin\Release\net9.0-windows10.0.22621.0\win-x64\publish"

[Setup]
AppName=PopGuard
AppVerName=PopGuard {#AppVersion}
VersionInfoVersion={#AppVersion}
AppVersion={#AppVersion}
AppPublisher=ClearCode Inc.
AppMutex=PopGuardSetup
DefaultDirName={autopf}\PopGuard
DefaultGroupName=PopGuard
Compression=lzma2
SolidCompression=yes
OutputDir=SetupOutput
OutputBaseFilename=PopGuardSetup-{#AppVersion}
SetupIconFile=PopGuard\PopGuard.ico
VersionInfoDescription=PopGuard Setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=admin

[Languages]
Name: jp; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "autostart"; Description: "Windows へのサインイン時に自動的に起動する"; GroupDescription: "スタートアップ:"

[Files]
; Application (exe, dlls, runtimeconfig/deps, and the ja\ satellite assembly)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Permissions: users-readexec admins-full system-full

; Default config (optional). Admins normally export PopGuard.rules.json from the
; parameter sheet and place it next to the exe. onlyifdoesntexist keeps a config
; that is already there on upgrade; skipifsourcedoesntexist makes the line a no-op
; when no default file is provided.
Source: "Resources\PopGuard.rules.json"; DestDir: "{app}"; Flags: onlyifdoesntexist skipifsourcedoesntexist; Permissions: users-readexec admins-full system-full

; License (optional)
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist; Permissions: users-readexec admins-full system-full

[Icons]
Name: "{group}\PopGuard"; Filename: "{app}\{#AppExeName}"
Name: "{group}\PopGuard をアンインストール"; Filename: "{uninstallexe}"

[Registry]
; Install record (also used as the uninstall anchor).
Root: HKLM; Subkey: "Software\PopGuard"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\PopGuard"; ValueType: string; ValueName: "Path"; ValueData: "{app}\"
Root: HKLM; Subkey: "Software\PopGuard"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"
Root: HKLM; Subkey: "Software\PopGuard"; ValueType: string; ValueName: "ConfigFile"; ValueData: "{app}\PopGuard.rules.json"

; Start at logon for every user (per-user session). Gated by the autostart task.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PopGuard"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Offer to launch right after installation (interactive installs only).
Filename: "{app}\{#AppExeName}"; Description: "PopGuard を起動する"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the resident app before removing its files.
Filename: "{sys}\taskkill.exe"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden; RunOnceId: "StopPopGuard"

[Code]
procedure TaskKill(FileName: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im "' + FileName + '"', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Stop a running instance so its files can be overwritten during install.
function InitializeSetup(): Boolean;
begin
  TaskKill('{#AppExeName}');
  Result := True;
end;
