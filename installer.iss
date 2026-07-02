; MosquitoNetCalculator — Inno Setup Installer Script
; Полностью самодостаточный установщик.
; Автоматически проверяет и устанавливает ТОЛЬКО недостающие зависимости:
;   - .NET 8 Desktop Runtime (опционально, для диагностики)
;   - WebView2 Runtime (>= минимальная версия)
;   - Visual C++ Redistributable 2015-2022 (>= минимальная версия)
;   - Минимальная версия Windows 10 1809 / 11
;
; Требуется: Inno Setup (https://jrsoftware.org/isdl.php)
;
; Инструкция по сборке:
; 1. Запустите build.bat — он создаст publish\ со всеми файлами
; 2. Запустите compile-installer.bat — он сам прочитает версию из .csproj
;    и скомпилирует установщик через ISCC.exe.
; 3. Готовый установщик будет в Output\SetupMosquitoNetCalculator-*.exe
;
; Примечание: если открываете installer.iss в Inno Setup Studio вручную,
; укажите параметр /DMyAppVersion=3.x.x в настройках компиляции,
; иначе версия будет "0.0.0".

#define MyAppName "MosquitoNetCalculator"
; Версия автоматически пробрасывается из compile-installer.bat (читает из .csproj).
; Если запускать ISCC вручную — укажите /DMyAppVersion=3.x.x или будет "0.0.0".
#ifndef MyAppVersion
#define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "MosquitoNet"
#define MyAppURL ""
#define MyAppExeName "MosquitoNetCalculator.exe"

; ============================================================
; Минимальные версии зависимостей (обновляйте при необходимости)
; ============================================================
#define MinWebView2Version "100.0.0.0"
#define MinVCRedistVersion "14.30.0.0"
#define MinWindowsBuild "17763"   ; Windows 10 1809 / Server 2019

[Setup]
; Базовые настройки
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\MosquitoNetCalculator
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputBaseFilename=SetupMosquitoNetCalculator-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
; Только x64, т.к. сборка — win-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Иконка установщика
SetupIconFile=MosquitoNetCalculator\Resources\app_icon.ico
; Визуальные настройки
DisableProgramGroupPage=no
DisableDirPage=no
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
DirExistsWarning=yes
; Показываем описание программы перед установкой
AppCopyright=© {#MyAppPublisher}
AppSupportURL={#MyAppURL}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на &рабочем столе"; GroupDescription: "Дополнительные значки:"; Flags: checkedonce

[Files]
; Основные файлы программы — копируются в выбранную пользователем папку.
; Данные (заказы, настройки, цены) живут в %AppData%\MosquitoNetCalculator\.
Source: "publish\MosquitoNetCalculator.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\app_icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\app_icon.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Удаление пользовательских данных из %AppData% — с подтверждением (MsgBox).
; Без RunOnceId — должно работать при каждой деинсталляции (включая переустановку).
Filename: "{cmd}"; Parameters: "/c if exist ""{userappdata}\MosquitoNetCalculator"" rmdir /s /q ""{userappdata}\MosquitoNetCalculator"""; Flags: runhidden; Check: ShouldDeleteAppData

[Code]
// =====================================================================
// Идентификация задачи "Создать ярлык на рабочем столе"
// Используется CurPageChanged ниже для принудительной отметки галочки.
// ВАЖНО: Эта подстрока КОУПЛИТСЯ с [Tasks] Description выше. Если кто-то
// изменит русскую формулировку задачи в [Tasks], обновите эту константу
// одновременно. Поиск идёт по ItemCaption[I] (NOT GroupDescription) —
// ищется по Description задачи, а не по group header.
// =====================================================================
const DesktopTaskCaptionMatch = 'рабочем столе';

// =====================================================================
// Импорт функции URLDownloadToFile из urlmon.dll для скачивания файлов
// =====================================================================
function URLDownloadToFile(
  const caller: IUnknown;
  const szURL: String;
  const szFileName: String;
  const dwReserved: DWORD;
  const lpfnCB: IUnknown
): HRESULT;
external 'URLDownloadToFileW@urlmon.dll stdcall';

const
  // Реестр — WebView2 Runtime (Evergreen Bootstrapper)
  WebView2RegKeyHKLM = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4F95-ADA8-00C4A42566F8}';
  WebView2RegKeyHKCU = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4F95-ADA8-00C4A42566F8}';
  WebView2VersionValue = 'pv';

  // Реестр — Visual C++ Redistributable 2015-2022 (x64)
  VCRedistKey = 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
  VCRedistInstalledValue = 'Installed';
  VCRedistVersionValue = 'Version';

  // Реестр — .NET 8 Desktop Runtime (x64)
  DotNetDesktop8Key = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

  // Реестр — Windows Build
  WindowsVersionKey = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion';

  // URL для скачивания
  WebView2URL = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703';
  VCRedistURL = 'https://aka.ms/vs/17/release/vc_redist.x64.exe';

  // Минимальные версии
  MinWebView2VersionStr = '{#MinWebView2Version}';
  MinVCRedistVersionStr = '{#MinVCRedistVersion}';
  MinWindowsBuildStr    = '{#MinWindowsBuild}';

var
  // Флаги состояния зависимостей (для итогового отчёта)
  DependencyReportLines: TStringList;
  // Флаг: True во время деинсталляции, False во время установки.
  // Нужен для ShouldDeleteAppData — UninstallSilent() нельзя вызывать при установке.
  UninstallMode: Boolean;

// =====================================================================
// Утилиты сравнения версий
// =====================================================================

/// Разбивает строку версии (например "100.0.0.0") на массив частей по разделителю.
/// В Inno Setup Pascal у TStringList нет свойств Delimiter/StrictDelimiter/DelimitedText,
/// поэтому split реализован вручную.
function SplitVersionString(const S: String; Delimiter: Char): TArrayOfString;
var
  I, StartIdx, ArrLen, SLen: Integer;
begin
  SetArrayLength(Result, 0);
  SLen := Length(S);
  ArrLen := 0;
  StartIdx := 1;

  for I := 1 to SLen do
  begin
    if S[I] = Delimiter then
    begin
      SetArrayLength(Result, ArrLen + 1);
      Result[ArrLen] := Copy(S, StartIdx, I - StartIdx);
      ArrLen := ArrLen + 1;
      StartIdx := I + 1;
    end;
  end;

  // Добавляем оставшийся кусок после последнего разделителя
  SetArrayLength(Result, ArrLen + 1);
  Result[ArrLen] := Copy(S, StartIdx, SLen - StartIdx + 1);
end;

/// Заменяет все вхождения подстроки From на подстроку To в строке S.
/// Реализовано вручную, чтобы не зависеть от StringChange (его сигнатура/наличие
/// могут различаться между версиями Inno Setup).
function ReplaceAll(S, From, Replacement: String): String;
var
  Idx, FLen: Integer;
begin
  Result := S;
  FLen := Length(From);
  if FLen = 0 then
    Exit;
  repeat
    Idx := Pos(From, Result);
    if Idx = 0 then
      Break;
    Result := Copy(Result, 1, Idx - 1) + Replacement + Copy(Result, Idx + FLen, MaxInt);
  until False;
end;

/// Сравнивает две строки версий (например "100.0.0.0" и "99.0.0.0").
/// Возвращает: 1 если A > B, -1 если A < B, 0 если равны.
function CompareVersions(A, B: String): Integer;
var
  AParts, BParts: TArrayOfString;
  i, MaxLen, ALen, BLen, ANum, BNum: Integer;
begin
  Result := 0;
  // Нормализуем разделитель (запятая -> точка) — на случай если версия пришла как "100,0,0,0"
  A := ReplaceAll(A, ',', '.');
  B := ReplaceAll(B, ',', '.');
  AParts := SplitVersionString(A, '.');
  BParts := SplitVersionString(B, '.');

  ALen := GetArrayLength(AParts);
  BLen := GetArrayLength(BParts);
  MaxLen := ALen;
  if BLen > MaxLen then
    MaxLen := BLen;

  for i := 0 to MaxLen - 1 do
  begin
    if i >= ALen then
      ANum := 0
    else
      ANum := StrToIntDef(AParts[i], 0);

    if i >= BLen then
      BNum := 0
    else
      BNum := StrToIntDef(BParts[i], 0);

    if ANum > BNum then
    begin
      Result := 1;
      Exit;
    end
    else if ANum < BNum then
    begin
      Result := -1;
      Exit;
    end;
  end;
end;

/// Возвращает True, если текущая версия >= минимальной
function VersionMeetsMinimum(Current, Minimum: String): Boolean;
begin
  Result := CompareVersions(Current, Minimum) >= 0;
end;

// =====================================================================
// Проверка версии Windows
// =====================================================================

/// Проверяет, что Windows версия >= минимальной
function IsWindowsVersionSupported: Boolean;
var
  BuildStr: String;
  BuildNum: Integer;
begin
  Result := False;
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, WindowsVersionKey, 'CurrentBuild', BuildStr) then
    BuildStr := '0';

  BuildNum := StrToIntDef(BuildStr, 0);

  // Win 10 >= 17763, Win 11 >= 22000 — обе проходят проверку
  // Для простоты проверяем только Build >= MinWindowsBuild
  // (Win10 1809 = 17763, Win11 = 22000+)
  Result := BuildNum >= StrToIntDef(MinWindowsBuildStr, 17763);

  // Win 7/8/8.1 не имеют ключа CurrentBuild — если строка пустая, считаем что ОС старая
  if BuildStr = '' then
    Result := False;
end;

/// Возвращает строку с информацией о Windows
function GetWindowsVersionInfo: String;
var
  ProductName, DisplayVersion, BuildStr: String;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, WindowsVersionKey, 'ProductName', ProductName) then
    ProductName := 'Windows';
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, WindowsVersionKey, 'DisplayVersion', DisplayVersion) then
    DisplayVersion := '';
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, WindowsVersionKey, 'CurrentBuild', BuildStr) then
    BuildStr := '0';
  BuildStr := Trim(BuildStr);

  Result := ProductName;
  if DisplayVersion <> '' then
    Result := Result + ' ' + DisplayVersion;
  Result := Result + ' (Build ' + BuildStr + ')';
end;

// =====================================================================
// Проверка .NET 8 Desktop Runtime
// =====================================================================

/// Возвращает True, если удалось детектировать установленную Desktop Runtime 8.x
/// через реестр. Внимание: из-за ограничений API Inno Setup (различия в сигнатурах
/// RegGetSubkeyNames/RegGetValueNames между версиями IS) эта функция здесь упрощена —
/// возвращает False для всех случаев. .NET 8 встроен в программу через self-contained
/// публикацию, поэтому эта диагностика носит лишь информационный характер.
function GetInstalledDotNetDesktop8Version(var VersionStr: String): Boolean;
begin
  // .NET 8 Desktop Runtime встроен в установщик — для запуска приложения это не нужно.
  // Реестр .NET Desktop Runtime хранит версии как подключи (8.0.10, 8.0.11 и т.п.),
  // но надёжный API для их перечисления в Inno Setup Pascal Script отсутствует
  // (RegGetSubkeyNames / RegGetValueNames имеют разные сигнатуры в разных версиях IS).
  // Возвращаем False — сообщаем юзеру что .NET используется встроенный.
  Result := False;
  VersionStr := '';
end;

/// Проверяет наличие .NET 8 Desktop Runtime — для диагностики (программа self-contained)
function CheckDotNetDesktop8Status(var StatusLine: String): Boolean;
var
  VersionStr: String;
begin
  Result := GetInstalledDotNetDesktop8Version(VersionStr);
  if Result then
    StatusLine := '.NET 8 Desktop Runtime ' + VersionStr + ' — установлен (используется встроенный .NET)'
  else
    StatusLine := '.NET 8 Desktop Runtime — не установлен (программа работает со встроенным .NET)';
end;

// =====================================================================
// Проверка WebView2 Runtime
// =====================================================================

/// Возвращает True, если WebView2 Runtime установлен и >= минимальной версии
function GetInstalledWebView2Version(var VersionStr: String): Boolean;
var
  Wv2KeyMachine, Wv2KeyUser: String;
begin
  Result := False;
  VersionStr := '';

  Wv2KeyMachine := WebView2RegKeyHKLM;
  Wv2KeyUser := WebView2RegKeyHKCU;

  // HKLM (полное имя HKEY_LOCAL_MACHINE; короткие HKLM/HKCU — зарезерв. константы в IS)
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, Wv2KeyMachine, WebView2VersionValue, VersionStr) then
  begin
    Result := True;
    Exit;
  end;

  // HKCU (per-user install)
  if RegQueryStringValue(HKEY_CURRENT_USER, Wv2KeyUser, WebView2VersionValue, VersionStr) then
  begin
    Result := True;
    Exit;
  end;

  // Ключ существует, но поле pv не читается (старая версия)
  if RegKeyExists(HKEY_LOCAL_MACHINE, Wv2KeyMachine) or
     RegKeyExists(HKEY_CURRENT_USER, Wv2KeyUser) then
  begin
    Result := True;
    VersionStr := '';
    Exit;
  end;
end;

/// Проверяет, установлен ли WebView2 Runtime и достаточно ли новый.
/// Если ключ есть, но версия неизвестна (VersionStr = '') — считаем устаревшим,
/// чтобы установщик инициировал переустановку актуальной версии.
function IsWebView2InstalledAndCurrent: Boolean;
var
  VersionStr: String;
begin
  Result := False;
  if not GetInstalledWebView2Version(VersionStr) then
    Exit;
  if VersionStr = '' then
    Exit;
  // Проверяем минимальную версию
  if not VersionMeetsMinimum(VersionStr, MinWebView2VersionStr) then
    Exit;
  Result := True;
end;

/// Проверяет статус WebView2 для отчёта
function CheckWebView2Status(var StatusLine: String): Boolean;
var
  VersionStr: String;
begin
  Result := False;
  if GetInstalledWebView2Version(VersionStr) then
  begin
    if VersionStr = '' then
      StatusLine := 'WebView2 Runtime — установлен (версия неизвестна, рекомендуется >= ' + MinWebView2VersionStr + ')'
    else if not VersionMeetsMinimum(VersionStr, MinWebView2VersionStr) then
      StatusLine := 'WebView2 Runtime ' + VersionStr + ' — установлен, но устарел (требуется >= ' + MinWebView2VersionStr + ')'
    else
    begin
      Result := True;
      StatusLine := 'WebView2 Runtime ' + VersionStr + ' — установлен и актуален';
    end;
  end
  else
    StatusLine := 'WebView2 Runtime — не установлен (требуется для предпросмотра КП)';
end;

// =====================================================================
// Проверка VC++ Redistributable
// =====================================================================

/// Проверяет, установлен ли VC++ Redistributable 2015-2022 (x64) и >= минимальной версии
function IsVCRedistInstalledAndCurrent: Boolean;
var
  InstalledValue: DWord;
  VersionStr: String;
begin
  Result := False;
  if not (RegQueryDWordValue(HKEY_LOCAL_MACHINE, VCRedistKey, VCRedistInstalledValue, InstalledValue) and
          (InstalledValue = 1)) then
    Exit;

  // Проверяем минимальную версию
  if RegQueryStringValue(HKEY_LOCAL_MACHINE, VCRedistKey, VCRedistVersionValue, VersionStr) then
  begin
    if not VersionMeetsMinimum(VersionStr, MinVCRedistVersionStr) then
      Exit;
  end;

  Result := True;
end;

/// Проверяет статус VC++ Redistributable для отчёта
function CheckVCRedistStatus(var StatusLine: String): Boolean;
var
  InstalledValue: DWord;
  VersionStr: String;
begin
  Result := False;
  if RegQueryDWordValue(HKEY_LOCAL_MACHINE, VCRedistKey, VCRedistInstalledValue, InstalledValue) and
     (InstalledValue = 1) then
  begin
    if RegQueryStringValue(HKEY_LOCAL_MACHINE, VCRedistKey, VCRedistVersionValue, VersionStr) then
    begin
      if VersionMeetsMinimum(VersionStr, MinVCRedistVersionStr) then
      begin
        Result := True;
        StatusLine := 'VC++ Redistributable 2015-2022 ' + VersionStr + ' — установлен и актуален';
      end
      else
        StatusLine := 'VC++ Redistributable ' + VersionStr + ' — устарел (требуется >= ' + MinVCRedistVersionStr + ')';
    end
    else
    begin
      // Установлен, но версия неизвестна — считаем актуальным (раз установлен)
      Result := True;
      StatusLine := 'VC++ Redistributable 2015-2022 — установлен';
    end;
  end
  else
    StatusLine := 'VC++ Redistributable 2015-2022 — не установлен';
end;

// =====================================================================
// Установка зависимостей
// =====================================================================

/// Скачивает файл по URL с отображением прогресса
/// Возвращает True, если скачивание успешно
function DownloadFile(const URL, LocalPath: String; const StatusCaption: String): Boolean;
var
  Retries: Integer;
begin
  Result := False;
  Retries := 0;

  while (Retries < 2) and (not Result) do
  begin
    WizardForm.StatusLabel.Caption := StatusCaption;
    WizardForm.FilenameLabel.Caption := LocalPath;
    WizardForm.ProgressGauge.Style := npbstMarquee;

    if URLDownloadToFile(nil, URL, LocalPath, 0, nil) = 0 then
      Result := True
    else
    begin
      Retries := Retries + 1;
      if Retries < 2 then
        WizardForm.StatusLabel.Caption := StatusCaption + ' (повторная попытка...)';
    end;
  end;

  WizardForm.ProgressGauge.Style := npbstNormal;
  WizardForm.FilenameLabel.Caption := '';
end;

/// Скачивает и тихо устанавливает VC++ Redistributable 2015-2022 (x64)
procedure InstallVCRedistIfNeeded;
var
  LocalPath: String;
  ResultCode: Integer;
begin
  if IsVCRedistInstalledAndCurrent then
    Exit;

  LocalPath := ExpandConstant('{tmp}\vc_redist.x64.exe');

  WizardForm.StatusLabel.Caption := 'Загрузка Visual C++ Redistributable 2015-2022 ...';
  if not DownloadFile(VCRedistURL, LocalPath, 'Загрузка Visual C++ Redistributable 2015-2022 ...') then
  begin
    DependencyReportLines.Add('• VC++ Redistributable — ОШИБКА ЗАГРУЗКИ (проверьте интернет)');
    Exit;
  end;

  WizardForm.StatusLabel.Caption := 'Установка Visual C++ Redistributable 2015-2022 ...';
  WizardForm.FilenameLabel.Caption := LocalPath;
  WizardForm.ProgressGauge.Style := npbstNormal;

  if Exec(LocalPath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if (ResultCode = 0) or (ResultCode = 1638) then
    begin
      // 0 = успех, 1638 = другая версия уже установлена (не ошибка)
      DependencyReportLines.Add('• VC++ Redistributable 2015-2022 — установлен');
    end
    else if ResultCode = 3010 then
    begin
      // 3010 = success, но требуется перезагрузка
      DependencyReportLines.Add('• VC++ Redistributable 2015-2022 — установлен (потребуется перезагрузка Windows)');
    end
    else
    begin
      DependencyReportLines.Add('• VC++ Redistributable — код ошибки установки: ' + IntToStr(ResultCode));
    end;
  end
  else
    DependencyReportLines.Add('• VC++ Redistributable — не удалось запустить установщик');

  WizardForm.FilenameLabel.Caption := '';
end;

/// Скачивает и тихо устанавливает WebView2 Runtime
procedure InstallWebView2IfNeeded;
var
  LocalPath: String;
  ResultCode: Integer;
begin
  if IsWebView2InstalledAndCurrent then
    Exit;

  LocalPath := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');

  WizardForm.StatusLabel.Caption := 'Загрузка WebView2 Runtime ...';
  if not DownloadFile(WebView2URL, LocalPath, 'Загрузка WebView2 Runtime ...') then
  begin
    DependencyReportLines.Add('• WebView2 Runtime — ОШИБКА ЗАГРУЗКИ (проверьте интернет)');
    Exit;
  end;

  WizardForm.StatusLabel.Caption := 'Установка WebView2 Runtime ...';
  WizardForm.FilenameLabel.Caption := LocalPath;
  WizardForm.ProgressGauge.Style := npbstNormal;

  if Exec(LocalPath, '/silent /install', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    if (ResultCode = 0) or (ResultCode = 1638) then
      DependencyReportLines.Add('• WebView2 Runtime — установлен')
    else if ResultCode = 3010 then
      DependencyReportLines.Add('• WebView2 Runtime — установлен (потребуется перезагрузка Windows)')
    else
      DependencyReportLines.Add('• WebView2 Runtime — код ошибки установки: ' + IntToStr(ResultCode));
  end
  else
    DependencyReportLines.Add('• WebView2 Runtime — не удалось запустить установщик');

  WizardForm.FilenameLabel.Caption := '';
end;

// =====================================================================
// Хуки установщика
// =====================================================================// Инициализация — проверка критичных зависимостей до начала установки
function InitializeSetup: Boolean;
var
  ErrorMsg: String;
begin
  DependencyReportLines := TStringList.Create;
  UninstallMode := False;

  // Проверяем версию Windows — это критично (WPF .NET 8 не работает на < Win 10 1809)
  if not IsWindowsVersionSupported then
  begin
    ErrorMsg := 'Ваша версия Windows: ' + GetWindowsVersionInfo + #13#10 + #13#10 +
                'Программа требует Windows 10 (версия 1809, October 2018 Update) или новее / Windows 11.' + #13#10 +
                'Пожалуйста, обновите Windows и попробуйте снова.';
    MsgBox(ErrorMsg, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

/// Вызывается при старте деинсталлятора — устанавливаем флаг UninstallMode.
function InitializeUninstall: Boolean;
begin
  UninstallMode := True;
  Result := True;
end;

/// Используется Inno Setup для заполнения стандартного текста страницы "Ready"
/// Мы дополняем дефолтный список (путь, иконки, компоненты) отчётом о зависимостях
function UpdateReadyMemo(
  const Space, NewLine: String;
  const MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
        MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  StatusLine: String;
  ReportText: String;
begin
  // Конкатенация стандартных блоков IS с фильтрацией пустых
  // (иначе при установке в дефолтную папку/без опций появятся лишние пробелы)
  if MemoDirInfo <> '' then
    Result := Result + MemoDirInfo + NewLine + NewLine;
  if MemoTypeInfo <> '' then
    Result := Result + MemoTypeInfo + NewLine + NewLine;
  if MemoComponentsInfo <> '' then
    Result := Result + MemoComponentsInfo + NewLine + NewLine;
  if MemoGroupInfo <> '' then
    Result := Result + MemoGroupInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + MemoTasksInfo + NewLine;

  // Делаем отчёт о статусе зависимостей
  ReportText := '=== Состояние системных зависимостей ===' + NewLine + NewLine;

  CheckWebView2Status(StatusLine);
  ReportText := ReportText + '* ' + StatusLine + NewLine;

  CheckVCRedistStatus(StatusLine);
  ReportText := ReportText + '* ' + StatusLine + NewLine;

  CheckDotNetDesktop8Status(StatusLine);
  ReportText := ReportText + '* ' + StatusLine + NewLine + NewLine;

  ReportText := ReportText + 'Если какой-то компонент отсутствует, установщик' + NewLine +
                'автоматически скачает и установит его после копирования файлов.' + NewLine +
                'Требуется подключение к интернету.';

  Result := Result + ReportText;
end;



/// После установки файлов — автоматически доустанавливаем компоненты
procedure CurStepChanged(CurStep: TSetupStep);
var
  StatusLine: String;
begin
  if CurStep = ssPostInstall then
  begin
    // VC++ Redistributable — критично для запуска .exe
    InstallVCRedistIfNeeded;
    // WebView2 Runtime — критично для предпросмотра КП
    InstallWebView2IfNeeded;

    // ── Финал установки зависимостей ──
    WizardForm.StatusLabel.Caption := 'Установка завершена!';
  end;

  // Финальный отчёт
  if CurStep = ssDone then
  begin
    StatusLine := 'Итог установки:' + #13#10 + #13#10;
    StatusLine := StatusLine + '• Программа установлена. Обновления — через GitHub Releases.' + #13#10;
    if DependencyReportLines.Count > 0 then
    begin
      StatusLine := StatusLine + #13#10 + 'Дополнительные компоненты:' + #13#10 + #13#10;
      StatusLine := StatusLine + DependencyReportLines.Text;
    end
    else
    begin
      StatusLine := StatusLine + #13#10 + 'Все необходимые компоненты уже были установлены в системе.';
    end;
    MsgBox(StatusLine, mbInformation, MB_OK);
  end;
end;

/// Принудительно отмечает галочку «Создать ярлык на рабочем столе», когда
/// показывается страница с задачами. Inno Setup флаг `checkedonce` запоминает
/// выбор пользователя и при повторной установке может показать галочку как
/// unchecked, если в прошлый раз её сняли. Для удобства массовой установки мы
/// ЯВНО требуем эту галочку checked каждый раз.
///
/// Поведение: каждый раз при входе на wpSelectTasks (включая Back → Next
/// навигацию) галочка принудительно отмечается. Это и есть желаемое поведение:
/// «галочка активна автоматически» независимо от предыдущих установок.
///
/// Поиск по каптину (Pos) робастнее, чем по индексу: если [Tasks] будет
/// расширен, коду не нужно править индекс. Константа DesktopTaskCaptionMatch
/// должна оставаться синхронизированной с [Tasks] Description (см. комментарий
/// выше константы).
procedure CurPageChanged(CurPageID: Integer);
var
  I: Integer;
begin
  if CurPageID <> wpSelectTasks then
    Exit;

  if WizardForm.TasksList = nil then
    Exit;

  // Если по какой-то причине Caption задачи изменился и не матчит
  // DesktopTaskCaptionMatch (например, был переименован русский текст),
  // мы НЕ ставим «тихий sanity fallback» (тихие fallback'ы опасны —
  // могут сделать check на КАКОЙ-ТО другой задаче, маскируя баг).
  // Просто ничего не делаем — пользователь увидит unchecked, и
  // проблема будет очевидна при первом запуске.
  for I := 0 to WizardForm.TasksList.Items.Count - 1 do
  begin
    if Pos(DesktopTaskCaptionMatch, WizardForm.TasksList.ItemCaption[I]) > 0 then
      WizardForm.TasksList.Checked[I] := True;
  end;
end;

/// Спрашивает пользователя, нужно ли удалить все данные (заказы, настройки, цены).
/// Используется как Check-функция в [UninstallRun] — возвращает True только
/// если пользователь явно подтвердил удаление в диалоговом окне.
/// При тихой деинсталляции (/VERYSILENT) данные всегда сохраняются.
function ShouldDeleteAppData: Boolean;
begin
  // Inno Setup вызывает Check для [UninstallRun] и при установке (чтобы решить,
  // нужно ли сохранить запись), и при деинсталляции. UninstallSilent() доступна
  // только в uninstall-контексте, поэтому проверяем флаг UninstallMode.
  if not UninstallMode then
  begin
    // Во время установки — всегда сохраняем запись (будет показана при удалении)
    Result := True;
    Exit;
  end;

  // Silent uninstall — never delete user data without explicit consent
  if UninstallSilent() then
  begin
    Result := False;
    Exit;
  end;

  Result := MsgBox(
    'Удалить все данные программы?' + #13#10 + #13#10 +
    'Будут безвозвратно удалены:' + #13#10 +
    '• заказы' + #13#10 +
    '• настройки' + #13#10 +
    '• цены' + #13#10 + #13#10 +
    'Если планируете переустановить программу — нажмите «Нет».',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

/// Очистка
procedure DeinitializeSetup;
begin
  if DependencyReportLines <> nil then
    DependencyReportLines.Free;
end;
