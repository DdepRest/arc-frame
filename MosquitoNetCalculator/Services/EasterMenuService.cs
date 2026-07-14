using System.Windows;
using MosquitoNetCalculator.Controls;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// EASTER-EGG v3.43.2.9 — шуточная «PRO подписка» для меню Откосы со СТРОГИМ циклом.
    ///
    /// ══════════════════════════════════════════════════════════════════
    ///  УДАЛЕНИЕ EASTER EGG (когда шутка надоест) — чеклист из 6 шагов:
    /// ══════════════════════════════════════════════════════════════════
    ///  1) Удалить этот файл: Services/EasterMenuService.cs
    ///  2) Удалить Controls/EasterProUpsellWindow.xaml + .xaml.cs
    ///  3) Удалить поле Settings.SlopesProUpsellUnlocked в AppSettingsService.cs
    ///     + пару методов IsSlopesProUpsellUnlocked/MarkSlopesProUpsellUnlocked
    ///  4) Убрать вызов EasterMenuService.ShowSlopesProUpsellIfNotUnlocked(this)
    ///     из MainWindow.xaml.cs::NavSlope_Click (одна строка + коммент)
    ///  5) Вернуть Text="Откосы" в MainWindow.xaml NavLabelSlope
    ///     (заменить "Авто · Откосы" обратно на "Откосы")
    ///  6) (опционально) удалить CHANGELOG.md пункт «Easter-egg PRO upsell».
    ///
    /// После удаления ключ «SlopesProUpsellUnlocked» останется в
    /// settings.json у пользователей — JsonSerializer его просто
    /// проигнорирует, settings останутся валидными.
    ///
    /// Поиск touch-сайтов для grep:
    ///   grep -rn 'EasterMenuService\|EasterProUpsellWindow\|SlopesProUpsellUnlocked\|"Авто · Откосы"' .
    /// </summary>
    public static class EasterMenuService
    {
        /// <summary>
        /// Показывает шуточный PRO-апселл, пока пользователь явно не «Оплатит».
        /// СТРОГИЙ режим: пока флаг <c>SlopesProUpsellUnlocked=false</c>, диалог
        /// показывается на КАЖДЫЙ клик по «Откосы». Только явное нажатие
        /// «Оплатить» → шутка → OK помечает флаг (Mark-after-Pay discipline).
        ///
        /// Возвращает РЕШЕНИЕ пользователя — caller ОБЯЗАН его уважать:
        ///   • true  — пользователь нажал «Оплатить» → шутка → OK (флаг «unlocked»
        ///             выставлен внутри, диалог больше не показывается).
        ///             <see cref="MainWindow.NavSlope_Click"/> открывает SlopeOverlay.
        ///   • false — пользователь нажал «Отклонить», X (любое состояние окна)
        ///             или нажал ESC. Флаг НЕ выставлен.
        ///             <see cref="MainWindow.NavSlope_Click"/> НЕ открывает SlopeOverlay
        ///             (per user request — «последующие разы тоже выпадает эта меню,
        ///             пока не нажмётся Оплатить»).
        ///   • true  — уже разблокировано ранее (IsSlopesProUpsellUnlocked()=true).
        ///             Повторный визит: диалог НЕ показывается, просто возвращаем true,
        ///             caller открывает SlopeOverlay штатно.
        ///
        /// То есть состояние шутки полностью описывается флагом «unlocked»:
        ///   • unlocked=true  → диалог отсутствует, slope открывается сразу.
        ///   • unlocked=false → диалог показывается; Pay → mark → slope открыт;
        ///     Decline/X/ESC → flag остаётся false, на следующем клике диалог снова.
        ///
        /// Вызывать из UI thread (из nav-menu Click handler).
        ///
        /// Допустимый owner может быть null — тогда окно центрируется по экрану.
        /// </summary>
        /// <returns>
        /// true = «unlocked» (slopes открыть — либо юзер только что оплатил,
        /// либо уже был оплачен ранее).
        /// false = decline / X / ESC (slopes НЕ открывать, диалог появится снова).
        /// </returns>
        public static bool ShowSlopesProUpsellIfNotUnlocked(Window? owner)
        {
            // 1. Short-circuit для повторных визитов от уже-оплативших.
            //    Без диалога — mainwindow просто открывает slope.
            if (AppSettingsService.IsSlopesProUpsellUnlocked())
                return true;

            var dialog = new EasterProUpsellWindow
            {
                Owner = owner,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen
            };
            dialog.ShowDialog();

            // v3.43.2.9: STRICT-LOOP + Mark-after-Pay.
            // ShowDialog() возвращает nullable bool:
            //   • true → BtnContinue (joke OK) → DialogResult=true
            //   • false → BtnDecline / BtnClose / ESC (IsCancel) → DialogResult=false
            //   • null → диалог не был закрыт через DialogResult (крайне редко;
            //     защищаемся явным `?? false`, чтобы случайный null не открыл
            //     Откосы без явного согласия пользователя).
            bool userChoseToPay = dialog.DialogResult ?? false;

            if (userChoseToPay)
            {
                // Mark-after-Pay: флаг выставляется ТОЛЬКО после явного
                // «Оплатить» → шутка → OK. До этого момента (decline / X / ESC)
                // пользователь должен каждый раз проходить шутку заново —
                // per user requirement: «последующие разы тоже выпадает эта меню,
                // пока не нажмётся Оплатить».
                AppSettingsService.MarkSlopesProUpsellUnlocked();
                return true;
            }

            // Decline / X / ESC — флаг остаётся false. На следующем клике
            // пользователь снова увидит диалог. Это и есть «строгий цикл».
            return false;
        }
    }
}
