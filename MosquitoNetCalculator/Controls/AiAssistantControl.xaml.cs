using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator.Controls
{
    public partial class AiAssistantControl : UserControl
    {
        private AiAssistantViewModel? _vm;
        private bool _isAttached;

        // Per-message DispatcherTimers driving the typewriter animation for
        // bot replies. We keep references so we can stop them when the control
        // unloads (otherwise they'd keep ticking on a control that's no longer
        // in the visual tree). AnimateTyping flag on the message itself
        // decides whether a timer is created at all.
        private readonly Dictionary<AiChatMessage, DispatcherTimer> _typingTimers = new();
        private bool _scrollPending;
        private const int TypingRevealTickMs = 35;   // ~28 chars/sec when active
        private const int TypingStartDelayMs = 280;  // roughly matches fade-in

        public AiAssistantControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Loaded/Unloaded can repeat when the assistant moves between the
            // docked panel and the floating window. Keep subscriptions alive
            // without attaching the same VM twice.
            if (_isAttached) return;
            _isAttached = true;
            DataContextChanged += OnDataContextChanged;
            AttachViewModel(DataContext as AiAssistantViewModel);

            // The initial ScrollChanged can fire before Loaded. Re-evaluate after
            // layout so a restored long history exposes the jump-to-latest affordance.
            Dispatcher.BeginInvoke(UpdateScrollToBottomVisibility, DispatcherPriority.Loaded);

            // Put the caret in the composer right away so the user can start
            // typing without an extra click — both in the docked window and the
            // in-panel overlay (Loaded fires every time the control is shown).
            Dispatcher.BeginInvoke(() => TxtInput.Focus(), DispatcherPriority.Input);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_isAttached) return;
            _isAttached = false;
            DataContextChanged -= OnDataContextChanged;
            DetachViewModel();

            // Stop every in-flight typewriter timer so they don't continue
            // animating on a control that's leaving the visual tree.
            foreach (var t in _typingTimers.Values) t.Stop();
            _typingTimers.Clear();

            if (SlashPopup != null)
                SlashPopup.IsOpen = false;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachViewModel();
            AttachViewModel(e.NewValue as AiAssistantViewModel);
        }

        private void AttachViewModel(AiAssistantViewModel? vm)
        {
            _vm = vm;
            if (_vm == null) return;
            _vm.Messages.CollectionChanged += Messages_CollectionChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
        }

        private void DetachViewModel()
        {
            if (_vm == null) return;
            _vm.Messages.CollectionChanged -= Messages_CollectionChanged;
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm = null;
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var vm = _vm;
            var shouldFollow = IsAtConversationEnd();
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsLoaded || !ReferenceEquals(_vm, vm)) return;
                if (shouldFollow)
                    ChatScroll.ScrollToEnd();
                UpdateScrollToBottomVisibility();
            });

            // Subscribe to streaming messages for auto-scroll, unsubscribe removed ones.
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is AiChatMessage msg)
                        msg.PropertyChanged -= OnStreamingMessageTextChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is AiChatMessage msg && msg.IsStreaming)
                        msg.PropertyChanged += OnStreamingMessageTextChanged;
                }
            }
        }

        /// <summary>
        /// Fires every time a streaming message's Text property changes.
        /// Debounces auto-scroll to avoid layout pressure from high-frequency SSE chunks.
        /// </summary>
        private void OnStreamingMessageTextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AiChatMessage.Text)) return;

            if (_scrollPending) return;
            _scrollPending = true;

            // Schedule a single scroll per Dispatcher frame (~16ms on 60Hz), not per chunk.
            Dispatcher.BeginInvoke(() =>
            {
                _scrollPending = false;
                if (!IsLoaded) return;
                if (IsAtConversationEnd())
                    ChatScroll.ScrollToEnd();
                UpdateScrollToBottomVisibility();
            }, DispatcherPriority.Render);
        }

        private bool IsAtConversationEnd()
        {
            return ChatScroll.ScrollableHeight - ChatScroll.VerticalOffset <= 16;
        }

        private void ChatScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateScrollToBottomVisibility();
        }

        private void BtnScrollToBottom_Click(object sender, RoutedEventArgs e)
        {
            ChatScroll.ScrollToEnd();
            UpdateScrollToBottomVisibility();
        }

        private void UpdateScrollToBottomVisibility()
        {
            if (!IsLoaded || ChatScroll.ScrollableHeight <= 0)
            {
                FadeOutScrollButton();
                return;
            }

            var distanceFromBottom = ChatScroll.ScrollableHeight - ChatScroll.VerticalOffset;
            bool shouldShow = distanceFromBottom > 16;

            if (shouldShow)
                FadeInScrollButton();
            else
                FadeOutScrollButton();
        }

        private void FadeInScrollButton()
        {
            if (BtnScrollToBottom.Visibility == Visibility.Visible && BtnScrollToBottom.Opacity >= 0.95)
                return;

            BtnScrollToBottom.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(BtnScrollToBottom.Opacity, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BtnScrollToBottom.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private void FadeOutScrollButton()
        {
            if (BtnScrollToBottom.Visibility != Visibility.Visible || BtnScrollToBottom.Opacity <= 0.05)
            {
                BtnScrollToBottom.Visibility = Visibility.Collapsed;
                return;
            }

            var fadeOut = new DoubleAnimation(BtnScrollToBottom.Opacity, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fadeOut.Completed += (_, _) =>
            {
                if (BtnScrollToBottom.Opacity <= 0.05)
                    BtnScrollToBottom.Visibility = Visibility.Collapsed;
            };
            BtnScrollToBottom.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AiAssistantViewModel.IsBusy) || _vm is not { } vm)
                return;

            var shouldFollow = IsAtConversationEnd();
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsLoaded || !ReferenceEquals(_vm, vm)) return;

                // The overlay indicator is bound to IsBusy directly in XAML — no
                // code-behind write, so no timing race can hide it.
                Dispatcher.BeginInvoke(() =>
                {
                    if (!IsLoaded || !ReferenceEquals(_vm, vm)) return;
                    if (shouldFollow)
                        ChatScroll.ScrollToEnd();
                    UpdateScrollToBottomVisibility();
                }, DispatcherPriority.Loaded);
            });
        }

        /// <summary>
        /// Reads the clarification form card from the message the button belongs
        /// to and hands it to the ViewModel, which builds an AddItem command
        /// without a second LLM call.
        /// </summary>
        private void BtnSubmitClarification_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not AiChatMessage msg) return;
            _vm?.SubmitClarificationForm(msg);
        }

        /// <summary>
        /// «Повторить с другой моделью» on the clarification card → re-sends the
        /// original user request to a different free model without retyping.
        /// </summary>
        private async void BtnRetryClarification_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not AiChatMessage msg) return;
            if (_vm == null) return;
            await _vm.RetryClarification(msg);
        }

        /// <summary>«Выполнить» on the plan card → confirm and fire PlanReceived.</summary>
        private void BtnConfirmPlan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AiChatMessage msg })
                _vm?.ConfirmPlan(msg);
        }

        /// <summary>«Отмена» on the plan card → nothing executes.</summary>
        private void BtnCancelPlan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AiChatMessage msg })
                _vm?.CancelPlan(msg);
        }

        /// <summary>«Отменить действие» on an executed plan → undo the AI action.</summary>
        private void BtnUndoPlan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: AiChatMessage msg })
                _vm?.RequestUndo(msg);
        }

        private async void BtnSendOrCancel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AiAssistantViewModel vm) return;

            if (vm.IsBusy)
            {
                // Cancel the in-flight request — the SendMessageAsync loop observes _cts.
                vm.Cancel();
            }
            else
            {
                await vm.SendMessageAsync();
            }
        }

        private async void TxtInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+V with an image on the clipboard stages it as an attachment
            // instead of the TextBox swallowing the paste into thin air.
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && TryPasteImageFromClipboard())
            {
                e.Handled = true;
                return;
            }

            // While the slash-command popup is open, keyboard drives it:
            // Up/Down move the selection, Enter/Tab insert the highlighted
            // command, Esc closes without sending.
            if (SlashPopup.IsOpen)
            {
                switch (e.Key)
                {
                    case Key.Down:
                        MoveSlashSelection(+1);
                        e.Handled = true;
                        return;
                    case Key.Up:
                        MoveSlashSelection(-1);
                        e.Handled = true;
                        return;
                    case Key.Enter when Keyboard.Modifiers == ModifierKeys.None:
                        InsertSlashCommand();
                        e.Handled = true;
                        return;
                    case Key.Tab:
                        InsertSlashCommand();
                        e.Handled = true;
                        return;
                    case Key.Escape:
                        SlashPopup.IsOpen = false;
                        e.Handled = true;
                        return;
                }
            }

            // Plain Enter sends; Shift+Enter keeps the native multiline newline.
            // Handled in the TUNNELING phase (PreviewKeyDown) because the
            // TextBox with AcceptsReturn=True inserts a line break from its
            // class-level KeyDown handler, which WPF raises BEFORE instance
            // handlers — so setting e.Handled in KeyDown would be too late and
            // a stray \n would appear after every sent message.
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && DataContext is AiAssistantViewModel vm)
            {
                e.Handled = true;
                await vm.SendMessageAsync();
            }
        }

        /// <summary>
        /// Opens or updates the slash-command autocomplete popup based on the
        /// current word in the composer. Typing «/» (or a partial command)
        /// shows matching commands with descriptions from
        /// <see cref="AiLocalCommandRouter.Commands"/>.
        /// </summary>
        private void UpdateSlashSuggestions()
        {
            if (SlashPopup == null || SlashCommandList == null) return;

            var currentWord = GetCurrentWord(TxtInput.Text);
            if (!currentWord.StartsWith('/'))
            {
                SlashPopup.IsOpen = false;
                return;
            }

            var matches = AiLocalCommandRouter.Commands
                .Where(c => c.Matches(currentWord))
                .ToList();

            if (matches.Count == 0)
            {
                SlashPopup.IsOpen = false;
                return;
            }

            SlashCommandList.ItemsSource = matches;
            SlashCommandList.SelectedIndex = 0;
            SlashPopup.IsOpen = true;
        }

        /// <summary>Current word at the caret: text after the last space.</summary>
        private static string GetCurrentWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int lastSpace = text.LastIndexOf(' ');
            return lastSpace < 0 ? text : text[(lastSpace + 1)..];
        }

        private void MoveSlashSelection(int delta)
        {
            if (SlashCommandList.Items.Count == 0) return;
            int idx = SlashCommandList.SelectedIndex;
            int next = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, SlashCommandList.Items.Count - 1);
            SlashCommandList.SelectedIndex = next;
            SlashCommandList.ScrollIntoView(SlashCommandList.SelectedItem);
        }

        /// <summary>
        /// Replaces the current word (the partial «/ко…») with the full command
        /// text and keeps the caret at the end so the user can type arguments.
        /// </summary>
        private void InsertSlashCommand()
        {
            if (SlashCommandList.SelectedItem is not SlashCommandInfo cmd) return;

            var text = TxtInput.Text;
            int lastSpace = text.LastIndexOf(' ');
            var prefix = lastSpace < 0 ? string.Empty : text[..(lastSpace + 1)];

            // Insert the main command (strip the «[аргументы]» usage hint).
            var command = cmd.Command.Split(' ')[0];
            TxtInput.Text = prefix + command + " ";
            TxtInput.CaretIndex = TxtInput.Text.Length;
            SlashPopup.IsOpen = false;
            TxtInput.Focus();
        }

        private void SlashCommandList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Clicking an item inserts it; the ListBox selection is already set.
            InsertSlashCommand();
        }

        private void BtnClearChat_Click(object sender, RoutedEventArgs e)
        {
            _vm?.ClearChat();
        }

        private const long MaxAttachmentBytes = 5 * 1024 * 1024; // 5 MB per image

        /// <summary>
        /// Reads a raster image from the system clipboard (Ctrl+V) and stages it
        /// as a PNG attachment. Returns true when the paste was consumed (image
        /// added or rejected as too large), false to fall through to the normal
        /// text paste.
        /// </summary>
        private bool TryPasteImageFromClipboard()
        {
            if (DataContext is not AiAssistantViewModel vm) return false;

            BitmapSource image;
            try
            {
                if (!Clipboard.ContainsImage()) return false;
                // Copies from Word/HTML often carry BOTH text and an image —
                // keep the text paste in that case instead of dropping it.
                if (!string.IsNullOrWhiteSpace(Clipboard.GetText())) return false;
                image = Clipboard.GetImage();
            }
            catch (COMException) { return false; }
            catch (ExternalException) { return false; }

            if (image == null) return false;

            byte[] bytes;
            try
            {
                // Normalize to BGRA so PngBitmapEncoder accepts any clipboard
                // source (indexed palette, CMYK, 16-bit, …).
                var normalized = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(normalized));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                bytes = ms.ToArray();
            }
            catch (Exception ex)
            {
                ToastService.ShowToast($"Не удалось вставить изображение: {ex.Message}", ToastType.Error);
                return true;
            }

            if (bytes.Length > MaxAttachmentBytes)
            {
                ToastService.ShowToast("Изображение больше 5 МБ — не добавлено.", ToastType.Error);
                return true;
            }

            vm.AddAttachmentWithOcr(new AiImageAttachment
            {
                FileName = $"Из буфера {vm.Attachments.Count + 1}.png",
                DataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}",
                SizeLabel = FormatBytes(bytes.Length)
            });
            return true;
        }

        private void BtnAttach_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AiAssistantViewModel vm) return;

            var dialog = new OpenFileDialog
            {
                Title = "Выберите изображения",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|Все файлы|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true) return;

            foreach (var path in dialog.FileNames)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxAttachmentBytes)
                    {
                        ToastService.ShowToast($"«{info.Name}» больше 5 МБ — пропущено.", ToastType.Error);
                        continue;
                    }
                    if (info.Length == 0) continue;

                    var bytes = File.ReadAllBytes(path);
                    var mime = GetImageMime(info.Extension);
                    vm.AddAttachmentWithOcr(new AiImageAttachment
                    {
                        FileName = info.Name,
                        DataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}",
                        SizeLabel = FormatBytes(info.Length)
                    });
                }
                catch (Exception ex)
                {
                    ToastService.ShowToast(
                        $"Не удалось загрузить «{Path.GetFileName(path)}»: {ex.Message}", ToastType.Error);
                }
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AiAssistantViewModel vm) return;
            if (sender is FrameworkElement { Tag: AiImageAttachment attachment })
                vm.RemoveAttachment(attachment);
        }

        private static string GetImageMime(string extension) => extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} КБ";
            return $"{bytes / (1024.0 * 1024.0):0.#} МБ";
        }

        private void TxtInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtCharCount.Visibility = TxtInput.Text.Length > 50 ? Visibility.Visible : Visibility.Collapsed;
            UpdatePlaceholderVisibility();
            UpdateSlashSuggestions();
        }

        /// <summary>
        /// The placeholder is driven from code-behind (not a binding) so it can
        /// react to focus as well as text: it stays visible only while the field
        /// is empty AND unfocused. Clicking into the field hides it immediately,
        /// so the caret and the hint never overlap.
        /// </summary>
        private void UpdatePlaceholderVisibility()
        {
            if (TxtPlaceholder == null) return;
            TxtPlaceholder.Visibility = !TxtInput.IsKeyboardFocusWithin && string.IsNullOrWhiteSpace(TxtInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void TxtInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
            => UpdatePlaceholderVisibility();

        private void TxtInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdatePlaceholderVisibility();
            // Clicking the popup moves focus away from the TextBox — but the
            // click itself is handled on PreviewMouseLeftButtonUp, so closing
            // here (rather than on click) would swallow the selection. Delay
            // one dispatcher pass: if focus is still outside the popup, close it.
            Dispatcher.BeginInvoke(() =>
            {
                if (SlashPopup.IsOpen && !SlashCommandList.IsKeyboardFocusWithin)
                    SlashPopup.IsOpen = false;
            }, DispatcherPriority.Input);
        }

        /// <summary>
        /// Copies the message text (stored in Tag) to the system clipboard,
        /// briefly flashes the message bubble background for instant visual
        /// confirmation, and shows a toast in the AI-anchored scope.
        /// Triggered by the hover copy button on every message bubble (bot and user).
        /// </summary>
        private void BtnCopyMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string text || string.IsNullOrEmpty(text))
                return;

            try
            {
                Clipboard.SetText(text);
                FlashCopiedBubble(btn);
                ToastService.ShowToast(ToastService.AiScope, "Скопировано", ToastType.Info, durationMs: 1500);
            }
            catch (COMException)
            {
                // Clipboard occasionally throws COMException when another process
                // is reading it. Best-effort only — no toast spam.
            }
        }

        /// <summary>
        /// Walks up the visual tree from the copy <paramref name="button"/> to
        /// find the parent Grid (the message row), then locates the bubble Border
        /// in column 1 and briefly swaps its Background to AccentLight for a
        /// ~350 ms copy-confirmation flash. Best-effort only — silently no-ops if
        /// the visual tree is unexpected or resources are unavailable.
        /// </summary>
        private static async void FlashCopiedBubble(Button button)
        {
            try
            {
                var parent = (DependencyObject?)VisualTreeHelper.GetParent(button);
                while (parent is not null and not Grid)
                    parent = VisualTreeHelper.GetParent(parent);

                if (parent is not Grid rowGrid) return;

                Border? bubble = null;
                foreach (UIElement child in rowGrid.Children)
                {
                    if (child is Border b && Grid.GetColumn(child) == 1)
                    {
                        bubble = b;
                        break;
                    }
                }

                if (bubble == null) return;

                var originalBg = bubble.Background;
                var highlight = Application.Current?.FindResource("AccentLight") as Brush;
                if (highlight == null) return;

                bubble.Background = highlight;
                await System.Threading.Tasks.Task.Delay(350);
                bubble.Background = originalBg;
            }
            catch
            {
                // Best-effort visual feedback — never crash on clipboard copy.
            }
        }

        /// <summary>
        /// Typewriter-style reveal for new bot messages. Fires when the bot
        /// bubble's Border is first rendered, walks the visual tree to find
        /// the TextBlock, and progressively extends its visible prefix through
        /// a single DispatcherTimer (one slow tick for the initial delay so the
        /// fade-in animation can finish, then faster ticks for the actual
        /// reveal). We skip:
        ///  • non-bot messages (IsUser),
        ///  • messages flagged AnimateTyping=false (history-loaded + ClearChat placeholder),
        ///  • single-token messages (no animation needed).
        ///
        /// The TextBlock.Text binding is overridden by the timer (set, not
        /// binding-driven). Since AiChatMessage.Text is init-only, the
        /// override persists for the lifetime of the bubble without drift.
        /// </summary>
        private void BotBubbleBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.DataContext is not AiChatMessage msg) return;
            if (msg.IsUser || !msg.AnimateTyping) return;
            if (string.IsNullOrEmpty(msg.Text)) return;
            // Streaming messages get their text in real time via SSE —
            // no need for the post-hoc typewriter animation.
            if (msg.IsStreaming) return;
            // HasAnimated lives on the message itself (not the control), so it
            // survives closing/reopening the panel: reopening never replays the
            // typewriter for messages that were already revealed in this session.
            if (msg.HasAnimated) return;
            msg.HasAnimated = true;

            var textBlock = FindMessageTextBlock(border, msg);
            if (textBlock == null) return;

            // Regex split with a capture group keeps the whitespace separators
            // in the result, so the typewriter reveal preserves the original
            // spacing (including \n between paragraphs).
            var tokens = Regex.Split(msg.Text, @"(\s+)");
            if (tokens.Length <= 1) return;

            // Anti-flash guard: the TextBlock is bound to the FULL message text,
            // so without this the complete reply would render immediately and stay
            // visible through the fade-in + initial timer delay — then get wiped
            // and re-typed (the user sees the whole message flash, disappear, and
            // be typed out again). Clearing synchronously at Loaded means the
            // bubble fades in empty and the reveal starts from the first token;
            // the parent Grid's own Opacity fade (0→1) guarantees no frame ever
            // paints the full text. Single-token messages are handled above (they
            // keep the full text, no animation needed).
            textBlock.Text = string.Empty;

            StartTypingReveal(textBlock, msg, tokens);
        }

        private void StartTypingReveal(TextBlock textBlock, AiChatMessage msg, string[] tokens)
        {
            // Single timer with two phases:
            //  • phase 0: wait TypingStartDelayMs (so the bubble fade-in finishes
            //    over the now-EMPTY text — the anti-flash guard in
            //    BotBubbleBorder_Loaded already cleared the bound full text),
            //  • phase 1+: tick every TypingRevealTickMs and extend the visible
            //    prefix by one token; snap to exact source text on the last tick.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TypingStartDelayMs) };
            _typingTimers[msg] = timer;

            int phase = 0;
            timer.Tick += (_, _) =>
            {
                if (phase == 0)
                {
                    phase = 1;
                    timer.Interval = TimeSpan.FromMilliseconds(TypingRevealTickMs);
                    textBlock.Text = tokens[0];
                    return;
                }
                int nextReveal = phase + 1;
                // string.Join(sep, tokens, start, count) — NOT string.Concat,
                // which has no (string[], int, int) overload and silently falls
                // back to Concat(object[]), printing "System.String[]010" etc.
                textBlock.Text = string.Join(string.Empty, tokens, 0, nextReveal);
                phase = nextReveal;
                if (phase >= tokens.Length)
                {
                    // Final tick — snap to formatted markdown and stop.
                    // During animation we showed raw text via TextBlock.Text
                    // (which clears Inlines). Now swap to parsed markdown
                    // so **bold**, *italic*, `code`, bullets render correctly.
                    MarkdownRenderer.ParseToInlines(msg.Text, textBlock);
                    timer.Stop();
                    _typingTimers.Remove(msg);
                }
            };
            timer.Start();
        }

        /// <summary>
        /// Walks the WPF visual tree depth-first and returns the first descendant
        /// TextBlock whose <c>Tag</c> reference matches the supplied message.
        /// The bot bubble's Border contains multiple TextBlocks (ActionCard icon,
        /// ActionCard summary, message text, timestamp) — anchoring on Tag
        /// disambiguates them so the typewriter only ever animates the message
        /// TextBlock, never the icon glyph or ActionCard summary.
        /// </summary>
        private static TextBlock? FindMessageTextBlock(DependencyObject? parent, AiChatMessage msg)
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb && ReferenceEquals(tb.Tag, msg))
                    return tb;
                var found = FindMessageTextBlock(child, msg);
                if (found != null) return found;
            }
            return null;
        }
    }
}
