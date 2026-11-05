using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Background crash-recovery service for in-progress orders.
    /// Debounces draft writes to prevent UI lag, writes atomically via temporary files,
    /// and allows restoring unsaved drafts after unexpected application exits or crashes.
    /// </summary>
    public class DraftRecoveryService : IDisposable
    {
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MosquitoNetCalculator");

        public static string DraftFilePath { get; set; } = Path.Combine(AppDataDir, "draft.json");

        private readonly Timer _debounceTimer;
        private readonly object _lock = new();
        private OrderData? _pendingDraft;
        private bool _isDisposed;

        public int DebounceMs { get; set; } = 800;
        public event EventHandler<DateTime>? DraftSaved;

        public DraftRecoveryService()
        {
            _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Schedules a background save of the draft.
        /// Debounced by <see cref="DebounceMs"/> to ensure zero UI delay during fast keystrokes.
        /// </summary>
        public void NotifyChanged(OrderData currentDraft)
        {
            if (_isDisposed) return;
            lock (_lock)
            {
                _pendingDraft = currentDraft;
                _debounceTimer.Change(DebounceMs, Timeout.Infinite);
            }
        }

        private void OnDebounceTimerElapsed(object? state)
        {
            OrderData? draftToSave;
            lock (_lock)
            {
                draftToSave = _pendingDraft;
            }

            if (draftToSave != null)
            {
                SaveDraftCore(draftToSave);
            }
        }

        /// <summary>
        /// Immediately writes the draft synchronously to disk (e.g. on application exit).
        /// </summary>
        public void SaveDraftNow(OrderData currentDraft)
        {
            lock (_lock)
            {
                _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _pendingDraft = null;
            }
            SaveDraftCore(currentDraft);
        }

        private void SaveDraftCore(OrderData draft)
        {
            try
            {
                string dir = Path.GetDirectoryName(DraftFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string tmpPath = DraftFilePath + ".tmp";
                string json = JsonSerializer.Serialize(draft, OrderStorageService.JsonOptions);

                File.WriteAllText(tmpPath, json, System.Text.Encoding.UTF8);

                if (File.Exists(DraftFilePath))
                {
                    File.Delete(DraftFilePath);
                }
                File.Move(tmpPath, DraftFilePath);
                DraftSaved?.Invoke(this, DateTime.Now);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DraftRecoveryService] Error saving draft: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks whether a valid pending draft exists that has unsaved content.
        /// </summary>
        public bool HasPendingDraft()
        {
            try
            {
                if (!File.Exists(DraftFilePath)) return false;
                var draft = LoadDraft();
                if (draft == null) return false;

                // Consider non-empty if it contains items, notes, or client info
                bool hasItems = draft.Items != null && draft.Items.Count > 0;
                bool hasClientInfo = !string.IsNullOrWhiteSpace(draft.ClientName)
                    || !string.IsNullOrWhiteSpace(draft.ClientPhone)
                    || !string.IsNullOrWhiteSpace(draft.ClientAddress)
                    || !string.IsNullOrWhiteSpace(draft.Notes);

                return hasItems || hasClientInfo;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads the draft object from disk, or returns null if not found/corrupt.
        /// </summary>
        public OrderData? LoadDraft()
        {
            try
            {
                if (!File.Exists(DraftFilePath)) return null;
                string json = File.ReadAllText(DraftFilePath, System.Text.Encoding.UTF8);
                return JsonSerializer.Deserialize<OrderData>(json, OrderStorageService.JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DraftRecoveryService] Error loading draft: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Deletes the draft file (e.g. after successful save or discard).
        /// </summary>
        public void DiscardDraft()
        {
            lock (_lock)
            {
                _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _pendingDraft = null;
            }

            try
            {
                if (File.Exists(DraftFilePath))
                    File.Delete(DraftFilePath);

                string tmp = DraftFilePath + ".tmp";
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DraftRecoveryService] Error deleting draft: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
            _debounceTimer.Dispose();
        }
    }
}
