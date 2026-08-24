using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Image attached to the AI composer before sending. Carries the base64
    /// data URL sent to the model plus a display label. Runtime-only state —
    /// attachments are not persisted to chat history.
    /// </summary>
    public sealed class AiImageAttachment : INotifyPropertyChanged
    {
        private string? _ocrText;
        private string? _ocrFailureReason;

        public string FileName { get; init; } = "";
        public string DataUrl { get; init; } = "";
        public string SizeLabel { get; init; } = "";

        /// <summary>OCR result cached at attach time. Null until OCR completes.</summary>
        public string? OcrText
        {
            get => _ocrText;
            set
            {
                if (_ocrText == value) return;
                _ocrText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OcrStatus));
                OnPropertyChanged(nameof(OcrStatusGlyph));
                OnPropertyChanged(nameof(OcrStatusToolTip));
            }
        }

        /// <summary>Human-readable reason when OCR failed (no pack / decode error / no text).</summary>
        public string? OcrFailureReason
        {
            get => _ocrFailureReason;
            set
            {
                if (_ocrFailureReason == value) return;
                _ocrFailureReason = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OcrStatusToolTip));
            }
        }

        /// <summary>Machine-readable OCR state for chip styling: pending / ok / empty.</summary>
        public string OcrStatus => _ocrText == null
            ? "pending"
            : string.IsNullOrWhiteSpace(_ocrText) ? "empty" : "ok";

        /// <summary>Glyph shown on the attachment chip.</summary>
        public string OcrStatusGlyph => _ocrText == null
            ? "…"
            : string.IsNullOrWhiteSpace(_ocrText) ? "⚠" : "✓";

        /// <summary>Tooltip explaining the OCR state (includes the failure reason).</summary>
        public string OcrStatusToolTip
        {
            get
            {
                if (_ocrText == null) return "Распознаю текст…";
                if (!string.IsNullOrWhiteSpace(_ocrText)) return "Текст распознан";
                return string.IsNullOrWhiteSpace(OcrFailureReason)
                    ? "Текст не распознан"
                    : OcrFailureReason;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
