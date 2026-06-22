using System;
using System.Collections.Generic;

namespace MosquitoNetCalculator.Models
{
    public class UpdateItem
    {
        public DateTime Date { get; set; } = DateTime.Today;
        public string Version { get; set; } = "";
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Changes { get; set; } = new();
    }
}
