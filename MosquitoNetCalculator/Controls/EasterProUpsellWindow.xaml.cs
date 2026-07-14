using System.Windows;
using System.Windows.Input;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// EASTER-EGG v3.43.2.9 — шуточное «PRO подписка» окно для меню Откосы со СТРОГИМ циклом.
    /// Один Window с двумя визуальными состояниями:
    ///   1) UpsellState — кнопки «Оплатить» (Primary) / «Отклонить» (Ghost, IsCancel)
    ///   2) JokeState   — открывается после «Оплатить», показывает «Это шутка» + OK (IsDefault)
    /// Кнопка «Оплатить» = unlock path (после шутки — OK помечает unlocked и шутка уходит).
    /// Кнопки «Отклонить» / X / ESC = decline (DialogResult=false), шутка остаётся
    /// циклиться до явного Pay→OK (strict-loop per user requirement).
    /// BtnDecline.IsCancel сбрасывается в BtnPay_Click, чтобы ESC в JokeState
    /// был no-op (WPF резолвит Cancel button на уровне Window, даже когда
    /// кнопка Collapsed).
    ///
    /// Чтобы выпилить easter egg целиком: удалить этот файл + .xaml,
    /// EasterMenuService, поле SlopesProUpsellUnlocked, hook в NavSlope_Click.
    /// </summary>
    public partial class EasterProUpsellWindow : Window
    {
        public EasterProUpsellWindow()
        {
            InitializeComponent();
        }

        private void BtnPay_Click(object sender, RoutedEventArgs e)
        {
            // Switch from upsell state to joke state — stay modal.
            UpsellState.Visibility = Visibility.Collapsed;
            JokeState.Visibility = Visibility.Visible;
            // Sync both visible chrome (TitleBarText) and OS-level Window.Title —
            // otherwise the taskbar / Alt-Tab / screen-reader announcement
            // briefly shows "Подписка PRO" while the in-app chrome shows the joke.
            TitleBarText.Text = "😄 Это шутка";
            Title = "😄 Это шутка";
            BtnContinue.Focus();

            // Tighten the dialog: joke only shows emoji + title + OK (no body
            // text). SizeToContent=Height shrinks the window to fit content,
            // so the dialog no longer looks stretched with empty middle space.
            // Default SizeToContent=Manual is restored on each fresh dialog
            // (EasterProUpsellWindow is recreated per ShowDialog call), so
            // subsequent UpsellState opens get the original 540 DIP back.
            SizeToContent = SizeToContent.Height;

            // Force immediate re-measure; without UpdateLayout the size change
            // can be deferred one frame on the first Pay click after a cold start.
            UpdateLayout();

            // v3.43.2.9: disable ESC-as-Cancel while in joke state. WPF fires
            // the Cancel button on ESC even when Visibility=Collapsed, so the
            // still-IsCancel BtnDecline would trigger DialogResult=false on
            // ESC after Pay — requiring the user to re-do the whole upsell
            // next time (sneaky strict-loop). With IsCancel cleared here,
            // ESC in JokeState is a no-op; the explicitly visible «OK» button
            // is the only unlock path. Strict-loop semantics preserved.
            BtnDecline.IsCancel = false;
        }

        private void BtnDecline_Click(object sender, RoutedEventArgs e)
        {
            // v3.43.2.9: REAL-гейт. Пользователь явно отказался от шутки —
            // DialogResult=false → MainWindow.NavSlope_Click НЕ открывает
            // SlopeOverlay. Маркер seen уже выставлен, повторно диалог
            // не покажется (пользователь сделал выбор — уважаем).
            DialogResult = false;
            Close();
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            // Joke state "OK" (после «Оплатить») — единственный путь к Откосам.
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            // v3.43.2.9: X-отмена приравнена к Decline — DialogResult=false.
            // Пользователь мог закрыть случайно, но без явного Pay
            // Откосы не открываем (per user request).
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
