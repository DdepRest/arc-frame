using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Запись журнала обновлений приложения.
    ///
    /// <para>
    /// <b>Контракт append-only:</b> Эта модель десериализуется из
    /// <c>Resources/update-log.json</c> в неизменяемом массиве —
    /// при добавлении нового релиза в JSON дописывается новая запись,
    /// старые записи остаются без изменений.
    /// </para>
    ///
    /// <para>
    /// <b>Признак «новейшая версия»</b> вычисляется runtime в
    /// <c>UpdateLog.AllNewestFirst()</c> и сохраняется в свойстве
    /// <see cref="IsLatest"/>. Оно НЕ сериализуется в JSON ([JsonIgnore]):
    /// позиция в файле не имеет значения, важен только Date + Version.
    /// Это позволяет добавлять новые записи в КОНЕЦ массива JSON без
    /// модификации существующих и без сдвига UI-привязок старых карточек.
    /// </para>
    /// </summary>
    public class UpdateItem : INotifyPropertyChanged
    {
        public DateTime Date { get; set; } = DateTime.Today;
        public string Version { get; set; } = "";
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> Changes { get; set; } = new();

        /// <summary>
        /// Типы записи для отрисовки бейджей. Строка <see cref="Type"/> может быть
        /// КОМПОЗИТОМ: «Новинка + Исправление» — релиз содержит и новое, и починенное
        /// (v3.53.1, решение владельца: смешанный релиз не должен носить бейдж одного
        /// типа). Композит парсится здесь, в одном месте; сериализуется по-прежнему
        /// только строка Type — контракт update-log.json/releases.json не меняется.
        /// Фильтр-чип «Новинки» находит запись по ЛЮБОМУ из её типов.
        /// </summary>
        public IReadOnlyList<string> Types
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Type)) return new[] { "" };
                var parts = Type.Split('+')
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .Distinct()
                    .ToArray();
                return parts.Length > 0 ? parts : new[] { Type };
            }
        }

        /// <summary>Запись относится к типу-фильтру (композит совпадает по любому из типов).</summary>
        public bool HasType(string type)
            => !string.IsNullOrEmpty(type) && Types.Contains(type, StringComparer.Ordinal);

        private bool _isLatest;

        /// <summary>
        /// True ровно у одной записи — той, что вычислена как самая свежая.
        /// Устанавливается в <c>UpdateLog.AllNewestFirst()</c> при загрузке;
        /// может переключаться в runtime при добавлении новых версий через
        /// <c>MainWindowViewModel.AddNewUpdate(...)</c>.
        /// </summary>
        /// <remarks>
        /// [JsonIgnore]: не пишется в JSON. Истинность определяется по
        /// данным (Date + Version), а не по позиции в файле — добавление
        /// новой записи в КОНЕЦ массива JSON не требует изменений существующих.
        /// </remarks>
        [JsonIgnore]
        public bool IsLatest
        {
            get => _isLatest;
            set
            {
                if (_isLatest != value)
                {
                    _isLatest = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isExpanded;

        /// <summary>
        /// Раскрыта ли карточка в «Истории обновлений» (UpdatesTabControl).
        /// Свёрнутые старые карточки показывают одну строку (версия · заголовок
        /// · дата), разворот — по клику. Как и <see cref="IsLatest"/>: runtime-
        /// состояние UI, в JSON не пишется; дефолт расставляет контрол при
        /// загрузке списка (первые N карточек раскрыты).
        /// </summary>
        [JsonIgnore]
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
