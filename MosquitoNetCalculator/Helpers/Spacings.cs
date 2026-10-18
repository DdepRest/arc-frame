using System.Windows;

namespace MosquitoNetCalculator.Helpers
{
    /// <summary>
    /// Отступы из дизайн-токенов (v3.53.0 / v3.54.4) для программного кода C# —
    /// те же ключи <c>Gap.*</c> и <c>Pad.*</c>, что и в <c>Themes/Tokens.Spacing.xaml</c>;
    /// контракт — <c>docs/specs/design-system-v3.53.md</c>.
    ///
    /// <para><b>Зачем.</b> В коде C# создаются динамические контейнеры, строки и кнопки
    /// (<c>TemplatesWindow</c>, <c>ToastService</c>, <c>MainWindow.Items</c>).
    /// Использование этого класса гарантирует, что динамически создаваемые контролы
    /// имеют строго шкальные отступы и синхронно реагируют на изменения дизайн-системы.</para>
    /// </summary>
    public static class Spacings
    {
        // ── Базовые и нулевые ─────────────────────────────────────────────
        public static Thickness None => Token("Gap.None", new Thickness(0));
        public static Thickness Hair => Token("Gap.Hair", new Thickness(2));
        public static Thickness HairH => Token("Gap.HairH", new Thickness(2, 0, 2, 0));
        public static Thickness HairV => Token("Gap.Separator", new Thickness(0, 2, 0, 2));

        // ── Вертикальные зазоры потока (Gap.Block*) ───────────────────────
        public static Thickness Block => Token("Gap.Block", new Thickness(0, 0, 0, 8));
        public static Thickness BlockSm => Token("Gap.BlockSm", new Thickness(0, 0, 0, 4));
        public static Thickness BlockLg => Token("Gap.BlockLg", new Thickness(0, 0, 0, 12));
        public static Thickness BlockXl => Token("Gap.BlockXl", new Thickness(0, 0, 0, 16));
        public static Thickness BlockTop => Token("Gap.BlockTop", new Thickness(0, 4, 0, 0));
        public static Thickness BlockTopSm => Token("Gap.BlockTopSm", new Thickness(0, 2, 0, 0));
        public static Thickness BlockTopLg => Token("Gap.BlockTopLg", new Thickness(0, 12, 0, 0));
        public static Thickness Top => Token("Gap.Top", new Thickness(0, 8, 0, 0));

        // ── Горизонтальные зазоры потока (Gap.Inline* / Gap.Left*) ────────
        public static Thickness Inline => Token("Gap.Inline", new Thickness(0, 0, 8, 0));
        public static Thickness InlineSm => Token("Gap.InlineSm", new Thickness(0, 0, 4, 0));
        public static Thickness InlineLg => Token("Gap.InlineLg", new Thickness(0, 0, 12, 0));
        public static Thickness Left => Token("Gap.Left", new Thickness(8, 0, 0, 0));
        public static Thickness LeftSm => Token("Gap.LeftSm", new Thickness(4, 0, 0, 0));
        public static Thickness LeftLg => Token("Gap.LeftLg", new Thickness(12, 0, 0, 0));

        // ── Внутренние поля (Padding: Pad.*) ──────────────────────────────
        public static Thickness PadTiny => Token("Pad.Tiny", new Thickness(4, 2, 4, 2));
        public static Thickness PadHair => Token("Pad.Hair", new Thickness(2, 2, 2, 2));
        public static Thickness PadControl => Token("Pad.Control", new Thickness(8, 6, 8, 6));
        public static Thickness PadControlSm => Token("Pad.ControlSm", new Thickness(4));
        public static Thickness PadButton => Token("Pad.Button", new Thickness(16, 8, 16, 8));
        public static Thickness PadButtonSm => Token("Pad.ButtonSm", new Thickness(10, 3, 10, 3));
        public static Thickness PadBadge => Token("Pad.Badge", new Thickness(8, 4, 8, 4));
        public static Thickness PadChip => Token("Pad.Chip", new Thickness(8, 3, 8, 3));
        public static Thickness PadCard => Token("Pad.Card", new Thickness(12, 10, 14, 10));

        private static Thickness Token(string key, Thickness fallback) =>
            Application.Current?.TryFindResource(key) is Thickness value
                ? value
                : fallback;
    }
}
