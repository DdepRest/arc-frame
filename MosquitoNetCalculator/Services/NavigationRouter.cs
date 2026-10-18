using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Реализация маршрутизатора представлений и докируемых инспекторов в парадигме Studio Workspace.
    /// </summary>
    public class NavigationRouter : INavigationRouter
    {
        private readonly Dictionary<Type, (object Vm, string Tag)> _viewModels = new();
        private object? _currentWorkspace;
        private object? _currentInspector;
        private bool _isInspectorOpen;
        private string _activeTag = "Calc";

        public event PropertyChangedEventHandler? PropertyChanged;

        public object? CurrentWorkspace
        {
            get => _currentWorkspace;
            private set => SetField(ref _currentWorkspace, value);
        }

        public object? CurrentInspector
        {
            get => _currentInspector;
            private set => SetField(ref _currentInspector, value);
        }

        public bool IsInspectorOpen
        {
            get => _isInspectorOpen;
            set => SetField(ref _isInspectorOpen, value);
        }

        public string ActiveTag
        {
            get => _activeTag;
            private set => SetField(ref _activeTag, value);
        }

        public void Register<TViewModel>(TViewModel instance, string tag) where TViewModel : class
        {
            _viewModels[typeof(TViewModel)] = (instance, tag);
        }

        public void NavigateTo<TViewModel>() where TViewModel : class
        {
            if (_viewModels.TryGetValue(typeof(TViewModel), out var entry))
            {
                CurrentWorkspace = entry.Vm;
                ActiveTag = entry.Tag;
            }
        }

        public void NavigateTo(object viewModel, string? tag = null)
        {
            CurrentWorkspace = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            if (!string.IsNullOrEmpty(tag))
                ActiveTag = tag;
        }

        public void ToggleInspector(object? inspector = null)
        {
            if (inspector != null && !ReferenceEquals(CurrentInspector, inspector))
            {
                CurrentInspector = inspector;
                IsInspectorOpen = true;
                return;
            }

            IsInspectorOpen = !IsInspectorOpen;
        }

        public void CloseInspector()
        {
            IsInspectorOpen = false;
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
