#ifndef StageDir
  #error StageDir is required
#endif
#ifndef AppVersion
  #define AppVersion "1.0.4"
#endif
[Setup]
AppId={{E6C178CA-B758-4C0D-AF51-64B6D5EC7DF0}
AppName=gpdt
AppVersion={#AppVersion}
DefaultDirName={autopf}\gpdt
DefaultGroupName=gpdt
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\dist
OutputBaseFilename=gpdt-v{#AppVersion}-setup-x64
SetupIconFile=..\assets\gpdt.ico
UninstallDisplayIcon={app}\gpdt.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
AppMutex=Local\gpdt-application
InfoBeforeFile=installer-info.txt

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "autostart"; Description: "Запускать gpdt при входе в Windows с правами администратора"; GroupDescription: "Автозапуск для текущей учётной записи:"
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked

[Files]
Source: "{#StageDir}\scripts\dependencies.ps1"; DestDir: "{tmp}\gpdt-prerequisites\scripts"; Flags: dontcopy
Source: "{#StageDir}\scripts\install-dependencies.ps1"; DestDir: "{tmp}\gpdt-prerequisites\scripts"; Flags: dontcopy
Source: "{#StageDir}\drivers\PawnIO_setup.exe"; DestDir: "{tmp}\gpdt-prerequisites\drivers"; Flags: dontcopy
Source: "{#StageDir}\umdf\install.ps1"; DestDir: "{tmp}\gpdt-prerequisites\umdf"; Flags: dontcopy
Source: "{#StageDir}\umdf\installed-filter.ps1"; DestDir: "{tmp}\gpdt-prerequisites\umdf"; Flags: dontcopy
Source: "{#StageDir}\umdf\gpdt-test.cer"; DestDir: "{tmp}\gpdt-prerequisites\umdf"; Flags: dontcopy
Source: "{#StageDir}\umdf\package\*"; DestDir: "{tmp}\gpdt-prerequisites\umdf\package"; Flags: dontcopy
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "data\*"

[Icons]
Name: "{group}\gpdt"; Filename: "{app}\gpdt.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\gpdt"; Filename: "{app}\gpdt.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\gpdt.exe"; Parameters: "--enable --tray"; Description: "Запустить gpdt"; Flags: nowait postinstall skipifsilent runascurrentuser; Check: CanLaunch

[UninstallRun]
Filename: "{app}\gpdt.exe"; Parameters: "--remove-startup"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveAutostart"

[Code]
var
  DriverConsent: TInputOptionWizardPage;
  DriversInstalled: Boolean;
  DriverRestart: Boolean;

procedure InitializeWizard;
begin
  DriverConsent := CreateInputOptionPage(wpSelectTasks, 'Компоненты для работы кнопок и мощности',
    'Установка PawnIO и фильтра яркости',
    'Будет установлен обычный подписанный PawnIO и локально подписанный фильтр gpdt. Для фильтра сертификат gpdt добавляется в доверенные корневые сертификаты и доверенные издатели этого компьютера. Это разрешает Windows доверять коду, подписанному этим сертификатом. Устанавливайте только сборку gpdt, которой доверяете.', False, False);
  DriverConsent.Add('Разрешаю установить драйверы и добавить сертификат фильтра gpdt');
  DriverConsent.Values[0] := ExpandConstant('{param:DRIVERCONSENT|no}') = 'yes';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = DriverConsent.ID) and not DriverConsent.Values[0] then begin
    SuppressibleMsgBox('Для полной установки необходимо согласие на установку драйверов и сертификата. Без согласия можно отменить установку.', mbInformation, MB_OK, IDOK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
  Arguments: String;
begin
  Result := '';
  if DriversInstalled then exit;
  if not DriverConsent.Values[0] then begin
    Result := 'Нет согласия на установку драйверов и сертификата.';
    exit;
  end;
  ExtractTemporaryFiles('{tmp}\gpdt-prerequisites\*');
  Arguments := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\gpdt-prerequisites\scripts\install-dependencies.ps1') +
    '" -TrustCertificate -LogPath "' + ExpandConstant('{localappdata}\gpdt-driver-setup.log') + '"';
  WizardForm.StatusLabel.Caption := 'Проверка устройства и установка драйверов…';
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := 'Не удалось запустить установку драйверов.';
    exit;
  end;
  if (ExitCode <> 0) and (ExitCode <> 3010) then begin
    Result := 'Драйверы не установлены. Проверьте модель устройства и закройте gpdt через трей. Подробности: ' + ExpandConstant('{localappdata}\gpdt-driver-setup.log');
    exit;
  end;
  DriversInstalled := True;
  DriverRestart := ExitCode = 3010;
end;

function NeedRestart: Boolean;
begin
  Result := DriverRestart;
end;

function CanLaunch: Boolean;
begin
  Result := DriversInstalled and not DriverRestart;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  Arguments: String;
begin
  if CurStep = ssPostInstall then begin
    if WizardIsTaskSelected('autostart') then
      Arguments := '--install-startup'
    else
      Arguments := '--remove-startup';
    if not Exec(ExpandConstant('{app}\gpdt.exe'), Arguments, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('Не удалось настроить автозапуск. Откройте gpdt → Ещё → Настройки запуска.');
    if ExitCode <> 0 then
      RaiseException('Ошибка настройки автозапуска. Откройте gpdt → Ещё → Настройки запуска.');
  end;
end;
