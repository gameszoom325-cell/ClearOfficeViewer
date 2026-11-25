#define MyAppName "ClearOffice Viewer"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ClearOffice Contributors"
#define MyAppURL "https://github.com/gameszoom325-cell/ClearOfficeViewer"
#define MyAppExeName "ClearOfficeViewer.exe"

[Setup]
AppId={{8F4C2B91-3D5E-4A7C-9E1B-2F6A8C4D0E12}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\ClearOffice Viewer
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=ClearOfficeViewer-Setup
SetupIconFile=..\assets\icon\ClearOfficeViewer.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "Register file associations (.pdf, .doc, .docx, .ppt, .pptx, .html, .htm) in Windows Open With"; GroupDescription: "Windows Integration:"

[Files]
Source: "..\publish\setup-payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; 1. Applications registration (appears in File Explorer -> Open With -> Choose another app)
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pdf"; ValueData: ""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".doc"; ValueData: ""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".docx"; ValueData: ""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".ppt"; ValueData: ""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pptx"; ValueData: ""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".html"; ValueData: ""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".htm"; ValueData: ""; Tasks: fileassoc

; 2. ProgIDs & OpenWithProgids
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Pdf"; ValueType: string; ValueData: "ClearOffice PDF Document"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Pdf\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Pdf\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.pdf\OpenWithProgids"; ValueType: string; ValueName: "ClearOfficeViewer.Pdf"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Doc"; ValueType: string; ValueData: "ClearOffice Word 97-2003 Document"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Doc\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Doc\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.doc\OpenWithProgids"; ValueType: string; ValueName: "ClearOfficeViewer.Doc"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Docx"; ValueType: string; ValueData: "ClearOffice Word Document"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Docx\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Docx\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.docx\OpenWithProgids"; ValueType: string; ValueName: "ClearOfficeViewer.Docx"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Ppt"; ValueType: string; ValueData: "ClearOffice PowerPoint 97-2003 Presentation"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Ppt\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Ppt\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.ppt\OpenWithProgids"; ValueType: string; ValueName: "ClearOfficeViewer.Ppt"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Pptx"; ValueType: string; ValueData: "ClearOffice PowerPoint Presentation"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Pptx\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Pptx\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.pptx\OpenWithProgids"; ValueType: string; ValueName: "ClearOfficeViewer.Pptx"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Html"; ValueType: string; ValueData: "ClearOffice HTML Web Document"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Html\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Html\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.html\OpenWithProgids"; ValueType: string; ValueName: "ClearOfficeViewer.Html"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Htm"; ValueType: string; ValueData: "ClearOffice HTML Web Document"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Htm\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\ClearOfficeViewer.Htm\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.htm\OpenWithProgids"; ValueType: string; ValueName: "ClearOfficeViewer.Htm"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

; 3. Windows Default Apps Capabilities registration
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Lightweight native viewer for PDF, Word, PowerPoint, and HTML files."; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "ClearOfficeViewer.Pdf"; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".doc"; ValueData: "ClearOfficeViewer.Doc"; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".docx"; ValueData: "ClearOfficeViewer.Docx"; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".ppt"; ValueData: "ClearOfficeViewer.Ppt"; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pptx"; ValueData: "ClearOfficeViewer.Pptx"; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".html"; ValueData: "ClearOfficeViewer.Html"; Tasks: fileassoc
Root: HKA; Subkey: "Software\ClearOfficeViewer\Capabilities\FileAssociations"; ValueType: string; ValueName: ".htm"; ValueData: "ClearOfficeViewer.Htm"; Tasks: fileassoc
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: "Software\ClearOfficeViewer\Capabilities"; Flags: uninsdeletevalue; Tasks: fileassoc

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
