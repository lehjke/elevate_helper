#ifndef AppName
  #define AppName "Elevate Helper"
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef ReleaseTag
  #define ReleaseTag "dev"
#endif

#ifndef Runtime
  #define Runtime "win-x64"
#endif

#ifndef SourceDir
  #define SourceDir "..\\artifacts\\publish\\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\\artifacts\\release"
#endif

#ifndef Publisher
  #define Publisher "Elevate Helper"
#endif

#ifndef PublisherUrl
  #define PublisherUrl "https://github.com/lehjke/elevate_helper"
#endif

#define AppExeName "ElevateHelperWinUI.exe"
#define AppId "{{8B59A4C3-46A1-4627-B077-8A302DA9E1C1}}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#PublisherUrl}
AppSupportURL={#PublisherUrl}
AppUpdatesURL={#PublisherUrl}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=ElevateHelper-{#Runtime}-{#ReleaseTag}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
