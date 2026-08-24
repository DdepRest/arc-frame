using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Диалог входа в админ-панель: пароль вшит в программу (одинаковый во всех
    /// офисах). Диалог сам проверяет пароль через
    /// <see cref="AppSettingsService.VerifyAdminPassword"/>: при неверном пароле —
    /// окно остаётся открытым, inline-сообщение показывает ошибку (сбрасывается
    /// при первом же вводе символа). При верном — DialogResult = true и окно закрывается.
    ///
    /// UX:
    ///   - кнопка «глаз» внутри поля — показать/скрыть пароль для верификации
    ///     (пароль вшитый — владельцу удобно);
    ///   - индикатор Caps Lock (DispatcherTimer polling 250 мс);
    ///   - inline-ошибка «Неверный пароль» / «Введите пароль»;
    ///   - Enter / Esc через PreviewKeyDown + IsDefault/IsCancel;
    ///   - Ctrl+V / Ctrl+C работают стандартно (PasswordBox/TextBox оба нативно поддерживают).
    /// </summary>
    public partial class AdminPasswordWindow : Window
    {
        /// <summary>Введённый пароль (валиден только при DialogResult == true).</summary>
        public string Password => PasswordBox.Password;

        private bool _suppressSync;
        private DispatcherTimer? _capsPoller;

        public AdminPasswordWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                PasswordBox.Focus();
                Activate();
                StartCapsLockPolling();
            };
            Closed += (_, _) => StopCapsLockPolling();
        }

        // ─────────────── Показ/скрытие пароля ───────────────

        private void BtnToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            bool show = PasswordVisibleTextBox.Visibility != Visibility.Visible;
            if (show)
            {
                PasswordVisibleTextBox.Text = PasswordBox.Password;
                PasswordVisibleTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Hidden;
                BtnToggleVisibilityIcon.Text = "\uE7B0"; // eye-off
                PasswordVisibleTextBox.Focus();
                PasswordVisibleTextBox.CaretIndex = PasswordVisibleTextBox.Text.Length;
            }
            else
            {
                PasswordBox.Password = PasswordVisibleTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordVisibleTextBox.Visibility = Visibility.Collapsed;
                BtnToggleVisibilityIcon.Text = "\uE7B3"; // eye
                PasswordBox.Focus();
            }
        }

        private void PasswordVisibleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSync) return;
            // Синхронизируем обратно при наборе (если пользователь редактирует).
            _suppressSync = true;
            PasswordBox.Password = PasswordVisibleTextBox.Text;
            _suppressSync = false;
            ClearInlineError();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressSync) return;
            _suppressSync = true;
            PasswordVisibleTextBox.Text = PasswordBox.Password;
            _suppressSync = false;
            ClearInlineError();
        }

        // ─────────────── Hotkey: Enter в полях → подтверждение ───────────────

        private void PasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrEmpty(PasswordBox.Password))
            {
                e.Handled = true;
                TryConfirm();
            }
        }

        private void PasswordVisibleTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrEmpty(PasswordVisibleTextBox.Text))
            {
                e.Handled = true;
                TryConfirm();
            }
        }

        // ─────────────── Caps Lock polling ───────────────

        private void StartCapsLockPolling()
        {
            UpdateCapsLockState();
            _capsPoller = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _capsPoller.Tick += (_, _) => UpdateCapsLockState();
            _capsPoller.Start();
        }

        private void StopCapsLockPolling()
        {
            _capsPoller?.Stop();
            _capsPoller = null;
        }

        private void UpdateCapsLockState()
        {
            try
            {
                bool caps = Keyboard.IsKeyToggled(Key.CapsLock);
                CapsLockWarning.Visibility = caps ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                CapsLockWarning.Visibility = Visibility.Collapsed;
            }
        }

        // ─────────────── Кнопки и inline-ошибка ───────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        private void OkButton_Click(object sender, RoutedEventArgs e) => TryConfirm();

        /// <summary>
        /// Проверяет пароль. При пустом — inline-сообщение «Введите пароль»,
        /// при неверном — «Неверный пароль», при верном — закрывает окно с DialogResult=true.
        /// </summary>
        private void TryConfirm()
        {
            string pwd = PasswordBox.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                ShowInlineError("Введите пароль.");
                PasswordBox.Focus();
                return;
            }
            if (!AppSettingsService.VerifyAdminPassword(pwd))
            {
                ShowInlineError("Неверный пароль");
                PasswordBox.SelectAll();
                PasswordBox.Focus();
                return;
            }
            DialogResult = true;
        }

        private void ShowInlineError(string text)
        {
            InlineError.Text = text;
            InlineError.Visibility = Visibility.Visible;
        }

        private void ClearInlineError()
        {
            if (InlineError.Visibility == Visibility.Visible)
                InlineError.Visibility = Visibility.Collapsed;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }
    }
}
