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
    /// предоставляет <c>SystemParameters.ClientAreaAnimation</c>. При
    /// выключенных анимациях движение НЕ отменяется, а сжимается в ноль: токены
    /// (<see cref="Fast"/>, <see cref="Base"/>, <see cref="Slow"/>,
    /// <see cref="Emphasized"/>) возвращают <see cref="TimeSpan.Zero"/>, а
    /// <see cref="Run"/> проигрывает Storyboard со сжатым временем
    /// (<see cref="InstantSpeedRatio"/>).</para>
    ///
    /// <para><b>Почему не «просто не запускать».</b> Анимация нулевой длины всё
    /// равно доходит до конечного значения и вызывает <c>Completed</c>, а на
    /// <c>Completed</c> висит очистка: убрать тост (<c>ToastService</c>),
    /// свернуть оверлей (<c>OverlayManager</c>), спрятать кнопку
    /// (<c>AiAssistantControl</c>), закрыть панель режимов
    /// (<c>QuickAddControl.AnwisMode</c>). Если анимацию не запускать, эти
    /// элементы остаются в промежуточном состоянии навсегда — тост не исчезает,
    /// оверлей не закрывается. Факт «нулевая длина → Completed срабатывает»
    /// зафиксирован тестом <c>MotionTests.InstantAnimation_StillCompletes</c>.</para>
    ///
    /// <para>Что гейт НЕ покрывает: анимации, которые стартуют сами из
    /// XAML-триггеров (hover/pressed в стилях контролов). Они запускаются
    /// триггерами WPF, а не кодом, и остаются как есть — это зафиксировано в
    /// спецификации, а не умолчано.</para>
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
        /// Во сколько раз сжимается время Storyboard при выключенных анимациях:
        /// самый длинный шаг шкалы (320 мс) превращается в ~0.3 мс. Clock при этом
        /// доходит до конца штатно (в отличие от «не запускать вовсе»), поэтому
        /// конечное состояние применяется и <c>Completed</c> вызывается.
        /// </summary>
        private const double InstantSpeedRatio = 1000;

        /// <summary>
        /// Читает длительность из ресурсов приложения. Без приложения или без
        /// ключа возвращает ноль — это «без анимации», а не «молча неверная
        /// длительность»: мгновенный переход всегда лучше случайного числа.
        /// </summary>
        private static TimeSpan Token(string key)
        {
            // Анимации выключены пользователем — вся шкала сжимается в ноль.
            // Проверяется на каждом чтении: настройку можно поменять в системе,
            // не перезапуская приложение.
            if (ReducedMotion) return TimeSpan.Zero;

            object? value = Application.Current?.TryFindResource(key);

            // Ресурс — Duration (struct), поэтому проверяем именно тип:
            // неверный ключ не должен выглядеть как валидный TimeSpan.
            return value is Duration duration && duration.HasTimeSpan
                ? duration.TimeSpan
                : TimeSpan.Zero;
        }

        /// <summary>
        /// Запускает Storyboard, уважая системную настройку «отключить анимации»:
        /// при выключенных анимациях время сжимается, то есть переход происходит
        /// мгновенно, но <c>Completed</c> срабатывает — на нём висит очистка.
        /// Единственная точка запуска Storyboard в приложении (стережёт
        /// <c>MotionTests.NoCode_StartsStoryboardsOutsideOfMotion</c>).
        /// </summary>
        public static bool Run(Storyboard storyboard, FrameworkElement? target = null)
        {
            if (storyboard == null) return false;

            if (target != null) storyboard.Begin(target, isControllable: true);
            else storyboard.Begin();

            if (ReducedMotion)
            {
                try
                {
                    if (target != null) storyboard.SetSpeedRatio(target, InstantSpeedRatio);
                    else storyboard.SetSpeedRatio(InstantSpeedRatio);
                }
                catch (InvalidOperationException)
                {
                    // Storyboard нельзя замедлить/ускорить (запущен не как
                    // controllable) — движение просто проиграется как есть.
                    // Это не логическая ошибка: гейт — улучшение, а не условие
                    // работоспособности.
                }
            }

            return true;
        }
    }
}
