using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Controls;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public enum SaveDiscardCancelResult { Save, Discard, Cancel }

    /// <summary>
    /// v3.45.0 (Phase 4 refactoring): DialogService is now a thin facade over
    /// XAML-based dialog windows and the fluent DialogBuilder. UI construction
    /// code has been moved to Controls/MessageDialogWindow and
    /// Controls/UpdateAvailableWindow.
    /// </summary>
    public static class DialogService
    {
        // Resource-lookup helpers. Use ?. so the dialog can be exercised in
        // non-WPF contexts (tests, background-thread callers) without NRE.
        // Fallbacks are last-resort brush/color/style values used only when the
        // theme dictionary is missing the named key.
        private static Brush GetBrush(string key, Brush fallback) =>
            (Brush?)Application.Current?.FindResource(key) ?? fallback;

        /// <summary>
        /// Creates a close (✕) button with the same Fluent animated Danger overlay
        /// as the main window's TitleBarControl — smooth 150ms fade-in of red
        /// background with white icon on hover.
        /// </summary>
        public static Button CreateFluentCloseButton(Action closeAction)
        {
            // Build template via FrameworkElementFactory (required for ControlTemplate in code)
            var fefBorder = new FrameworkElementFactory(typeof(Border));
            fefBorder.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent), Path = new PropertyPath("Background") });
            fefBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0));

            var fefGrid = new FrameworkElementFactory(typeof(Grid));

            var fefOverlay = new FrameworkElementFactory(typeof(Border));
            fefOverlay.Name = "HoverOverlay";
            fefOverlay.SetValue(Border.BackgroundProperty, GetBrush("Danger", Brushes.Red));
            fefOverlay.SetValue(Border.OpacityProperty, 0.0);

            var fefContent = new FrameworkElementFactory(typeof(ContentPresenter));
            fefContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            fefContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            fefGrid.AppendChild(fefOverlay);
            fefGrid.AppendChild(fefContent);
            fefBorder.AppendChild(fefGrid);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = fefBorder };

            // Hover trigger: white foreground + animated Danger overlay
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));

            var enterStoryboard = new Storyboard();
            var enterAnim = new DoubleAnimation(1.0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetName(enterAnim, "HoverOverlay");
            Storyboard.SetTargetProperty(enterAnim, new PropertyPath(Border.OpacityProperty));
            enterStoryboard.Children.Add(enterAnim);
            trigger.EnterActions.Add(new BeginStoryboard { Storyboard = enterStoryboard });

            var exitStoryboard = new Storyboard();
            var exitAnim = new DoubleAnimation(0.0, new Duration(TimeSpan.FromMilliseconds(150)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTargetName(exitAnim, "HoverOverlay");
            Storyboard.SetTargetProperty(exitAnim, new PropertyPath(Border.OpacityProperty));
            exitStoryboard.Children.Add(exitAnim);
            trigger.ExitActions.Add(new BeginStoryboard { Storyboard = exitStoryboard });

            template.Triggers.Add(trigger);

            var glyph = new TextBlock
            {
                Text = "\uE8BB",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 10
            };

            var btn = new Button
            {
                Template = template,
                Content = glyph,
                Width = 40,
                Height = 40,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = GetBrush("TextPrimary", Brushes.Black),
                Cursor = Cursors.Hand,
                ToolTip = "Закрыть (Escape)"
            };

            btn.Click += (s, e) => closeAction();
            return btn;
        }

        /// <summary>
        /// Shows a confirmation dialog with "Да" / "Нет" buttons.
        /// </summary>
        public static bool ShowConfirm(string message, string title = "Подтверждение", Window? owner = null)
        {
            return new DialogBuilder<bool>()
                .Title(title)
                .Message(message)
                .WithButton("Нет", false, isCancel: true, styleResource: "GhostButton")
                .WithButton("Да", true, isDefault: true, styleResource: "PrimaryButton")
                .ShowDialog(owner);
        }

        /// <summary>
        /// Fluent-styled update-available dialog — replaces raw MessageBox
        /// for the "new version found" confirmation. Shows version, asks
        /// to download & restart. Returns true if user clicked "Скачать".
        /// </summary>
        public static bool ShowUpdateAvailable(string version, Window? owner = null)
            => ShowUpdateAvailable(version, Array.Empty<UpdateItem>(), owner, isAutomatic: false);

        /// <summary>
        /// Fluent-styled update-available dialog with changelog.
        /// Shows version badge, changelog of skipped versions, and
        /// download confirmation. Returns true if user clicked "Скачать и установить".
        /// <paramref name="isAutomatic"/> controls whether the anti-recommend
        /// text is shown next to the "Отложить" button.
        /// </summary>
        public static bool ShowUpdateAvailable(string version, IEnumerable<UpdateItem> changelog, Window? owner = null, bool isAutomatic = false)
        {
            var window = new UpdateAvailableWindow(version, changelog, isAutomatic) { Owner = owner };
            window.ShowDialog();
            return window.Accepted;
        }

        /// <summary>
        /// Shows a Save / Discard / Cancel dialog for unsaved changes.
        /// </summary>
        public static SaveDiscardCancelResult ShowSaveDiscardCancel(string message, string title = "Несохранённые изменения", Window? owner = null)
        {
            return new DialogBuilder<SaveDiscardCancelResult>()
                .Title(title)
                .Message(message)
                .WithButton("Отмена", SaveDiscardCancelResult.Cancel, isCancel: true, styleResource: "GhostButton")
                .WithButton("Нет", SaveDiscardCancelResult.Discard, styleResource: "DangerGhostButton")
                .WithButton("Да", SaveDiscardCancelResult.Save, isDefault: true, styleResource: "SuccessButton")
                .ShowDialog(owner);
        }
    }
}
