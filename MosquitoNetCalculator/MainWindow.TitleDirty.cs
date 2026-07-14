using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow : INotifyPropertyChanged
    {
        /// <summary>
        /// Re-opens the location-picker dialog so the user can change their
        /// installation point. After it closes, refresh sidebar / title / and
        /// the in-progress contract number so prefix change takes effect.
        /// </summary>
        internal void OpenWelcomeWindow()
        {
            var welcome = new WelcomeWindow { Owner = this };
            if (welcome.ShowDialog() != true) return;

            SyncContractPrefix(AppSettingsService.LoadContractPrefix());
            UpdateBaseTitle();
        }

        /// <summary>
        /// Sets the Title to the base text "A.R.C. Frame v{version} – {location}".
        /// Preserves the trailing dirty marker "•" if present.
        /// </summary>
        internal void UpdateBaseTitle()
        {
            _cachedTitleLocation = AppSettingsService.LoadLocationName();
            ApplyTitle();
        }

        /// <summary>
        /// Builds the base title text WITHOUT the trailing dirty marker from
        /// the cached location + cached assembly version. Pure function over
        /// cached state — touch-free of AppSettingsService and UndoRedo so it
        /// composes cleanly with the dirty-toggling ApplyTitle() path.
        /// </summary>
        private string BuildBaseTitle()
        {
            string version = _appVersion?.ToString() ?? "";
            return string.IsNullOrEmpty(_cachedTitleLocation)
                ? $"A.R.C. Frame v{version}"
                : $"A.R.C. Frame v{version} – {_cachedTitleLocation}";
        }

        /// <summary>
        /// Single apply path: writes Title + ActionBarControl.DirtyChip
        /// in lock-step from the cached base title and the current dirty flag.
        /// </summary>
        private void ApplyTitle()
        {
            bool dirty = ViewModel?.UndoRedo?.IsDirty ?? false;
            Title = dirty ? BuildBaseTitle() + "  •" : BuildBaseTitle();

            if (StatusDirtyIndicator != null)
                StatusDirtyIndicator.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateDirtyIndicator() => ApplyTitle();

        /// <summary>
        /// Updates the undo/redo hint in the status bar. Called after every undo/redo push.
        /// Shows available shortcuts when the undo stack is non-empty.
        /// </summary>
        internal void UpdateUndoRedoHint()
        {
            if (UndoRedoHint == null) return;
            bool canUndo = ViewModel.UndoRedo.CanUndo;
            UndoRedoHint.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
            UndoRedoHint.Text = canUndo ? "Ctrl+Z — отменить | Ctrl+Y — повторить" : "";
        }

        internal void MarkDirty()
        {
            ViewModel.UndoRedo.MarkDirty();
        }

        internal void MarkClean()
        {
            ViewModel.UndoRedo.MarkClean();
        }

        internal void PushUndo()
        {
            var snapshot = ViewModel.SnapshotItems();
            if (ViewModel.UndoRedo.TryPeekTopSnapshot(out var top))
            {
                try
                {
                    string newJson = System.Text.Json.JsonSerializer.Serialize(snapshot);
                    string topJson = System.Text.Json.JsonSerializer.Serialize(top!);
                    if (newJson == topJson) return;
                }
                catch { /* serialization failure is non-fatal; push anyway */ }
            }
            ViewModel.UndoRedo.PushUndo(snapshot);
            UpdateUndoRedoHint();
        }

        private void RestoreFromSnapshot(OrderSnapshot snapshot)
        {
            ViewModel.RestoreFromSnapshot(snapshot, RecalculateAndUpdateTotal);
            UpdateTotal();
            UpdateEmptyState();
            UpdateUndoRedoHint();
        }

        private void Undo()
        {
            if (!ViewModel.UndoRedo.CanUndo)
            {
                ToastService.ShowToast("Нечего отменять.", ToastType.Info);
                return;
            }
            var prev = ViewModel.UndoRedo.Undo(ViewModel.SnapshotItems);
            if (prev != null) { RestoreFromSnapshot(prev); UpdateUndoRedoHint(); }
        }

        private void Redo()
        {
            if (!ViewModel.UndoRedo.CanRedo)
            {
                ToastService.ShowToast("Нечего повторять.", ToastType.Info);
                return;
            }
            var next = ViewModel.UndoRedo.Redo(ViewModel.SnapshotItems);
            if (next != null) { RestoreFromSnapshot(next); UpdateUndoRedoHint(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
