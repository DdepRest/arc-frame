using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Страховка зависимостей при ПОРТАБЕЛЬНОМ запуске (exe без установки).
    /// Инсталлятор (installer.iss) сам гарантирует VC++ Redistributable
    /// 2015–2022 x64 ≥ 14.30 и ставит её при необходимости — установленным
    /// пользователям проверка не нужна и шуметь им не надо. Но exe можно
    /// запустить и без установки (скинули папку на флешке, подкинули на
    /// чистую машину): там гарантий нет, а нативные зависимости есть — QuestPDF
    /// (печать КП) и Tesseract (OCR вложений) падают в рантайме с тёмными
    /// сообщениями, по которым пользователь ничего не поймёт.
    ///
    /// <para><b>Как отличить портабельный запуск.</b> Inno Setup пишет ключ
    /// деинсталляции <c>HKLM\...\Uninstall\MosquitoNetCalculator_is1</c>
    /// (суффикс <c>_is1</c> — дефолт Inno, AppId GUID'ом не задан).
    /// Ключ есть → установлен → проверка молчит. Ключа нет → проверяем
    /// VC++ и при отсутствии показываем warning-тост с кнопкой «Скачать»
    /// (официальный aka.ms-линк). Проверка тоже честно глотает ошибки
    /// реестра: нет доступа/политика — молчим, это диагностика, а не бизнес.</para>
    ///
    /// <para><b>Почему реестр, а не «попробуй создать Process».</b> Проверка
    /// честнее и дешевле: ключ <c>VC\Runtimes\x64</c> — документированный
    /// маркер самого редистрибутива (тот же использует и инсталлятор, и
    /// check-deps.ps1), а пробные загрузки нативных библиотек дают
    /// побочные эффекты и зависят от порядка инициализации.</para>
    ///
    /// <para><b>Тестируемость.</b> Открыватели ключей инжектятся
    /// (<see cref="UninstallKeyOpener"/>, <see cref="VCRedistKeyOpener"/>),
    /// тесты подменяют их на одноразовые кусты HKCU — паттерн
    /// <c>FontSelfInstallServiceTests</c>: тест не оставляет значений в
    /// реальном реестре.</para>
    /// </summary>
    internal static class DependencyCheckerService
    {
        /// <summary>Официальная ссылка инсталлятора VC++ 2015–2022 x64.</summary>
        internal const string VCRedistDownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

        /// <summary>Ключ маркера самого редистрибутива (тот же, что в installer.iss).</summary>
        internal const string VCRedistKeyPath = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

        /// <summary>Ключ деинсталляции Inno (без GUID AppId имя = <c>&lt;AppName&gt;_is1</c>).</summary>
        internal const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MosquitoNetCalculator_is1";

        /// <summary>Открыватель ключа деинсталляции (HKLM, read). Инжект в тестах.</summary>
        internal static Func<RegistryKey?>? UninstallKeyOpener =
            () => TryOpen(Registry.LocalMachine, UninstallKeyPath);

        /// <summary>Открыватель ключа VC++ Runtimes (HKLM, read). Инжект в тестах.</summary>
        internal static Func<RegistryKey?>? VCRedistKeyOpener =
            () => TryOpen(Registry.LocalMachine, VCRedistKeyPath);

        /// <summary>
        /// Портабельный запуск без VC++ → warning-тост с кнопкой «Скачать».
        /// Установленным пользователям и машинам с VC++ — тишина. Вызывать
        /// после инициализации тостов (окно видимо), фон не блокирует.
        /// </summary>
        /// <summary>Показывать ли тост: ПОРТАБЕЛЬНЫЙ запуск И VC++ отсутствует.
        /// Установленным — тишина всегда (инсталлятор уже гарантировал);
        /// ошибки реестра = тишина (диагностика не должна шуметь).</summary>
        internal static bool NotifyIsNeeded()
        {
            try
            {
                return !IsInstalledInstall() && !IsVCRedistInstalled(out _);
            }
            catch
            {
                return false;
            }
        }

        internal static void NotifyIfMissingOnPortable()
        {
            try
            {
                if (!NotifyIsNeeded()) return;
                ToastService.ShowToast(
                    "Visual C++ Redistributable не найден — печать КП и распознавание файлов могут не работать. " +
                    "Установите его и перезапустите приложение.",
                    ToastType.Warning,
                    "Скачать VC++",
                    () => OpenUrl(VCRedistDownloadUrl),
                    durationMs: 12000);
            }
            catch
            {
                // Диагностика не должна уронить старт: любая ошибка — молча.
            }
        }

        /// <summary>Запуск установлен? (есть ключ деинсталляции Inno — проверка не нужна)</summary>
        internal static bool IsInstalledInstall()
        {
            using var key = UninstallKeyOpener?.Invoke();
            return key != null;
        }

        /// <summary>VC++ 2015–2022 x64 установлен? (Installed = 1; ошибки = false)</summary>
        internal static bool IsVCRedistInstalled(out string? version)
        {
            version = null;
            try
            {
                using var key = VCRedistKeyOpener?.Invoke();
                if (key == null) return false;
                if (key.GetValue("Installed") is not int installed || installed != 1) return false;
                version = key.GetValue("Version") as string;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static RegistryKey? TryOpen(RegistryKey hive, string path)
        {
            try { return hive.OpenSubKey(path); }
            catch { return null; }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Браузера нет/политика — не падаем: пользователь установит сам.
            }
        }
    }
}
