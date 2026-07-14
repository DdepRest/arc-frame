using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Tests
{
    internal static class ModuleInitializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        }
    }
}
