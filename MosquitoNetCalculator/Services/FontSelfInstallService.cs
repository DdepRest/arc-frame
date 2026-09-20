using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Страховка типографики после обновления: запущенное приложение само
    /// убеждается, что семейство Inter есть на устройстве, и при отсутствии
    /// ставит его per-user из файлов, вшитых в приложение
    /// (<c>Resources/Fonts/*.ttf</c>, <c>&lt;Resource&gt;</c>). Без прав
    /// администратора и без участия установщика — поэтому после
    /// автообновления ни у кого шрифт не «отваливается», даже если какой-то
    /// сторонний деинсталлятор почистил папку Fonts.
    ///
    /// <para><b>Почему это страховка, а не основной механизм.</b> Интерфейс
    /// берёт шрифт не из системной установки, а из токена <c>Font.Text</c>,
    /// который <see cref="AppFontService"/> создаёт напрямую из сборки
    /// (pack-URI). Приложение поэтому рисует Inter всегда, даже если установки
    /// нет. Per-user установка нужна для всего ОСТАЛЬНОГО, где вшитую семью
    /// подставить нельзя: печатное КП (QuestPDF/FlowDocument ищут семью по
    /// имени в системе), диалоги печати Windows и сторонние просмотрщики
    /// экспортированных документов.</para>
    ///
    /// <para><b>Как ставится (per-user, без admin).</b> Пара
    /// <see cref="Native.AddFontResource"/>(файл) + ключ
    /// <c>HKCU\...\Fonts</c> «имя → полный путь». Это документированный
    /// Windows-способ установки шрифта текущему пользователю: он виден всем
    /// новым процессам пользователя немедленно, текущему процессу — после
    /// <see cref="Native.SendMessageTimeout"/> с <c>WM_FONTCHANGE</c>. Права
    /// администратора не нужны: HKCU и профиль пользователя доступны и так.</para>
    ///
    /// <para><b>Идемпотентность.</b> Каждый запуск: проверить семью → если
    /// есть, ничего не делать. Если нет — поставить и записать версию
    /// комплекта в settings (<c>InstalledFontVersion</c>), чтобы при смене
    /// состава файлов в будущем можно было переустановить принудительно.</para>
    ///
    /// <para><b>Отказы глотаются</b> (файловая система только для чтения,
    /// реестр под политикой и т.п.): интерфейс всё равно рисует вшитый Inter,
    /// а попытка повторится при следующем запуске. Метод возвращает сводку —
    /// она попадает в диагностику, но не в UI: молчаливая фоновая починка,
    /// а не повод для тостов.</para>
    /// </summary>
    internal static class FontSelfInstallService
    {
        /// <summary>Семейство, которое обязано быть на устройстве.</summary>
        internal const string FamilyName = "Inter";

        /// <summary>
        /// Версия комплекта файлов: меняется при пересоставе вшитых ttf.
        /// Позволяет будущим версиям приложения переустановить шрифт, даже
        /// если семейство с тем же именем уже есть (устаревшего состава).
        /// </summary>
        internal const string BundleVersion = "1.0";

        /// <summary>Файлы комплекта в порядке установки (обычный → курсив).</summary>
        internal static readonly string[] BundleFiles =
        {
            "Inter-Regular.ttf",
            "Inter-Italic.ttf",
            "Inter-Medium.ttf",
            "Inter-SemiBold.ttf",
            "Inter-Bold.ttf",
        };

        /// <summary>
        /// Подключаемая проверка наличия семьи (для тестов): по умолчанию —
        /// перечисление установленных семей GDI.
        /// </summary>
        internal static Func<string, bool> FamilyInstalled = IsFamilyInstalledGdi;

        /// <summary>
        /// Куда копируются файлы шрифта: отдельная подпапка на версию комплекта.
        /// Отдельно от exe — папка приложения заменяется при обновлении, а
        /// установленному шрифту нельзя «уехать» из-под системы. Отдельно по
        /// версии — GDI держит файлы ЗАРЕГИСТРИРОВАННЫМ шрифтов открытыми, и
        /// перезаписать их в том же каталоге не выйдет (замер: IOException
        /// «being used by another process» на второй установке). Старая
        /// версия остаётся рядом — это безвредно: ключ реестра перезаписывается
        /// на новый путь, файлы прошлой версии просто больше не используются.
        /// </summary>
        internal static string InstallDir { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MosquitoNetCalculator",
            "fonts",
            BundleVersion);

        /// <summary>
        /// Извлекает вшитый файл из сборки. Файлы объявлены
        /// <c>&lt;Resource Include="Resources\\Fonts\\*.ttf"/&gt;</c> — это WPF
        /// Resource: он компилируется в <c>.g.resources</c> сборки (в single-file
        /// publish — в бандл) и НЕ копируется рядом с exe, поэтому путь на диске
        /// не сработал бы. Имя ключа — путь файла строчными буквами
        /// («resources/fonts/inter-regular.ttf»), замер по собранной dll.
        /// </summary>
        internal static Func<string, byte[]> BundledBytes = fileName =>
        {
            var asm = typeof(FontSelfInstallService).Assembly;
            var key = "resources/fonts/" + fileName.ToLowerInvariant();
            var manifest = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(".g.resources", StringComparison.Ordinal))
                ?? throw new FileNotFoundException("В сборке нет .g.resources");
            using var stream = asm.GetManifestResourceStream(manifest);
            using var reader = new System.Resources.ResourceReader(stream!);
            foreach (var entry in reader.Cast<System.Collections.DictionaryEntry>())
            {
                if (!string.Equals((string)entry.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // Значение WPF Resource для ttf — Stream (типизированный);
                // в некоторых конфигурациях — byte[]. Поддерживаем оба.
                if (entry.Value is Stream s)
                {
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
                if (entry.Value is byte[] b)
                {
                    return b;
                }
            }
            throw new FileNotFoundException($"Вшитый шрифт не найден: {key}");
        };

        private sealed class Native
        {
            [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
            internal static extern int AddFontResource(string lpFileName);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            internal static extern IntPtr SendMessageTimeout(
                IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
                uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

            internal const uint HWND_BROADCAST = 0xFFFF;
            internal const uint WM_FONTCHANGE = 0x001D;
            internal const uint SMTO_ABORTIFHUNG = 0x0002;
        }

        /// <summary>Имя под ключом реестра: «Имя (Стиль) (файл)».</summary>
        internal static string RegistryValueName(string fileName) =>
            $"{FamilyName} (TrueType)" +
            (fileName == "Inter-Regular.ttf" ? "" : $" {StyleWord(fileName)}") +
            $" ({fileName})";

        private static string StyleWord(string fileName) => fileName switch
        {
            "Inter-Italic.ttf" => "Italic",
            "Inter-Medium.ttf" => "Medium",
            "Inter-SemiBold.ttf" => "SemiBold",
            "Inter-Bold.ttf" => "Bold",
            _ => "",
        };

        /// <summary>
        /// Ключ per-user шрифтов. AddFontResource сам пишет сюда только в
        /// старых Windows; для надёжности пишем сами (так делает и
        /// установщик per-user шрифтов Windows 10 1809+).
        /// </summary>
        internal static string RegistryKeyPath =>
            @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";

        /// <summary>
        /// Точка доступа к реестру (для тестов): по умолчанию — настоящий HKCU.
        /// Тесты подменяют на временный куст, чтобы не оставлять значения в
        /// реальном профиле — иначе остаётся битый ключ, указывающий на
        /// удалённую тестовую папку (реальный дефект первого прогона тестов).
        /// Вызывается НА КАЖДЫЙ файл и обязана возвращать НОВЫЙ открытый ключ:
        /// вызов обёрнут в using — если подсунуть один и тот же объект, второй
        /// файл получит «closed registry key» (ловилось тестом).
        /// </summary>
        internal static Func<RegistryKey> FontsKeyOpener { get; set; } =
            () => Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);

        /// <summary>Флаг «установка шла в подменный реестр» — диагностика тестов.</summary>
        internal static bool LastInstallWasFake { get; private set; }

        /// <summary>Сброс флага — для тестов.</summary>
        internal static void ResetFakeFlag() => LastInstallWasFake = false;

        /// <summary>
        /// Главная точка входа: вернуть true, если семейство есть (или его
        /// удалось поставить). Все отказы внутрь: исключение здесь не должно
        /// влиять на старт приложения.
        /// </summary>
        internal static bool EnsureInstalled()
        {
            try
            {
                if (FamilyInstalled(FamilyName))
                {
                    // Семья есть, но комплект мог устареть: если в settings
                    // записана ДРУГАЯ версия комплекта (например, машина
                    // ставила старый состав файлов), переустанавливаем —
                    // иначе ключ реестра навсегда указывает на прошлый путь.
                    // Пустой записи нет доверия: шрифт ставили не мы — не трогаем.
                    var saved = AppSettingsService.LoadInstalledFontVersion();
                    if (string.IsNullOrEmpty(saved) ||
                        string.Equals(saved, BundleVersion, StringComparison.Ordinal))
                    {
                        return true;
                    }
                    return InstallBundle();
                }

                return InstallBundle();
            }
            catch
            {
                // Шрифт — страховка, не критический путь старта.
                return false;
            }
        }

        /// <summary>Перечисление семей через GDI — как их видит печать.</summary>
        private static bool IsFamilyInstalledGdi(string family)
        {
            try
            {
                using var families = new System.Drawing.Text.InstalledFontCollection();
                return families.Families.Any(f =>
                    string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Копия комплекта в профиль + AddFontResource + HKCU-ключ.</summary>
        internal static bool InstallBundle()
        {
            Directory.CreateDirectory(InstallDir);

            var installed = 0;
            foreach (var fileName in BundleFiles)
            {
                byte[] bytes;
                try
                {
                    bytes = BundledBytes(fileName);
                }
                catch
                {
                    continue; // файла нет в комплекте — ставим что есть
                }

                try
                {
                    var dst = Path.Combine(InstallDir, fileName);
                    WriteWithRetry(bytes, dst);

                    // Регистрация: AddFontResource в сессии + ключ для будущих сессий.
                    Native.AddFontResource(dst);
                    using (var key = FontsKeyOpener())
                    {
                        key.SetValue(RegistryValueName(fileName), dst, RegistryValueKind.String);
                        LastInstallWasFake = key.Name != $"HKEY_CURRENT_USER\\{RegistryKeyPath}";
                    }
                    installed++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FontSelfInstall] {fileName}: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }

            if (installed == 0)
            {
                return false;
            }

            // Текущий процесс (и соседи) узнают о новом шрифте без перезапуска.
            Native.SendMessageTimeout(
                (IntPtr)Native.HWND_BROADCAST, Native.WM_FONTCHANGE,
                IntPtr.Zero, IntPtr.Zero,
                Native.SMTO_ABORTIFHUNG, 1000, out _);

            // Версия комплекта: маркер для будущей принудительной переустановки.
            AppSettingsService.SaveInstalledFontVersion(BundleVersion);
            return true;
        }

        private static void WriteWithRetry(byte[] bytes, string dst, int attempts = 5)
        {
            for (var i = 1; ; i++)
            {
                try
                {
                    // Перезаписываем: файл мог остаться от прошлой попытки битым.
                    File.WriteAllBytes(dst, bytes);
                    return;
                }
                // IOException — обычная блокировка чтением; UnauthorizedAccess —
                // держит сам GDI: AddFontResource ПРЕДЫДУЩЕГО файла не отпускает
                // каталог мгновенно (замер в тесте: на третьем файле падало).
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (i >= attempts) throw;
                    System.Threading.Thread.Sleep(120 * i);
                }
            }
        }

        /// <summary>
        /// Есть ли семья в системе с точки зрения GDI (публично для тестов).
        /// </summary>
        internal static bool IsFamilyInstalled() => FamilyInstalled(FamilyName);
    }
}
