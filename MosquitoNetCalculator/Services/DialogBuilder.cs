using System.Collections.Generic;
using System.Windows;
using MosquitoNetCalculator.Controls;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// v3.45.0 (Phase 4 refactoring): Fluent builder for generic message dialogs.
    /// Replaces programmatic dialog construction in DialogService with XAML templates.
    /// </summary>
    public sealed class DialogBuilder<T>
    {
        private string _title = "";
        private string _message = "";
        private readonly List<DialogButton<T>> _buttons = new();

        /// <summary>Sets the dialog title.</summary>
        public DialogBuilder<T> Title(string title)
        {
            _title = title;
            return this;
        }

        /// <summary>Sets the dialog message.</summary>
        public DialogBuilder<T> Message(string message)
        {
            _message = message;
            return this;
        }

        /// <summary>Adds a button to the dialog.</summary>
        public DialogBuilder<T> WithButton(string content, T result, bool isDefault = false, bool isCancel = false, string? styleResource = null)
        {
            _buttons.Add(new DialogButton<T>(content, result, isDefault, isCancel, styleResource));
            return this;
        }

        /// <summary>Shows the dialog and returns the selected result.</summary>
        public T ShowDialog(Window? owner)
        {
            // MessageDialogWindow is not generic, so we box the buttons to object results.
            var objectButtons = new List<DialogButton<object>>();
            foreach (var button in _buttons)
            {
                T result = button.Result;
                objectButtons.Add(new DialogButton<object>(
                    button.Content,
                    result!,
                    button.IsDefault,
                    button.IsCancel,
                    button.StyleResource));
            }

            var window = new MessageDialogWindow(_title, _message, objectButtons)
            {
                Owner = owner
            };
            window.ShowDialog();
            return window.SelectedResult is T typedResult ? typedResult : default!;
        }
    }

    /// <summary>
    /// Configuration for a single dialog button.
    /// </summary>
    public sealed class DialogButton<T>
    {
        public string Content { get; }
        public T Result { get; }
        public bool IsDefault { get; }
        public bool IsCancel { get; }
        public string? StyleResource { get; }

        public DialogButton(string content, T result, bool isDefault, bool isCancel, string? styleResource)
        {
            Content = content;
            Result = result;
            IsDefault = isDefault;
            IsCancel = isCancel;
            StyleResource = styleResource;
        }
    }
}
