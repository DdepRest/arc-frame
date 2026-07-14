using System;
using System.Diagnostics;
using System.Windows;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Thin gate that wraps the Easter-egg "PRO subscription" joke dialog
    /// for the Slopes nav button. Extracted from MainWindow.NavSlope_Click
    /// as part of Phase 1 refactoring (REFACTORING_PLAN.md §3.2 —
    /// SlopesProUpsellGate component).
    ///
    /// ══════════════════════════════════════════════════════════════════
    ///  УДАЛЕНИЕ EASTER EGG (когда шутка надоест) — чеклист:
    /// ══════════════════════════════════════════════════════════════════
    ///  1) Удалить этот файл: Services/SlopesProUpsellGate.cs
    ///  2) Удалить Services/EasterMenuService.cs
    ///  3) Удалить Controls/EasterProUpsellWindow.xaml + .xaml.cs
    ///  4) Удалить поле Settings.SlopesProUpsellUnlocked в AppSettingsService.cs
    ///     + пару методов IsSlopesProUpsellUnlocked/MarkSlopesProUpsellUnlocked
    ///  5) В MainWindow.xaml.cs::NavSlope_Click убрать вызов
    ///     _slopesProUpsellGate.ShouldOpenSlopes(this) (одна строка + коммент)
    ///  6) Вернуть Text="Откосы" в MainWindow.xaml NavLabelSlope
    ///     (заменить "Авто · Откосы" обратно на "Откосы")
    ///
    /// После удаления ключ "SlopesProUpsellUnlocked" останется в
    /// settings.json у пользователей — JsonSerializer его просто
    /// проигнорирует, settings останутся валидными.
    ///
    /// Grep для будущего аудита:
    ///   grep -rn 'SlopesProUpsellGate\|EasterMenuService\|EasterProUpsellWindow\|SlopesProUpsellUnlocked\|"Авто · Откосы"' .
    /// </summary>
    public sealed class SlopesProUpsellGate
    {
        /// <summary>
        /// Determines whether the slope overlay should be opened.
        /// If the easter egg has been unlocked, returns true immediately.
        /// Otherwise shows the joke dialog and returns true only if the
        /// user "pays" (Pay → joke → OK).
        ///
        /// Includes a defensive try/catch: if EasterMenuService fails due
        /// to I/O issues (read-only %AppData% / file lock / corrupted
        /// settings.json) or XAML-init, the fallback opens slopes anyway
        /// with an info toast — the main workflow must not break.
        /// </summary>
        /// <param name="owner">The Window that owns the dialog (can be null).</param>
        /// <param name="onFallback">Optional callback for the graceful-fallback toast message.</param>
        /// <returns>true if slopes should open; false if the user declined.</returns>
        public bool ShouldOpenSlopes(Window? owner, Action? onFallback = null)
        {
            bool canOpenSlopes = false;

            try
            {
                canOpenSlopes = EasterMenuService.ShowSlopesProUpsellIfNotUnlocked(owner);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SlopesProUpsellGate] EasterMenuService failed (non-fatal): {ex}");
                onFallback?.Invoke();
                canOpenSlopes = true; // graceful fallback
            }

            return canOpenSlopes;
        }
    }
}
