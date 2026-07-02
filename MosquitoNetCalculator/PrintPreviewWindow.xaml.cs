using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using MosquitoNetCalculator.Services;

namespace MosquitoNetCalculator
{
    public partial class PrintPreviewWindow : Window
    {
        private readonly string _htmlContent;
        private readonly List<string> _tempFiles = new();

        public PrintPreviewWindow(string htmlContent)
        {
            InitializeComponent();
            _htmlContent = htmlContent;

            // Print preview is always light-themed regardless of app theme
            WebView.DefaultBackgroundColor = System.Drawing.Color.White;

            Loaded += PrintPreviewWindow_Loaded;
        }

        private async void PrintPreviewWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize the WebView2 environment with a user-data folder
                // inside %AppData% to avoid E_ACCESSDENIED when the app is
                // installed under Program Files.
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MosquitoNetCalculator",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await WebView.EnsureCoreWebView2Async(env);

                // Write HTML to a temp file and navigate to it
                string tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"KP_preview_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                _tempFiles.Add(tempPath);

                File.WriteAllText(tempPath, _htmlContent, System.Text.Encoding.UTF8);

                // Auto-open the browser print dialog once the page finishes loading.
                // Skipping the intermediate «Печать / Сохранить PDF» button click —
                // user goes straight from «Печать КП» to the print settings dialog.
                WebView.CoreWebView2.NavigationCompleted += async (s, args) =>
                {
                    // Small delay to let the page fully lay out before triggering print
                    await System.Threading.Tasks.Task.Delay(400);
                    try
                    {
                        await WebView.CoreWebView2.ExecuteScriptAsync("window.print();");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"[PrintPreviewWindow] Auto-print failed: {ex.Message}");
                    }
                };

                WebView.CoreWebView2.Navigate(tempPath);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                var result = MessageBox.Show(
                    "Для предпросмотра КП необходим WebView2 Runtime.\n\n" +
                    "Нажмите «Да», чтобы открыть страницу загрузки Microsoft.\n" +
                    "Нажмите «Нет», чтобы открыть КП в браузере.",
                    "WebView2 не найден",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = DependencyCheckerService.WebView2DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Best-effort: if the browser can't be launched, the user still
                        // gets the KP opened via FallbackOpenInBrowser below.
                    }
                }

                FallbackOpenInBrowser();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка загрузки предпросмотра:\n{ex.Message}\n\nКП будет открыто в браузере.",
                    "Предпросмотр",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                FallbackOpenInBrowser();
                Close();
            }
        }

        private void FallbackOpenInBrowser()
        {
            try
            {
                string tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"KP_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                _tempFiles.Add(tempPath);

                File.WriteAllText(tempPath, _htmlContent, System.Text.Encoding.UTF8);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // Browser failed — save to Desktop as last resort
                try
                {
                    string desktopPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"KP_{DateTime.Now:yyyyMMdd_HHmmss}.html");

                    File.WriteAllText(desktopPath, _htmlContent, System.Text.Encoding.UTF8);

                    MessageBox.Show(
                        $"Не удалось открыть браузер.\n\nКП сохранено на рабочем столе:\n{desktopPath}\n\nОткройте файл вручную.",
                        "Печать КП",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        "Не удалось открыть браузер и сохранить файл КП.\nПроверьте права доступа к папкам Temp и Desktop.",
                        "Ошибка печати",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WebView.CoreWebView2 != null)
                {
                    await WebView.CoreWebView2.ExecuteScriptAsync("window.print();");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка печати: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CleanupTempFiles()
        {
            foreach (var path in _tempFiles)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { /* best-effort cleanup */ }
            }
            _tempFiles.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupTempFiles();
            base.OnClosed(e);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CleanupTempFiles();
            Close();
        }
    }
}
