using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace MosquitoNetCalculator.Services
{
    public partial class PrintService
    {
        private string LoadTemplate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "MosquitoNetCalculator.Resources.print_template.html";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "print_template.html");
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath, Encoding.UTF8);
            }

            throw new InvalidOperationException(
                "print_template.html not found — embedded resource missing and file not on disk.");
        }
    }
}
