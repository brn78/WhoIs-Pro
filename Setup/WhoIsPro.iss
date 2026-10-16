; =============================================================================
;  WhoIs Pro - programma di installazione per Windows (Inno Setup 6 o 7)
;
;  Si compila con  Scripts\Build-Setup.ps1  che pubblica il progetto e definisce
;  AppVersion, SourceDir e OutputDir.
;
;  Cosa fa il setup:
;   - installa il programma (runtime .NET incluso) per l'utente corrente, senza
;     diritti di amministratore, in %LOCALAPPDATA%\Programs\WhoIs Pro (oppure per
;     tutti gli utenti in Programmi, a scelta)
;   - crea i collegamenti nel menu Start e, a richiesta, sul desktop
;   - eseguito sopra un'installazione esistente aggiorna i file; i dati restano in
;     %LOCALAPPDATA%\WhoIsPro
;  Aggiornamento dal programma:  Setup.exe /SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RIAVVIA=1
;  (il programma si chiude, viene aggiornato e riaperto)
; =============================================================================

#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\WhoIsPro"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#define MyAppName "WhoIs Pro"
#define MyAppPublisher "Bruno Leonardi"
#define MyAppExeName "WhoIsPro.exe"
#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(AddBackslash(SourceDir) + MyAppExeName)
#endif
#define HasWizardImages FileExists(AddBackslash(SourcePath) + "Images\WizardSmall-55.bmp") && FileExists(AddBackslash(SourcePath) + "Images\WizardLarge-164.bmp")

[Setup]
AppId={{3C9E6B2A-7F41-4D58-9A0C-5B1E2F7D8A64}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/brn78/WhoIs-Pro
AppSupportURL=https://github.com/brn78/WhoIs-Pro/issues
AppUpdatesURL=https://github.com/brn78/WhoIs-Pro/releases
AppCopyright=© 2026 {#MyAppPublisher}. Tutti i diritti riservati.
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=© 2026 {#MyAppPublisher}. Tutti i diritti riservati.
VersionInfoDescription=Installazione di {#MyAppName}
DefaultDirName={autopf}\WhoIs Pro
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DirExistsWarning=no
OutputDir={#OutputDir}
OutputBaseFilename=WhoIsPro-{#AppVersion}-Setup
SetupIconFile=..\Assets\WhoIsPro.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
#if HasWizardImages
WizardSmallImageFile=Images\WizardSmall-55.bmp,Images\WizardSmall-69.bmp,Images\WizardSmall-83.bmp,Images\WizardSmall-110.bmp
WizardImageFile=Images\WizardLarge-164.bmp,Images\WizardLarge-205.bmp,Images\WizardLarge-246.bmp,Images\WizardLarge-328.bmp
#endif
Compression=lzma2/max
SolidCompression=yes
#if Ver >= EncodeVer(7,0,0,0)
SetupArchitecture=x64
#endif
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 o successivi (WebView2 Runtime e .NET 10)
MinVersion=10.0.17763
; Per l'utente corrente, senza UAC; la finestra iniziale permette di scegliere "tutti gli utenti"
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
ShowLanguageDialog=auto
SetupLogging=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
italian.TaskDesktop=Crea un collegamento sul desktop
english.TaskDesktop=Create a desktop shortcut
italian.RunApp=Avvia WhoIs Pro
english.RunApp=Start WhoIs Pro
italian.DeleteDataQuestion=Vuoi eliminare anche i dati di WhoIs Pro (database dei produttori aggiornato, posizione della finestra, dati di WebView2)?%n%n%1%n%nLe sessioni salvate (.whoispro) non vengono toccate.
english.DeleteDataQuestion=Do you also want to delete the WhoIs Pro data (updated vendor database, window position, WebView2 data)?%n%n%1%n%nSaved sessions (.whoispro) are not touched.

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Il programma riconosce di essere stato installato con il setup (InstallDir = cartella in esecuzione): abilita «Scarica e installa»
Root: HKA; Subkey: "Software\BrunoLeonardi"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\BrunoLeonardi\WhoIsPro"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\BrunoLeonardi\WhoIsPro"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"
Root: HKA; Subkey: "Software\BrunoLeonardi\WhoIsPro"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"

[Run]
; Installazione dalla procedura guidata: il programma si apre alla fine, se l'utente vuole
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "{cm:RunApp}"; Flags: postinstall nowait skipifsilent
; Aggiornamento avviato dal programma (/RIAVVIA=1): il programma si riapre da solo
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Flags: nowait; Check: RestartRequested

[Code]
{ /RIAVVIA=1 sulla riga di comando: il setup è stato avviato dal programma per aggiornarsi }
function RestartRequested: Boolean;
begin
  Result := ExpandConstant('{param:RIAVVIA|0}') = '1';
end;

function DataDir: String;
begin
  Result := ExpandConstant('{localappdata}\WhoIsPro');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if (not UninstallSilent) and DirExists(DataDir) then
      if MsgBox(FmtMessage(CustomMessage('DeleteDataQuestion'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
end;
