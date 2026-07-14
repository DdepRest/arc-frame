using System;
using System.Collections.ObjectModel;
using System.Windows;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class MainWindow
    {
        // Owns the download-progress-bar fade-in/out Storyboard logic,
        // extracted from OnUpdateProgressChanged in v3.40.3 to enable unit
        // testing without spinning up the full MainWindow tree.
        private ProgressBarUpdateAnimator? _progressAnimator;

        private void OnUpdateProgressChanged(object? sender, EventArgs e)
        {
            // Body moved to MosquitoNetCalculator/Helpers/ProgressBarUpdateAnimator.cs
            // in v3.40.3 (testability refactor). The animator includes the same
            // try/catch + TryFindResource fallback that was inline here in v3.40.2
            // — guards the auto-update flow against any UI-thread regression.
            _progressAnimator?.Animate(UpdateService.IsDownloading);
        }

        /// <summary>
        /// Auto-update detected a new release on a background thread.
        /// Forwarding to <see cref="ViewModels.MainWindowViewModel.AddNewUpdate"/>
        /// must happen on the UI dispatcher — <see cref="ObservableCollection{T}"/>
        /// throws <see cref="InvalidOperationException"/> on cross-thread writes,
        /// and the binding engine needs the change to be observed on the
        /// dispatcher that owns the visual tree.
        /// <para>
        /// The AddNewUpdate call itself is fully synchronous, so WPF batches
        /// data-bind + render until AFTER the entire batch — keeping the
        /// "Новейшая" badge from flickering off-then-on across the three-step
        /// atomic ordering (newItem.IsLatest=true → clear old → Insert(0,
        /// newItem)). See <c>MainWindowViewModelTests.AddNewUpdate_ZeroIsLatestFrame_neverObserved</c>.
        /// </para>
        /// </summary>
        private void OnUpdateDetected(UpdateItem item)
        {
            if (item == null) return;
            // Dispatcher.Invoke (sync) is intentional: we don't yield back
            // to the background scheduler mid-call, which would let WPF
            // render the transient "no badge" state before Insert completes.
            Dispatcher.Invoke(() => ViewModel.AddNewUpdate(item));
        }
    }
}
