using System;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        internal void UpdateContractNumber()
        {
            if (SidebarControl?.TxtPrefix == null || SidebarControl?.TxtNumber == null) return;

            string prefix = Sidebar.TxtPrefix.Text.Trim();
            if (string.IsNullOrEmpty(prefix)) prefix = "1";

            string contractNum = ViewModel.OrdersVM.GenerateContractNumber(prefix);
            ClientInfo.ContractNumber = contractNum;
        }

        internal void UpdateCurrentOrderInfo()
        {
            if (StatusOrderInfo == null) return;
            StatusOrderInfo.Text = IsNewOrder
                ? "Новый заказ"
                : $"Ред.: {ClientInfo.ContractNumber}";
        }

        // ── Helpers ──

        /// <summary>
        /// IDisposable scope that flips <see cref="_suppressContractNumberUpdate"/>
        /// to true for the duration of a batch update and resets on dispose.
        /// </summary>
        internal IDisposable SuppressContractNumberUpdates() =>
            new SuppressContractNumberScope(this);

        /// <summary>
        /// Sets the contract-prefix textbox under a suppress scope and then
        /// re-rolls the in-progress contract number against the new prefix.
        /// </summary>
        internal void SyncContractPrefix(string newPrefix)
        {
            using (SuppressContractNumberUpdates())
            {
                Sidebar.TxtPrefix.Text = newPrefix;
            }
            UpdateContractNumber();
        }

        private sealed class SuppressContractNumberScope : IDisposable
        {
            private readonly MainWindow _owner;
            internal SuppressContractNumberScope(MainWindow owner)
            {
                _owner = owner;
                _owner._suppressContractNumberUpdate = true;
            }
            public void Dispose() =>
                _owner._suppressContractNumberUpdate = false;
        }
    }
}
