using System;
using System.ComponentModel;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Контракт сервиса маршрутизации рабочих областей (Workspaces) и контекстных инспекторов.
    /// </summary>
    public interface INavigationRouter : INotifyPropertyChanged
    {
        /// <summary>Активная рабочая область (Workspace).</summary>
        object? CurrentWorkspace { get; }

        /// <summary>Активный контекстный инспектор (боковая док-панель справа).</summary>
        object? CurrentInspector { get; }

        /// <summary>Признак открытости док-инспектора.</summary>
        bool IsInspectorOpen { get; set; }

        /// <summary>Текущий активный навигационный тег (Calc, Orders, Prices, Slope, etc.).</summary>
        string ActiveTag { get; }

        /// <summary>Регистрация рабочей области.</summary>
        void Register<TViewModel>(TViewModel instance, string tag) where TViewModel : class;

        /// <summary>Переход к рабочей области по типу ViewModel.</summary>
        void NavigateTo<TViewModel>() where TViewModel : class;

        /// <summary>Переход к рабочей области по экземпляру ViewModel.</summary>
        void NavigateTo(object viewModel, string? tag = null);

        /// <summary>Открыть или переключить инспектор.</summary>
        void ToggleInspector(object? inspector = null);

        /// <summary>Закрыть инспектор.</summary>
        void CloseInspector();
    }
}
