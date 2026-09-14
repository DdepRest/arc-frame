using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Data;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Controls
{
    /// <summary>
    /// Чистая логика списка «История обновлений» (UpdatesTabControl),
    /// вынесенная из code-behind для тестируемости: группировка по годам,
    /// предикат фильтра (чипы типа + поиск), формат счётчика, правило
    /// «какие карточки раскрыты» и текст для кнопки «Скопировать».
    ///
    /// Никакого WPF-состояния — только функции над данными и CollectionView.
    /// </summary>
    public static class UpdatesListLogic
    {
        /// <summary>Сколько верхних (самых свежих) карточек раскрыто по умолчанию.</summary>
        public const int DefaultExpandedCards = 5;

        // ─── Группировка по годам ───────────────────────────────────────────

        /// <summary>
        /// Настраивает представление списка: группировка по году карточки
        /// («2026», «2025», …). Представление кэшируется WPF на коллекцию —
        /// повторные вызовы не дублируют описание группы. Фильтр сбрасывается.
        /// </summary>
        public static ListCollectionView ConfigureView(ObservableCollection<UpdateItem> updates)
        {
            var view = (ListCollectionView)CollectionViewSource.GetDefaultView(updates);
            if (view.GroupDescriptions.Count == 0)
                view.GroupDescriptions.Add(new PropertyGroupDescription("Date.Year"));
            view.Filter = null;
            return view;
        }

        // ─── Чипы-фильтры: взаимоисключающая логика выбора ───────────

        /// <summary>
        /// Какой чип должен быть активен после клика. Ровно один всегда:
        /// повторный клик по активному чипу-типу (или клик по «Все»)
        /// возвращает «Все» (пустая строка); клик по неактивному чипу
        /// выбирает его. Чистая функция — покрыта юнит-тестами.
        /// </summary>
        public static string ResolveChipSelection(string clickedChip, string activeChip)
        {
            if (clickedChip == "Все") return "";
            return clickedChip == activeChip ? "" : clickedChip;
        }

        // ─── Фильтр: чип типа + поисковый запрос ────────────────────────────

        /// <summary>
        /// Предикат фильтра карточек: по типу («Новинка»/«Улучшение»/«Исправление»,
        /// пустая строка/_null = все) и подстроке запроса — версия, заголовок или
        /// текст пунктов изменений (без учёта регистра). Без обоих условий —
        /// пропускает всё.
        /// </summary>
        public static Func<UpdateItem, bool> BuildPredicate(string? typeFilter, string? query)
        {
            bool byType = !string.IsNullOrEmpty(typeFilter);
            string q = query?.Trim() ?? string.Empty;
            bool byQuery = q.Length > 0;

            if (!byType && !byQuery)
                return _ => true;

            return item =>
            {
                if (item == null) return false;
                if (byType && !string.Equals(item.Type, typeFilter, StringComparison.Ordinal))
                    return false;
                if (!byQuery) return true;
                if (Contains(item.Version, q)) return true;
                if (Contains(item.Title, q)) return true;
                if (item.Changes != null)
                    foreach (var change in item.Changes)
                        if (Contains(change, q)) return true;
                return false;
            };
        }

        private static bool Contains(string? haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) &&
            haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        // ─── Счётчик в шапке ────────────────────────────────────────────────

        /// <summary>
        /// Текст счётчика версий: без активного фильтра — «N версий»
        /// (русская плюрализация), при фильтре — «N из M версий».
        /// </summary>
        public static string CountText(int filtered, int total)
        {
            if (filtered == total)
                return ClassicCountText(total);
            return $"{filtered} из {total} {PluralVersions(total)}";
        }

        /// <summary>Русская плюрализация: 1 версия, 2-4 версии, 5+ версий
        /// (с учётом особой формы 11-14: 11 версий, 12 версий, …).</summary>
        public static string ClassicCountText(int count)
        {
            return $"{count} {PluralVersions(count)}";
        }

        private static string PluralVersions(int count)
        {
            int rem10 = count % 10;
            int rem100 = count % 100;
            if (rem100 is >= 11 and <= 14) return "версий";
            return rem10 switch
            {
                1 => "версия",
                2 or 3 or 4 => "версии",
                _ => "версий"
            };
        }

        // ─── Свёрнутые карточки ─────────────────────────────────────────────

        /// <summary>
        /// Правило по умолчанию: первые <see cref="DefaultExpandedCards"/>
        /// (самые свежие) карточки раскрыты, остальные свёрнуты в одну строку.
        /// </summary>
        public static void ApplyExpandedDefaults(
            ObservableCollection<UpdateItem> updates, int expandFirst = DefaultExpandedCards)
        {
            if (updates == null) return;
            for (int i = 0; i < updates.Count; i++)
                updates[i].IsExpanded = i < expandFirst;
        }

        /// <summary>Раскрыть/свернуть все карточки (используется при активном
        /// поиске: отфильтрованные карточки показываются раскрытыми).</summary>
        public static void SetAllExpanded(ObservableCollection<UpdateItem> updates, bool expanded)
        {
            if (updates == null) return;
            foreach (var item in updates)
                item.IsExpanded = expanded;
        }

        // ─── Кнопка «Скопировать» ───────────────────────────────────────────

        /// <summary>
        /// Текст карточки для пересылки в чат: «v 3.49.0 — Заголовок», затем
        /// пункты изменений списком. Пустые пункты пропускаются.
        /// </summary>
        public static string BuildCopyText(UpdateItem? item)
        {
            if (item == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("v ").Append(item.Version).Append(" — ").Append(item.Title).Append('\n');
            if (item.Changes != null)
                foreach (var change in item.Changes)
                    if (!string.IsNullOrWhiteSpace(change))
                        sb.Append("• ").Append(change.Trim()).Append('\n');
            return sb.ToString().TrimEnd();
        }
    }
}
