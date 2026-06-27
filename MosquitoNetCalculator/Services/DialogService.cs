using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Models;

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
        /// Builds a compact horizontal bullet row (• text) for changelog items.
        /// </summary>
        private static StackPanel CreateBullet(string text, bool isBold)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 2, 0, 2)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "•",
                FontSize = 12,
                Foreground = GetBrush("TextMuted", Brushes.Gray),
                Margin = new Thickness(0, 0, 6, 0)
            });

            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = GetBrush("TextSecondary", Brushes.DarkSlateGray),
                TextWrapping = TextWrapping.Wrap
            };
            if (isBold)
                tb.FontWeight = FontWeights.SemiBold;

            panel.Children.Add(tb);
            return panel;
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

        /// <summary>
        /// Builds the shared dialog shell — chromeless window, rounded card with
        /// drop shadow, custom title bar with close button and drag support, and
        /// an empty content <see cref="StackPanel"/> ready for message + buttons.
        /// Returns the <paramref name="closeResult"/> action wired to the close
        /// button (callers invoke it from their own button handlers).
        /// </summary>
        private static (Window Window, StackPanel ContentPanel, Action CloseResult) BuildDialogBase(
            string title, int width, Window? owner, Action closeResult)
        {
            var window = new Window
            {
                Title = title,
                Width = width,
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

            // Custom title bar
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

            var closeBtn = CreateFluentCloseButton(() => { closeResult(); window.Close(); });
            Grid.SetColumn(closeBtn, 1);
            titleBarGrid.Children.Add(closeBtn);

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) window.DragMove();
            };
            titleBar.Child = titleBarGrid;
            rootStack.Children.Add(titleBar);

            var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            rootStack.Children.Add(content);

            card.Child = rootStack;
            window.Content = card;

            return (window, content, closeResult);
        }

        public static bool ShowConfirm(string message, string title = "Подтверждение", Window? owner = null)
        {
            bool result = false;
            var (window, content, _) = BuildDialogBase(title, 380, owner, () => result = false);

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
            btnNo.Click += (s, e) => { result = false; window.Close(); };

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
            btnYes.Click += (s, e) => { result = true; window.Close(); };

            btnPanel.Children.Add(btnNo);
            btnPanel.Children.Add(btnYes);
            content.Children.Add(btnPanel);

            window.ShowDialog();
            return result;
        }

        /// <summary>
        /// Fluent-styled update-available dialog — replaces raw MessageBox
        /// for the "new version found" confirmation. Shows version, asks
        /// to download & restart. Returns true if user clicked "Скачать".
        /// </summary>
        public static bool ShowUpdateAvailable(string version, Window? owner = null)
            => ShowUpdateAvailable(version, Array.Empty<UpdateItem>(), owner);

        /// <summary>
        /// Fluent-styled update-available dialog with changelog.
        /// Shows version badge, changelog of skipped versions, and
        /// download confirmation. Returns true if user clicked "Скачать и установить".
        /// </summary>
        public static bool ShowUpdateAvailable(string version, IEnumerable<UpdateItem> changelog, Window? owner = null)
        {
            bool result = false;
            var (window, content, _) = BuildDialogBase("Доступно обновление", 460, owner, () => result = false);

            // ── Version badge ──
            var badge = new Border
            {
                Background = GetBrush("Accent", Brushes.SteelBlue),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 5, 12, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 14)
            };
            badge.Child = new TextBlock
            {
                Text = $"Версия {version}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = GetBrush("OnAccent", Brushes.White)
            };
            content.Children.Add(badge);

            // ── Changelog ──
            var changelogItems = changelog as UpdateItem[] ?? changelog.ToArray();
            if (changelogItems.Length > 0)
            {
                var changelogPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

                var scrollViewer = new ScrollViewer
                {
                    MaxHeight = 220,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Margin = new Thickness(0, 0, 0, 0)
                };

                var versionsStack = new StackPanel();

                foreach (var item in changelogItems)
                {
                    // Version header row: version + date + type badge
                    var headerPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 10, 0, 4)
                    };

                    var versionText = new TextBlock
                    {
                        Text = $"v{item.Version}",
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = GetBrush("TextPrimary", Brushes.Black),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    headerPanel.Children.Add(versionText);

                    // Type badge (coloured by update type)
                    var typeBrush = item.Type switch
                    {
                        "Новинка" => GetBrush("Success", Brushes.Green),
                        "Исправление" => GetBrush("Danger", Brushes.Red),
                        _ => GetBrush("Warning", Brushes.Orange)
                    };
                    var typeBadge = new Border
                    {
                        Background = typeBrush,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 1, 6, 1),
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    typeBadge.Child = new TextBlock
                    {
                        Text = item.Type,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = GetBrush("OnAccent", Brushes.White)
                    };
                    headerPanel.Children.Add(typeBadge);

                    // Date
                    if (item.Date != default)
                    {
                        var dateText = new TextBlock
                        {
                            Text = item.Date.ToString("dd.MM.yyyy"),
                            FontSize = 11,
                            Foreground = GetBrush("TextMuted", Brushes.Gray),
                            Margin = new Thickness(8, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        headerPanel.Children.Add(dateText);
                    }

                    versionsStack.Children.Add(headerPanel);

                    // Title as first bullet (summary line)
                    if (!string.IsNullOrEmpty(item.Title) &&
                        (item.Changes == null || !item.Changes.Contains(item.Title)))
                    {
                        versionsStack.Children.Add(CreateBullet(item.Title, isBold: true));
                    }

                    // Changes list
                    if (item.Changes?.Count > 0)
                    {
                        foreach (var change in item.Changes)
                            versionsStack.Children.Add(CreateBullet(change, isBold: false));
                    }
                }

                scrollViewer.Content = versionsStack;
                changelogPanel.Children.Add(scrollViewer);
                content.Children.Add(changelogPanel);
            }
            else
            {
                // Fallback when no changelog available
                content.Children.Add(new TextBlock
                {
                    Text = "Список изменений недоступен.",
                    FontSize = 12,
                    Foreground = GetBrush("TextMuted", Brushes.Gray),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 0, 0, 16)
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = "Приложение будет перезапущено после установки.",
                FontSize = 12,
                Foreground = GetBrush("TextMuted", Brushes.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

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
            btnCancel.Click += (s, e) => { result = false; window.Close(); };

            var btnDownload = new Button
            {
                Content = "Скачать и установить",
                MinWidth = 150,
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
            btnDownload.Click += (s, e) => { result = true; window.Close(); };

            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnDownload);
            content.Children.Add(btnPanel);

            window.ShowDialog();
            return result;
        }

        public static DialogResult ShowSaveDiscardCancel(string message, string title = "Несохранённые изменения", Window? owner = null)
        {
            string result = "cancel";
            var (window, content, _) = BuildDialogBase(title, 420, owner, () => result = "cancel");

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

            window.ShowDialog();
            return result switch { "save" => DialogResult.Save, "discard" => DialogResult.Discard, _ => DialogResult.Cancel };
        }
    }
}
