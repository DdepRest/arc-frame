using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Service for automatic daily rolling backups and manual backups of the orders database.
    /// Stores archives in %AppData%\MosquitoNetCalculator\backups\ and keeps a 14-day rolling window.
    /// </summary>
    public class OrderBackupService
    {
        private static readonly string AppDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MosquitoNetCalculator");

        public static string BackupsDir { get; set; } = Path.Combine(AppDataDir, "backups");
        public static string OrdersDir { get; set; } = Path.Combine(AppDataDir, "orders");
        public static string CounterPath { get; set; } = Path.Combine(AppDataDir, "order_counter.json");
        public static string PricesPath { get; set; } = Path.Combine(AppDataDir, "prices.json");

        public const int RetentionDays = 14;

        public OrderBackupService()
        {
            if (!Directory.Exists(BackupsDir))
                Directory.CreateDirectory(BackupsDir);
        }

        /// <summary>
        /// Checks if a backup for today already exists; if not, creates one asynchronously in the background.
        /// </summary>
        public Task CreateDailyBackupIfNeededAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    string todayName = $"backup_{DateTime.Today:yyyy-MM-dd}.zip";
                    string targetFile = Path.Combine(BackupsDir, todayName);

                    if (!File.Exists(targetFile))
                    {
                        CreateBackupCore(targetFile);
                    }

                    PruneOldBackups(RetentionDays);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OrderBackupService] Daily backup failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Creates an immediate manual backup with timestamp. Returns the path of the created ZIP file.
        /// </summary>
        public string CreateManualBackup()
        {
            string fileName = $"backup_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip";
            string targetFile = Path.Combine(BackupsDir, fileName);

            CreateBackupCore(targetFile);
            PruneOldBackups(RetentionDays);

            return targetFile;
        }

        private void CreateBackupCore(string targetZipPath)
        {
            if (!Directory.Exists(BackupsDir))
                Directory.CreateDirectory(BackupsDir);

            if (File.Exists(targetZipPath))
                File.Delete(targetZipPath);

            using var zip = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);

            // Add all files from orders/
            if (Directory.Exists(OrdersDir))
            {
                foreach (var file in Directory.GetFiles(OrdersDir, "*.json"))
                {
                    try
                    {
                        zip.CreateEntryFromFile(file, "orders/" + Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OrderBackupService] Skip order file {file}: {ex.Message}");
                    }
                }
            }

            // Add counter if exists
            if (File.Exists(CounterPath))
            {
                try { zip.CreateEntryFromFile(CounterPath, Path.GetFileName(CounterPath)); } catch { }
            }

            // Add prices if exists
            if (File.Exists(PricesPath))
            {
                try { zip.CreateEntryFromFile(PricesPath, Path.GetFileName(PricesPath)); } catch { }
            }
        }

        /// <summary>
        /// Removes backups older than the retention threshold, keeping the newest ones.
        /// </summary>
        public void PruneOldBackups(int keepCount = RetentionDays)
        {
            try
            {
                if (!Directory.Exists(BackupsDir)) return;

                var files = Directory.GetFiles(BackupsDir, "backup_*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.CreationTime)
                    .ToList();

                if (files.Count > keepCount)
                {
                    foreach (var oldFile in files.Skip(keepCount))
                    {
                        try { oldFile.Delete(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OrderBackupService] Prune failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns a description of the latest backup (or null if none exist).
        /// </summary>
        public (DateTime Date, long SizeBytes, string FileName)? GetLatestBackupInfo()
        {
            try
            {
                if (!Directory.Exists(BackupsDir)) return null;

                var latest = Directory.GetFiles(BackupsDir, "backup_*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.CreationTime)
                    .FirstOrDefault();

                if (latest != null)
                {
                    return (latest.CreationTime, latest.Length, latest.Name);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Opens the backups folder in Windows Explorer.
        /// </summary>
        public void OpenBackupsFolder()
        {
            if (!Directory.Exists(BackupsDir))
                Directory.CreateDirectory(BackupsDir);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = BackupsDir,
                UseShellExecute = true
            });
        }
    }
}
