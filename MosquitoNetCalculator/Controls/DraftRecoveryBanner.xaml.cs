using System;
using System.Windows;
using System.Windows.Controls;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator.Controls
{
    public partial class DraftRecoveryBanner : UserControl
    {
        public event EventHandler? RestoreRequested;
        public event EventHandler? SelectiveRestoreRequested;
        public event EventHandler? PostponeRequested;
        public event EventHandler? DiscardRequested;

        private OrderData? _currentDraft;

        public DraftRecoveryBanner()
        {
            InitializeComponent();
        }

        public void DisplayDraft(OrderData draft)
        {
            _currentDraft = draft;
            int count = draft.Items?.Count ?? 0;
            string client = !string.IsNullOrWhiteSpace(draft.ClientName)
                ? draft.ClientName
                : (!string.IsNullOrWhiteSpace(draft.ClientPhone) ? draft.ClientPhone : "Новый заказ");

            string time = draft.UpdatedAt.ToString("dd.MM HH:mm");
            TxtDraftInfo.Text = $"от {time} (позиций: {count}, клиент: {client})";
            Visibility = Visibility.Visible;
        }

        public void HideBanner()
        {
            Visibility = Visibility.Collapsed;
        }

        public OrderData? CurrentDraft => _currentDraft;

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            RestoreRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnSelective_Click(object sender, RoutedEventArgs e)
        {
            SelectiveRestoreRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnPostpone_Click(object sender, RoutedEventArgs e)
        {
            HideBanner();
            PostponeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnDiscard_Click(object sender, RoutedEventArgs e)
        {
            DiscardRequested?.Invoke(this, EventArgs.Empty);
            HideBanner();
            _currentDraft = null;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            BtnPostpone_Click(sender, e);
        }
    }
}
