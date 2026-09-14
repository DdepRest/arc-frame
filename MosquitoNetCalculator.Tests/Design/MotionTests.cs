using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Animation;
using MosquitoNetCalculator.Helpers;
using MosquitoNetCalculator.Tests.Helpers;
using Xunit;

namespace MosquitoNetCalculator.Tests.Design
{
    /// <summary>
    /// Движение берётся из токенов и не длится дольше шкалы (v3.53.0).
    ///
    /// Правило 1 из `Tokens.Motion.xaml`: ни одна анимация не длится дольше
    /// 320 мс — дольше ощущается как тормоз, а не как отзывчивость. До v3.53.0 в
    /// разметке жило 46 литеральных Duration (0.08…0.6 с), а в коде — 30
    /// анимаций с числами на месте, включая пульс 600 мс.
    /// </summary>
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

        /// <summary>Токены длительностей из Themes/Tokens.Motion.xaml.</summary>
        private static Dictionary<string, TimeSpan> Tokens()
        {
            string path = Path.Combine(RepoRoot(), "MosquitoNetCalculator", "Themes", "Tokens.Motion.xaml");
            var result = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);

            foreach (Match m in Regex.Matches(File.ReadAllText(path),
                         "<Duration x:Key=\"(Motion\\.[A-Za-z]+)\">([^<]+)</Duration>"))
            {
                result[m.Groups[1].Value] = TimeSpan.Parse(m.Groups[2].Value.Trim(), CultureInfo.InvariantCulture);
            }

            return result;
        }

        [Fact]
        public void CodeDurations_ComeFromTheSameTokensAsXaml()
        {
            TestAppThemes.RunOnSta(() =>
            {
                var tokens = Tokens();
                Assert.Equal(4, tokens.Count);

                // Код и разметка обязаны читать ОДИН источник: иначе смена шкалы
                // меняет половину движения, а вторая половина остаётся прежней.
                Assert.Equal(tokens["Motion.Fast"], Motion.Fast);
                Assert.Equal(tokens["Motion.Base"], Motion.Base);
                Assert.Equal(tokens["Motion.Slow"], Motion.Slow);
                Assert.Equal(tokens["Motion.Emphasized"], Motion.Emphasized);
            });
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
            string appDir = Path.Combine(RepoRoot(), "MosquitoNetCalculator");
            var wrong = new List<string>();

            var literal = new Regex("Duration=\"[0-9:.]");
            var reference = new Regex("Duration=\"\\{StaticResource (Motion\\.[A-Za-z]+)\\}\"");

            foreach (string file in Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}"))
                    continue;

                string text = File.ReadAllText(file);
                string rel = Path.GetRelativePath(appDir, file).Replace('\\', '/');

                foreach (Match m in literal.Matches(text))
                    wrong.Add($"{rel}: литеральная длительность {m.Value}");

                foreach (Match m in reference.Matches(text))
                    if (!Tokens().ContainsKey(m.Groups[1].Value))
                        wrong.Add($"{rel}: {m.Groups[1].Value} — нет такого токена");
            }

            Assert.True(wrong.Count == 0,
                "Движение в разметке задано не токеном:\n  " + string.Join("\n  ", wrong));
        }

        [Fact]
        public void Run_SkipsAnimation_WhenUserDisabledThem()
        {
            // Состояние применяется вызывающей стороной до анимации, поэтому
            // «пропустить Storyboard» = мгновенный переход, а не отсутствие результата.
            Assert.Equal(!SystemParameters.ClientAreaAnimation, Motion.ReducedMotion);

            TestAppThemes.RunOnSta(() =>
            {
                // Запуск разрешён ровно тогда, когда анимации включены: гейт
                // смотрит на системную настройку, а не на собственную копию.
                Assert.Equal(!Motion.ReducedMotion, Motion.Run(new Storyboard()));

                // Пустой Storyboard — не повод падать: нет анимации, нет и запуска.
                Assert.False(Motion.Run(null!));
            });
        }
    }
}
