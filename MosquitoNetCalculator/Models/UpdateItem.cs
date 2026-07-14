using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
