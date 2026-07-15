using System;
using System.Reflection;
using System.Windows;

namespace Probe
{
    class Program
    {
        static void Main()
        {
            var t = typeof(System.Windows.Application);
            Console.WriteLine($"== Static fields on {t.FullName} (NonPublic | Static) ==");
            foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            {
                Console.WriteLine($"  {f.Name} : {f.FieldType.Name}");
            }
            Console.WriteLine();
            Console.WriteLine("== Static properties (NonPublic | Static) ==");
            foreach (var p in t.GetProperties(BindingFlags.NonPublic | BindingFlags.Static))
            {
                Console.WriteLine($"  {p.Name} : {p.PropertyType.Name}");
            }
        }
    }
}
