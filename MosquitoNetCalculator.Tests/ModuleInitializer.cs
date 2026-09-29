using System.Runtime.CompilerServices;
using Xunit;

// Параллелизм коллекций выключен на УРОВНЕ СБОРКИ, а не только конфигом
// раннера (xunit.runner.json): WPF-статика в тест-хосте одна, и гонка двух
// STA-нитей роняет хост FailFast'ом из Application.GetResourcePackage
// (v3.53.1: краш и локально, и на CI). Атрибут гарантирован компилятором,
// тогда как файл конфига может не доехать в вывод сборки.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MosquitoNetCalculator.Tests
{
    internal static class ModuleInitializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            // Сериализацию WPF-тестов делает WpfTestHelper.WpfGate (процесс-глобальный
            // замок): параллельные STA-нити разных коллекций ломали WPF-статику —
            // FailFast из Application.GetResourcePackage и «Unrecoverable system
            // error» testhost'а на CI (v3.53.1). Programmatic RenderMode.SoftwareOnly
            // пробовали — не помогло и нестабильно по времени вызова: убрано.
        }
    }
}
