#if (Ver >> 8) != (EncodeVer(6, 7, 4) >> 8)
  #error Inno Setup 6.7.4 is required
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
#ifndef AppSource
  #error AppSource is required
#endif
#ifndef CandidateOutput
  #error CandidateOutput is required
#endif

#ifdef MonetaLab
  #define InstallName "Moneta Lab"
  #define AppGuid "{{71B65119-0CF2-460F-BC50-3AD64DBF6460}"
  #define AppInstance "Local\Moneta.Lab.Running.v1"
  #define SetupInstance "Local\Moneta.Lab.Setup.v1"
#else
  #define InstallName "Moneta"
  #define AppGuid "{{5FB38575-937B-41C4-9581-F984E825178E}"
  #define AppInstance "Local\Moneta.Alpha.Running.v1"
  #define SetupInstance "Local\Moneta.Alpha.Setup.v1"
#endif

[Setup]
AppId={#AppGuid}
AppName={#InstallName}
AppVersion={#ReleaseVersion}
AppPublisher=Moneta
VersionInfoCompany=Moneta
VersionInfoProductName=Moneta
VersionInfoProductVersion={#ReleaseVersion}
VersionInfoVersion={#ReleaseVersion}
DefaultDirName={localappdata}\Programs\{#InstallName}
DefaultGroupName={#InstallName}
DisableDirPage=yes
DisableProgramGroupPage=yes
UsePreviousAppDir=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
UninstallDisplayIcon={app}\Moneta.App.exe
SetupIconFile=..\assets\branding\moneta.ico
OutputDir={#CandidateOutput}
OutputBaseFilename=Moneta-{#ReleaseVersion}-win-x64
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=no
RestartApplications=no
AppMutex={#AppInstance}
SetupMutex={#SetupInstance}
AllowCancelDuringInstall=no

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userprograms}\{#InstallName}"; Filename: "{app}\Moneta.App.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\Moneta.App.exe"; Description: "Lancer {#InstallName}"; Flags: postinstall nowait skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Existing, Candidate: Int64;
begin
  Result := '';
  Candidate := {#StrToVersion(ReleaseVersion + ".0")};
  if CompareText(RemoveBackslashUnlessRoot(ExpandConstant('{app}')),
                 ExpandConstant('{localappdata}\Programs\{#InstallName}')) <> 0 then
    Result := 'Le programme doit être installé dans le dossier Moneta prévu pour ce profil Windows.'
  else if GetPackedVersion(ExpandConstant('{app}\Moneta.App.exe'), Existing) and (ComparePackedVersion(Existing, Candidate) > 0) then
    Result := 'Une version plus récente de Moneta est déjà installée.';
end;
