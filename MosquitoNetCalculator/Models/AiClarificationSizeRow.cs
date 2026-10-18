using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// One editable size row of the AI clarification card («Размеры, мм»).
    ///
    /// The card used to hold a single width/height/quantity triple, so a request
    /// naming SEVERAL positions (a screenshot with two rows of «ПМС Anwis, бел. 1
    /// 304x1593 / 1 724x1593», or «сетки 700х1400 и 900х1600») could only ever
    /// produce one position — the second one was silently lost. Every size read
    /// from the request / OCR / the model now becomes its own row, and
    /// «Добавить в расчёт» builds one add-item per row with the shared
    /// type/color/mode/монтаж picked once above.
    ///
    /// Runtime-only UI state: never persisted.
    /// </summary>
    public sealed class AiClarificationSizeRow : INotifyPropertyChanged
    {
        private string _widthText = string.Empty;
        private string _heightText = string.Empty;
        private string _quantityText = "1";

        public AiClarificationSizeRow(string widthText = "", string heightText = "", string quantityText = "1")
        {
            _widthText = widthText;
            _heightText = heightText;
            _quantityText = quantityText;
        }

        public string WidthText
        {
            get => _widthText;
            set { _widthText = value; OnPropertyChanged(); }
        }

        public string HeightText
        {
            get => _heightText;
            set { _heightText = value; OnPropertyChanged(); }
        }

        public string QuantityText
        {
            get => _quantityText;
            set { _quantityText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True when the row carries no width and no height. Such a row adds
        /// nothing to the order (an empty row the user added but never filled
        /// must not block the whole card).
        /// </summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(WidthText) && string.IsNullOrWhiteSpace(HeightText);

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
