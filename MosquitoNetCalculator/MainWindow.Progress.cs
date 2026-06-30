using System;
using MosquitoNetCalculator.Helpers;
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
    }
}
