using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace MosquitoNetCalculator.Helpers
{
    /// <summary>
    /// Движение из токенов (v3.53.0). Контракт — `Themes/Tokens.Motion.xaml`
    /// и `docs/specs/design-system-v3.53.md` §4.
    ///
    /// <para><b>Зачем.</b> До v3.53.0 длительности в коде задавались числами на
    /// месте: 50 / 80 / 120 / 150 / 160 / 180 / 200 / 220 / 250 / 280 / 300 /
    /// 400 / 600 мс — четыре «почти 150» и одна анимация длиннее полусекунды.
    /// Теперь код читает те же токены, что и разметка, поэтому смена шкалы в
    /// одном файле меняет движение всего приложения.</para>
    ///
    /// <para><b>Уважение к настройке «отключить анимации».</b> Windows
    /// предоставляет `SystemParameters.ClientAreaAnimation`; при выключенных
    /// анимациях `Run` не запускает Storyboard — состояние применяется
    /// мгновенно (свойства уже выставлены вызывающей стороной, а анимация лишь
    /// анимирует переход к ним).</para>
    /// </summary>
    public static class Motion
    {
        /// <summary>Мгновенный отклик: hover, pressed, фокус (токен `Motion.Fast`).</summary>
        public static TimeSpan Fast => Token("Motion.Fast");

        /// <summary>Базовый переход: появление чипа, раскрытие карточки, смена цвета.</summary>
        public static TimeSpan Base => Token("Motion.Base");

        /// <summary>Смена темы, swap содержимого панели.</summary>
        public static TimeSpan Slow => Token("Motion.Slow");

        /// <summary>Выезд оверлея/панели целиком — самый длинный шаг шкалы.</summary>
        public static TimeSpan Emphasized => Token("Motion.Emphasized");

        /// <summary>Системная настройка «отключить анимации» (специальные возможности).</summary>
        public static bool ReducedMotion => !SystemParameters.ClientAreaAnimation;

        /// <summary>
        /// Читает длительность из ресурсов приложения. Без приложения или без
        /// ключа возвращает ноль — это «без анимации», а не «молча неверная
        /// длительность»: мгновенный переход всегда лучше случайного числа.
        /// </summary>
        private static TimeSpan Token(string key)
        {
            object? value = Application.Current?.TryFindResource(key);

            // Ресурс — Duration (struct), поэтому проверяем именно тип:
            // неверный ключ не должен выглядеть как валидный TimeSpan.
            return value is Duration duration && duration.HasTimeSpan
                ? duration.TimeSpan
                : TimeSpan.Zero;
        }

        /// <summary>
        /// Запускает Storyboard, если пользователь не отключил анимации.
        /// Возвращает false, если анимация пропущена (полезно в тестах).
        /// </summary>
        public static bool Run(Storyboard storyboard, FrameworkElement? target = null)
        {
            if (storyboard == null) return false;
            if (ReducedMotion) return false;

            if (target != null) storyboard.Begin(target, isControllable: true);
            else storyboard.Begin();

            return true;
        }
    }
}
