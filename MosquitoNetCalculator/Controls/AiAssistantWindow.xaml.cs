using System;
using System.Windows;
using MosquitoNetCalculator.ViewModels;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Separate tool window for the AI assistant.
    /// Allows the user to position the AI chat alongside the main application window.
    /// </summary>
    public partial class AiAssistantWindow : Window
    {
        public AiAssistantWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets or retrieves the ViewModel backing the AI assistant control.
        /// </summary>
        public AiAssistantViewModel? ViewModel
        {
            get => DataContext as AiAssistantViewModel;
            set => DataContext = value;
        }

        /// <summary>
        /// Positions this window docked flush against the RIGHT edge of the owner
        /// window, aligned with the owner's top and mirroring its height — the
        /// panel is BOUND to the program and follows it. Everything is clamped
        /// into the monitor's working area so the window can never end up off-screen.
        /// </summary>
        public void PositionNextTo(Window owner)
        {
            if (owner == null) return;

            var helper = new System.Windows.Interop.WindowInteropHelper(owner);
            var handle = helper.Handle == IntPtr.Zero ? helper.EnsureHandle() : helper.Handle;

            var screen = System.Windows.Forms.Screen.FromHandle(handle);
            if (screen == null)
            {
                // No monitor info — fall back to the PRIMARY screen's working area.
                // Must be FINITE (not ±Infinity) so Window.Top/Left never become NaN.
                var wa = SystemParameters.WorkArea;
                PositionNextToCore(owner.Left, owner.Top, owner.Width, owner.Height,
                                   wa.Left, wa.Top, wa.Right, wa.Bottom);
                return;
            }

            var workingArea = screen.WorkingArea;
            PositionNextToCore(owner.Left, owner.Top, owner.Width, owner.Height,
                               workingArea.Left, workingArea.Top, workingArea.Right, workingArea.Bottom);
        }

        /// <summary>
        /// Closes this window when the user clicks the header close button.
        /// </summary>
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Core positioning logic, extracted for unit testing without requiring
        /// a real window handle or screen geometry.
        ///
        /// Strategy:
        ///  1. Height mirrors the owner's height, clamped to the working area
        ///     (so the docked panel feels like part of the main window).
        ///  2. Docked flush to the RIGHT edge of the owner — the AI window is
        ///     BOUND to the program and follows it; it never floats away,
        ///     self-centers, or flips to the left side.
        ///  3. Top aligns with the OWNER's top (not screen-centered) — the panel
        ///     is attached to the program. Centering of the whole group
        ///     «program + dock» is handled by MainWindow when the dock opens.
        ///  4. Final clamp on ALL four sides into the working area — the window
        ///     stays attached and can never be partially off the monitor.
        /// </summary>
        internal void PositionNextToCore(double ownerLeft, double ownerTop, double ownerWidth, double ownerHeight,
                                         double screenLeft, double screenTop, double screenRight, double screenBottom)
        {
            // Height mirrors the owner, clamped to the vertical working area.
            double availableHeight = screenBottom - screenTop;
            Height = Math.Max(MinHeight, Math.Min(ownerHeight, availableHeight));

            // Docked to the right edge of the owner, aligned with its top.
            double left = ownerLeft + ownerWidth;
            double top = ownerTop;

            // Final clamp into the working area on all sides.
            left = Math.Max(screenLeft, Math.Min(left, screenRight - Width));
            top = Math.Max(screenTop, Math.Min(top, screenBottom - Height));

            Left = left;
            Top = top;
        }
    }
}
