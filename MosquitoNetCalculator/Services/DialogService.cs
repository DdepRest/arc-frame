using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator.Services
{
    public enum DialogResult { Save, Discard, Cancel }

    public static class DialogService
    {
        // Resource-lookup helpers. Use ?. so the dialog can be exercised in
        // non-WPF contexts (tests, background-thread callers) without NRE.
        // Fallbacks are last-resort brush/color/style values used only when the
        // theme dictionary is missing the named key.
        private static Brush GetBrush(string key, Brush fallback) =>
            (Brush?)Application.Current?.FindResource(key) ?? fallback;

        private static Color GetColor(string key, Color fallback) =>
            (Color?)Application.Current?.FindResource(key) ?? fallback;

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
        /// Try to fetch a Style resource. Returns true and outputs the style when
        /// found, false otherwise. Caller is expected to apply a fallback when
        /// false is returned.
        /// </summary>
        private static bool TryGetStyle(string key, out Style? style)
        {
            style = (Style?)Application.Current?.FindResource(key);
            return style != null;
        }

        public static bool ShowConfirm(string message, string title = "Подтверждение", Window? owner = null)
        {
            // Chromeless Fluent dialog — matches ShowSaveDiscardCancel design
            var window = new Window
            {
                Title = title,
                Width = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.Height
            };

            var card = new Border
            {
                Background = GetBrush("Surface", Brushes.White),
                BorderBrush = GetBrush("Border", Brushes.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = GetColor("ShadowColor", Colors.Black),
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.25
                }
            };

            var rootStack = new StackPanel();

            // ─── Custom title bar ───
            var titleBar = new Border
            {
                Background = GetBrush("HeaderBg", Brushes.WhiteSmoke),
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Height = 32,
                Padding = new Thickness(14, 0, 0, 0)
            };
            var titleBarGrid = new Grid();
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextPrimary", Brushes.Black),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(titleText, 0);
            titleBarGrid.Children.Add(titleText);

            var closeBtn = CreateFluentCloseButton(() => { window.DialogResult = false; window.Close(); });
            Grid.SetColumn(closeBtn, 1);
            titleBarGrid.Children.Add(closeBtn);

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) window.DragMove();
            };

            titleBar.Child = titleBarGrid;
            rootStack.Children.Add(titleBar);

            // ─── Content ───
            var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            content.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = GetBrush("TextPrimary", Brushes.Black),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnNo = new Button
            {
                Content = "Нет",
                MinWidth = 90,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand,
                IsCancel = true
            };
            if (TryGetStyle("GhostButton", out var noStyle))
                btnNo.Style = noStyle;
            else
            {
                Trace.WriteLine("[DialogService] Style 'GhostButton' not found, using fallback brushes for 'Нет' button.");
                btnNo.Background = GetBrush("GhostBg", Brushes.White);
                btnNo.Foreground = GetBrush("TextPrimary", Brushes.Black);
            }
            btnNo.Click += (s, e) => { window.DialogResult = false; window.Close(); };

            var btnYes = new Button
            {
                Content = "Да",
                MinWidth = 100,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                IsDefault = true
            };
            if (TryGetStyle("PrimaryButton", out var yesStyle))
                btnYes.Style = yesStyle;
            else
            {
                Trace.WriteLine("[DialogService] Style 'PrimaryButton' not found, using fallback brushes for 'Да' button.");
                btnYes.Background = GetBrush("Accent", Brushes.SteelBlue);
                btnYes.Foreground = GetBrush("OnAccent", Brushes.White);
            }
            btnYes.Click += (s, e) => { window.DialogResult = true; window.Close(); };

            btnPanel.Children.Add(btnNo);
            btnPanel.Children.Add(btnYes);
            content.Children.Add(btnPanel);

            rootStack.Children.Add(content);
            card.Child = rootStack;
            window.Content = card;

            window.ShowDialog();
            return window.DialogResult == true;
        }

        /// <summary>
        /// Fluent-styled update-available dialog — replaces raw MessageBox
        /// for the "new version found" confirmation. Shows version, asks
        /// to download & restart. Returns true if user clicked "Скачать".
        /// </summary>
        public static bool ShowUpdateAvailable(string version, Window? owner = null)
        {
            var window = new Window
            {
                Title = "Доступно обновление",
                Width = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.Height
            };

            var card = new Border
            {
                Background = GetBrush("Surface", Brushes.White),
                BorderBrush = GetBrush("Border", Brushes.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = GetColor("ShadowColor", Colors.Black),
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.25
                }
            };

            var rootStack = new StackPanel();

            // Title bar
            var titleBar = new Border
            {
                Background = GetBrush("HeaderBg", Brushes.WhiteSmoke),
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Height = 32,
                Padding = new Thickness(14, 0, 0, 0)
            };
            var titleBarGrid = new Grid();
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = "Доступно обновление",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextPrimary", Brushes.Black),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(titleText, 0);
            titleBarGrid.Children.Add(titleText);

            var closeBtn = CreateFluentCloseButton(() => { window.DialogResult = false; window.Close(); });
            Grid.SetColumn(closeBtn, 1);
            titleBarGrid.Children.Add(closeBtn);

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) window.DragMove();
            };
            titleBar.Child = titleBarGrid;
            rootStack.Children.Add(titleBar);

            // Content
            var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            // Version badge
            var badge = new Border
            {
                Background = GetBrush("Accent", Brushes.SteelBlue),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 5, 12, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 16)
            };
            badge.Child = new TextBlock
            {
                Text = $"Версия {version}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = GetBrush("OnAccent", Brushes.White)
            };
            content.Children.Add(badge);

            content.Children.Add(new TextBlock
            {
                Text = "Скачать и установить обновление?",
                FontSize = 13,
                Foreground = GetBrush("TextPrimary", Brushes.Black),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });

            content.Children.Add(new TextBlock
            {
                Text = "Приложение будет перезапущено.",
                FontSize = 12,
                Foreground = GetBrush("TextMuted", Brushes.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            // Buttons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Отмена",
                MinWidth = 90,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand,
                IsCancel = true
            };
            if (TryGetStyle("GhostButton", out var cancelStyle))
                btnCancel.Style = cancelStyle;
            else
                btnCancel.Background = GetBrush("GhostBg", Brushes.White);
            btnCancel.Click += (s, e) => { window.DialogResult = false; window.Close(); };

            var btnDownload = new Button
            {
                Content = "Скачать",
                MinWidth = 110,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                IsDefault = true
            };
            if (TryGetStyle("SuccessButton", out var downloadStyle))
                btnDownload.Style = downloadStyle;
            else
                btnDownload.Background = GetBrush("Success", Brushes.Green);
            btnDownload.Click += (s, e) => { window.DialogResult = true; window.Close(); };

            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnDownload);
            content.Children.Add(btnPanel);

            rootStack.Children.Add(content);
            card.Child = rootStack;
            window.Content = card;

            window.ShowDialog();
            return window.DialogResult == true;
        }

        public static DialogResult ShowSaveDiscardCancel(string message, string title = "Несохранённые изменения", Window? owner = null)
        {
            // Chromeless dialog (WindowStyle.None + AllowsTransparency) with a
            // custom 32px title bar matching the main window's TitleBarControl
            // — the program uses a custom title bar everywhere, so the dialog
            // must too (a Windows-native chrome here would feel out of place).
            string result = "cancel";

            var window = new Window
            {
                Title = title, // for taskbar / accessibility
                Width = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.Height
            };

            // Outer card with rounded corners and drop shadow.
            var card = new Border
            {
                Background = GetBrush("Surface", Brushes.White),
                BorderBrush = GetBrush("Border", Brushes.Gray),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = GetColor("ShadowColor", Colors.Black),
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.25
                }
            };

            var rootStack = new StackPanel();

            // ─── Custom title bar (matches TitleBarControl look) ──────────────
            var titleBar = new Border
            {
                Background = GetBrush("HeaderBg", Brushes.WhiteSmoke),
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Height = 32,
                Padding = new Thickness(14, 0, 0, 0)
            };
            var titleBarGrid = new Grid();
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetBrush("TextPrimary", Brushes.Black),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(titleText, 0);
            titleBarGrid.Children.Add(titleText);

            // Close (✕) button — same Segoe Fluent Icons glyph as the main
            // window's close button. Transparent background, hover lightens
            // via inline trigger.
            var closeBtn = CreateFluentCloseButton(() => { result = "cancel"; window.Close(); });
            Grid.SetColumn(closeBtn, 1);
            titleBarGrid.Children.Add(closeBtn);

            titleBar.Child = titleBarGrid;

            // Make the whole title bar a drag region (excludes the close
            // button which captures its own click). This mirrors how the main
            // window's TitleBarControl handles drag.
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) window.DragMove();
            };

            rootStack.Children.Add(titleBar);

            // ─── Content area (message + 3 buttons) ──────────────────────────
            var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            content.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = GetBrush("TextPrimary", Brushes.Black),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // Buttons use the styled ControlTemplate (rounded border + colors)
            // from GhostButton / DangerGhostButton / SuccessButton via direct
            // Style assignment (SetCurrentValue on the Style DP doesn't
            // reliably re-template). Local Padding / FontSize / FontWeight
            // keep the buttons at a dialog-appropriate size.
            var btnCancel = new Button
            {
                Content = "Отмена",
                MinWidth = 90,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Cursor = Cursors.Hand,
                IsCancel = true
            };
            if (TryGetStyle("GhostButton", out var cancelStyle))
                btnCancel.Style = cancelStyle;
            else
            {
                Trace.WriteLine("[DialogService] Style 'GhostButton' not found, using fallback brushes for 'Отмена' button.");
                btnCancel.Background = GetBrush("GhostBg", Brushes.White);
                btnCancel.Foreground = GetBrush("TextPrimary", Brushes.Black);
            }
            btnCancel.Click += (s, e) => { result = "cancel"; window.Close(); };

            var btnNo = new Button
            {
                Content = "Нет",
                MinWidth = 110,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            if (TryGetStyle("DangerGhostButton", out var noStyle))
                btnNo.Style = noStyle;
            else
            {
                Trace.WriteLine("[DialogService] Style 'DangerGhostButton' not found, using fallback brushes for discard 'Нет' button.");
                btnNo.Background = GetBrush("DangerLight", Brushes.LightPink);
                btnNo.Foreground = GetBrush("Danger", Brushes.Red);
            }
            btnNo.Click += (s, e) => { result = "discard"; window.Close(); };

            var btnSave = new Button
            {
                Content = "Да",
                MinWidth = 100,
                Padding = new Thickness(16, 7, 16, 7),
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                IsDefault = true
            };
            if (TryGetStyle("SuccessButton", out var saveStyle))
                btnSave.Style = saveStyle;
            else
            {
                Trace.WriteLine("[DialogService] Style 'SuccessButton' not found, using fallback brushes for save 'Да' button.");
                btnSave.Background = GetBrush("Success", Brushes.Green);
                btnSave.Foreground = GetBrush("OnSuccess", Brushes.White);
            }
            btnSave.Click += (s, e) => { result = "save"; window.Close(); };

            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnNo);
            btnPanel.Children.Add(btnSave);
            content.Children.Add(btnPanel);

            rootStack.Children.Add(content);
            card.Child = rootStack;
            window.Content = card;

            window.ShowDialog();
            return result switch { "save" => DialogResult.Save, "discard" => DialogResult.Discard, _ => DialogResult.Cancel };
        }
    }
}
