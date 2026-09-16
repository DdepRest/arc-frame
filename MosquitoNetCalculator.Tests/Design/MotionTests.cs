using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Движение берётся из токенов, не длится дольше шкалы и уважает системную
    /// настройку «отключить анимации» (v3.53.0).
    ///
    /// Правило 1 из `Tokens.Motion.xaml`: ни одна анимация не длится дольше
    /// 320 мс — дольше ощущается как тормоз, а не как отзывчивость. До v3.53.0 в
    /// разметке жило 46 литеральных Duration (0.08…0.6 с), а в коде — 30
    /// анимаций с числами на месте, включая пульс 600 мс.
    ///
    /// Правило 3 — гейт `Motion.Run`/токены: при выключенных анимациях движение
    /// СЖИМАЕТСЯ В НОЛЬ, а не отменяется, потому что на `Completed` висит очистка
    /// (тост, оверлей, кнопка). Первая версия гейта просто не запускала
    /// Storyboard и вызывалась ровно из ниоткуда: обещание в комментарии, а не в
    /// коде. Здесь это закреплено двумя проверками — на самом факте (нулевая
    /// длина всё равно завершается) и на единственной точке запуска.
    /// </summary>
    [Collection("WPF_UI")]
    public class MotionTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MosquitoNetCalculator", "App.xaml")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Не найден корень репозитория.");
        }

        private static string AppDir => Path.Combine(RepoRoot(), "MosquitoNetCalculator");

        private static IEnumerable<string> ProductCs() =>
            Directory.EnumerateFiles(AppDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                            && !p.Contains($"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}"));

        /// <summary>Токены длительностей из Themes/Tokens.Motion.xaml.</summary>
        private static Dictionary<string, TimeSpan> Tokens()
        {
            string path = Path.Combine(AppDir, "Themes", "Tokens.Motion.xaml");
            var result = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);

            foreach (Match m in Regex.Matches(File.ReadAllText(path),
                         "<Duration x:Key=\"(Motion\\.[A-Za-z]+)\">([^<]+)</Duration>"))
            {
                result[m.Groups[1].Value] = TimeSpan.Parse(m.Groups[2].Value.Trim(), CultureInfo.InvariantCulture);
            }

            return result;
        }

        private static void Pump(int frames = 10)
        {
            for (int i = 0; i < frames; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(8);
            }
        }

        [Fact]
        public void CodeDurations_ComeFromTheSameTokensAsXaml()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var tokens = Tokens();
                Assert.Equal(4, tokens.Count);

                if (Motion.ReducedMotion)
                {
                    // Анимации выключены системной настройкой — шкала сжимается
                    // в ноль целиком (движение становится мгновенным).
                    Assert.Equal(TimeSpan.Zero, Motion.Fast);
                    Assert.Equal(TimeSpan.Zero, Motion.Base);
                    Assert.Equal(TimeSpan.Zero, Motion.Slow);
                    Assert.Equal(TimeSpan.Zero, Motion.Emphasized);
                    return;
                }

                // Код и разметка обязаны читать ОДИН источник: иначе смена шкалы
                // меняет половину движения, а вторая половина остаётся прежней.
                Assert.Equal(tokens["Motion.Fast"], Motion.Fast);
                Assert.Equal(tokens["Motion.Base"], Motion.Base);
                Assert.Equal(tokens["Motion.Slow"], Motion.Slow);
                Assert.Equal(tokens["Motion.Emphasized"], Motion.Emphasized);
            });
        }

        [Fact]
        public void ReducedMotion_ReflectsTheSystemSetting()
        {
            // Гейт смотрит на системную настройку, а не на собственную копию.
            Assert.Equal(!SystemParameters.ClientAreaAnimation, Motion.ReducedMotion);
        }

        [Fact]
        public void NoDuration_ExceedsTheScaleCeiling()
        {
            var tokens = Tokens();

            // 320 мс — потолок шкалы: длиннее = «тормоз».
            var tooLong = tokens.Where(kv => kv.Value > TimeSpan.FromMilliseconds(320)).ToList();
            Assert.True(tooLong.Count == 0,
                "Длительность длиннее потолка шкалы (320 мс): " +
                string.Join(", ", tooLong.Select(kv => $"{kv.Key}={kv.Value.TotalMilliseconds}мс")));

            Assert.Equal(TimeSpan.FromMilliseconds(320), tokens["Motion.Emphasized"]);
            Assert.True(tokens["Motion.Fast"] < tokens["Motion.Base"]);
            Assert.True(tokens["Motion.Base"] < tokens["Motion.Slow"]);
            Assert.True(tokens["Motion.Slow"] < tokens["Motion.Emphasized"]);
        }

        [Fact]
        public void EveryXamlDuration_ReferencesAMotionToken()
        {
            var wrong = new List<string>();

            var literal = new Regex("Duration=\"[0-9:.]");
            var reference = new Regex("Duration=\"\\{StaticResource (Motion\\.[A-Za-z]+)\\}\"");

            foreach (string file in Directory.EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}"))
                    continue;

                string text = File.ReadAllText(file);
                string rel = Path.GetRelativePath(AppDir, file).Replace('\\', '/');

                foreach (Match m in literal.Matches(text))
                    wrong.Add($"{rel}: литеральная длительность {m.Value}");

                foreach (Match m in reference.Matches(text))
                    if (!Tokens().ContainsKey(m.Groups[1].Value))
                        wrong.Add($"{rel}: {m.Groups[1].Value} — нет такого токена");
            }

            Assert.True(wrong.Count == 0,
                "Движение в разметке задано не токеном:\n  " + string.Join("\n  ", wrong));
        }

        /// <summary>
        /// Инвариант, на котором стоит весь гейт: анимация НУЛЕВОЙ длины доходит до
        /// конечного значения и вызывает <c>Completed</c>. Если это перестанет быть
        /// верным, «мгновенный переход» превратится в «эффект не применился»:
        /// тост не исчезнет, оверлей не свернётся, карточка останется с Opacity=0.
        /// </summary>
        [Fact]
        public void InstantAnimation_StillCompletes()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var border = new Border { Width = 10, Height = 10, Opacity = 1.0 };
                var host = new Window
                {
                    Content = border,
                    Width = 60,
                    Height = 60,
                    ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -30000,
                    Top = -30000,
                };
                host.Show();

                try
                {
                    bool propertyCompleted = false;
                    var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.Zero);
                    fade.Completed += (_, _) => propertyCompleted = true;
                    border.BeginAnimation(UIElement.OpacityProperty, fade);
                    Pump();

                    Assert.True(propertyCompleted,
                        "Completed у анимации нулевой длины не сработал — очистка (снять тост, свернуть оверлей) не выполнится.");
                    Assert.Equal(0.0, border.Opacity, 3);

                    bool storyboardCompleted = false;
                    var storyboard = new Storyboard();
                    var appear = new DoubleAnimation(0.0, 1.0, TimeSpan.Zero);
                    Storyboard.SetTarget(appear, border);
                    Storyboard.SetTargetProperty(appear, new PropertyPath(UIElement.OpacityProperty));
                    storyboard.Children.Add(appear);
                    storyboard.Completed += (_, _) => storyboardCompleted = true;
                    storyboard.Begin();
                    Pump();

                    Assert.True(storyboardCompleted, "Storyboard нулевой длины не сообщил о завершении.");
                    Assert.Equal(1.0, border.Opacity, 3);
                }
                finally
                {
                    host.Close();
                }
            });
        }

        [Fact]
        public void Run_NeverCancelsMotion_OnlyCompressesIt()
        {
            TestAppThemes.RunOnSta(() =>
            {
                // Гейт не имеет права вернуть «не запускал»: именно на этом
                // вызывающая сторона строила бы ошибочный вывод, что очистку
                // выполнять не нужно. Пустой Storyboard тоже завершается.
                Assert.True(Motion.Run(new Storyboard()));
                Assert.False(Motion.Run(null!));
            });
        }

        [Fact]
        public void NoCode_StartsStoryboardsOutsideOfMotion()
        {
            var offenders = new List<string>();

            foreach (string file in ProductCs())
            {
                string rel = Path.GetRelativePath(AppDir, file).Replace('\\', '/');
                if (rel.Equals("Helpers/Motion.cs", StringComparison.OrdinalIgnoreCase)) continue;

                string text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(text, "\\.Begin\\("))
                    offenders.Add($"{rel}: {m.Value}");
            }

            Assert.True(offenders.Count == 0,
                "Storyboard запускается напрямую — мимо гейта reduced-motion (Helpers/Motion.Run):\n  " +
                string.Join("\n  ", offenders));
        }
    }
}
