using System;
using System.Linq;
using System.Windows;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Показ «Что нового» после обновления. При старте сравнивает версию,
    /// для которой пользователь уже видел список изменений
    /// (<see cref="AppSettingsService.LoadLastSeenVersion"/>), с текущей
    /// версией приложения. Если программа обновилась — показывает окно
    /// с записями changelog новее последней виденной версии.
    /// Первый запуск без сохранённой версии фиксирует текущую молча —
    /// окно появится при следующем обновлении.
    /// </summary>
    public static class WhatsNewService
    {
        /// <summary>
        /// True, когда приложение обновилось относительно последней виденной
        /// версии (сохранённая версия парсится и строго меньше текущей).
        /// </summary>
        public static bool ShouldShow(Version currentVersion, string? lastSeenVersion)
        {
            var lastSeen = UpdateService.ParseSafe(lastSeenVersion);
            return lastSeen != null && lastSeen < currentVersion;
        }

        /// <summary>
        /// Записи changelog новее последней виденной версии, от новых к старым.
        /// Пустой массив, когда показывать нечего (нет сохранённой версии,
        /// версия не изменилась или запись не парсится).
        /// </summary>
        public static UpdateItem[] GetChanges(Version currentVersion, string? lastSeenVersion)
        {
            if (!ShouldShow(currentVersion, lastSeenVersion)) return Array.Empty<UpdateItem>();
            var lastSeen = UpdateService.ParseSafe(lastSeenVersion)!;
            return UpdateLog.GetChangesSince(lastSeen).Reverse().ToArray();
        }

        /// <summary>
        /// Точка входа из <c>App.OnStartup</c>: показывает окно «Что нового»,
        /// если приложение обновилось, и фиксирует текущую версию как виденную.
        /// Версия помечается виденной ДО показа окна: если окно по любой причине
        /// упадёт, следующий запуск не будет повторять сбой (иначе — бесконечный
        /// цикл падений при старте, т.к. вызов идёт в фатальном try App.OnStartup).
        /// </summary>
        public static void ShowIfNeeded(Window? owner)
        {
            var current = UpdateService.CurrentVersion;
            var changes = GetChanges(current, AppSettingsService.LoadLastSeenVersion());

            // Mark-before-show (см. выше). SaveSettings сам глотает ошибки IO.
            AppSettingsService.SaveLastSeenVersion(current.ToString());

            if (changes.Length == 0 || owner == null)
                return;

            // Декоративное окно не должно мешать старту приложения — как
            // welcome-окно в App.OnStartup: при сбое показываем ошибку,
            // но продолжаем запуск.
            try
            {
                var window = new WhatsNewWindow(current, changes) { Owner = owner };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WhatsNew] failed to show: {ex}");
            }
        }
    }
}
