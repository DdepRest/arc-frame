using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// UserControl для ввода количества копий: TextBox по центру + кнопки ± (RepeatButton).
    /// Поддерживает hold-to-repeat, валидацию ввода (только цифры), clamp к минимуму 1.
    /// На клик/фокус — SelectAll() по полю (как в остальной программе).
    /// Подписчики получают ValueChanged (без необходимости слушать TextBox.TextChanged).
    /// </summary>
    public partial class NumericUpDownControl : UserControl
    {
        private static readonly Regex _digitsOnly = new("[^0-9]+", RegexOptions.Compiled);
        private bool _isUpdatingText;

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(int),
                typeof(NumericUpDownControl),
                new FrameworkPropertyMetadata(
                    1,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValueChanged));

        public int Value
        {
            get => (int)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Событие изменения Value — позволяет подписчикам реагировать без TextBox.</summary>
        public event RoutedPropertyChangedEventHandler<int>? ValueChanged;

        public NumericUpDownControl()
        {
            InitializeComponent();
            // 1.2: клик мышью и Tab-фокус → выделить всё число в поле (как в остальных полях программы).
            ValueTextBox.PreviewMouseLeftButtonDown += SelectAllOnMouseDown;
            ValueTextBox.GotFocus += SelectAllOnFocus;

            // п.4 (bug-fix v3.44): WPF оптимизирует SetValue и НЕ вызывает
            // PropertyChangedCallback, если присваиваемое значение совпадает
            // с metadata-default. При XAML-инициализации <NumericUpDownControl Value="1"/>
            // property-callback не вызывается (1 == metadata default), и
            // ValueTextBox.Text остаётся пустым. Форсируем синхронизацию
            // здесь и на Loaded — гарантирует, что поле всегда показывает
            // начальное значение после построения.
            UpdateTextBox(Value);
            Loaded += (_, _) => UpdateTextBox(Value);
        }

        private void SelectAllOnMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Захватываем фокус + SelectAll пока TextBox ещё не получил фокус от клика,
            // чтобы стандартный caret-режим не отменял выделение.
            DependencyObject scope = FocusManager.GetFocusScope(ValueTextBox);
            FocusManager.SetFocusedElement(scope, ValueTextBox);
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
            e.Handled = true;
        }

        private void SelectAllOnFocus(object sender, RoutedEventArgs e)
            => ValueTextBox.SelectAll();

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NumericUpDownControl c)
            {
                int newVal = (int)e.NewValue;
                c.UpdateTextBox(newVal);
                // Подписчики получают (old, new) для реакции без чтения TextBox.
                // Используем 2-арг конструктор RoutedPropertyChangedEventArgs (3-арг требует RoutedEvent, не DP).
                c.ValueChanged?.Invoke(c,
                    new RoutedPropertyChangedEventArgs<int>((int)e.OldValue, newVal));
            }
        }

        private void UpdateTextBox(int val)
        {
            if (val < 1) val = 1;
            _isUpdatingText = true;
            ValueTextBox.Text = val.ToString();
            _isUpdatingText = false;
        }

        // ── Button handlers ──────────────────────────────────────────

        private void Minus_Click(object sender, RoutedEventArgs e)
        {
            if (Value > 1)
                Value--;
        }

        private void Plus_Click(object sender, RoutedEventArgs e)
        {
            Value++;
        }

        // ── Level 1: PreviewTextInput — block non-digit characters ────

        private void ValueTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = _digitsOnly.IsMatch(e.Text);
        }

        // ── Level 2: Pasting — block paste with non-digit content ────

        private void ValueTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (_digitsOnly.IsMatch(text))
                    e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }

        // ── Level 3: LostFocus — restore last valid value if empty/invalid ──

        private void ValueTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ValueTextBox.Text) ||
                !int.TryParse(ValueTextBox.Text, out int result) ||
                result < 1)
            {
                UpdateTextBox(Value);
            }
        }

        // ── Level 4: TextChanged — update Value from text ────────────

        private void ValueTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingText) return;
            if (int.TryParse(ValueTextBox.Text, out int result) && result >= 1)
                Value = result;
        }
    }
}
