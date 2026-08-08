using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// UI wrapper around <see cref="AiModelOption"/> that adds a selection flag
    /// used by the model-selection dialog.
    /// </summary>
    public sealed class AiModelSelectionItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public AiModelOption Model { get; }

        public string DisplayName => Model.DisplayName;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CheckGlyph)));
            }
        }

        /// <summary>Glyph that visually reflects the current selected state.</summary>
        public string CheckGlyph => IsSelected ? "●" : "○";

        /// <summary>Tooltip for the provider badge explaining the provider role.</summary>
        public string ProviderTooltip => Model.Provider == AiProvider.Nvidia
            ? "Провайдер NVIDIA"
            : "Провайдер OpenRouter";

        public AiModelSelectionItem(AiModelOption model, bool isSelected = false)
        {
            Model = model;
            _isSelected = isSelected;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
