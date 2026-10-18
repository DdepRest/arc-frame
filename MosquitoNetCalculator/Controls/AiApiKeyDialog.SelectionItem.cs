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
        public string ProviderTooltip => Model.Provider switch
        {
            AiProvider.Nvidia => "Провайдер NVIDIA",
            AiProvider.FreeLlmApi => "Провайдер FreeLLMAPI",
            _ => "Провайдер OpenRouter"
        };

        /// <summary>True when the catalog reported the model accepts photos.</summary>
        public bool IsVisionCapable => Model.SupportsVision == true;

        /// <summary>Tooltip for the vision badge.</summary>
        public string VisionTooltip => "Поддерживает фото (vision)";

        /// <summary>True for meta-routers like Free Models Router, auto, fusion.</summary>
        public bool IsRouter =>
            Model.Id.StartsWith("auto", System.StringComparison.OrdinalIgnoreCase) ||
            Model.Id == "openrouter/free" ||
            Model.Id == "fusion";

        public string RouterBadgeText => Model.Id switch
        {
            "fusion" => "ФЬЮЖН",
            _ => "РОУТЕР"
        };

        private int _latencyMs = -1;
        private bool _isTesting;
        private bool _isAvailable = true;

        public int LatencyMs
        {
            get => _latencyMs;
            set
            {
                if (_latencyMs == value) return;
                _latencyMs = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LatencyMs)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LatencyText)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasLatency)));
            }
        }

        public bool IsTesting
        {
            get => _isTesting;
            set
            {
                if (_isTesting == value) return;
                _isTesting = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsTesting)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LatencyText)));
            }
        }

        public bool IsAvailable
        {
            get => _isAvailable;
            set
            {
                if (_isAvailable == value) return;
                _isAvailable = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsAvailable)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LatencyText)));
            }
        }

        public bool HasLatency => LatencyMs >= 0 || !_isAvailable || _isTesting;

        public string LatencyText
        {
            get
            {
                if (_isTesting) return "…";
                if (!_isAvailable) return "✕ Недоступна";
                if (_latencyMs >= 0) return $"⚡ {_latencyMs} мс";
                return "";
            }
        }

        public AiModelSelectionItem(AiModelOption model, bool isSelected = false)
        {
            Model = model;
            _isSelected = isSelected;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
