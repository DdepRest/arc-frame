using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    public class UndoRedoService
    {
        // Singleton: lives for the entire app lifetime. If multiple windows are ever
        // created, each must call SetDirtyCallback and Clear to avoid stale references.
        private static readonly Lazy<UndoRedoService> _instance = new(() => new UndoRedoService());
        public static UndoRedoService Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly Stack<OrderSnapshot> _undoStack = new();
        private readonly Stack<OrderSnapshot> _redoStack = new();
        private const int MaxUndoSteps = 30;
        private bool _isDirty;
        private bool _suppressDirty;
        private Action? _onDirtyChanged;

        public bool CanUndo { get { lock (_lock) return _undoStack.Count > 0; } }
        public bool CanRedo { get { lock (_lock) return _redoStack.Count > 0; } }
        public bool IsDirty { get { lock (_lock) return _isDirty; } }

        private UndoRedoService() { }

        public void PushUndo(Func<OrderSnapshot> snapshotFactory)
        {
            var snapshot = snapshotFactory();
            PushUndo(snapshot);
        }

        /// <summary>
        /// Returns the top of the undo stack without popping it, or null if empty.
        /// Used by MainWindow.PushUndo to detect and skip duplicate snapshots.
        /// </summary>
        public bool TryPeekTopSnapshot(out OrderSnapshot? snapshot)
        {
            lock (_lock)
            {
                if (_undoStack.Count == 0) { snapshot = null; return false; }
                snapshot = _undoStack.Peek();
                return true;
            }
        }

        public void PushUndo(OrderSnapshot snapshot)
        {
            lock (_lock)
            {
                _undoStack.Push(snapshot);
                if (_undoStack.Count > MaxUndoSteps)
                {
                    // Remove the oldest snapshot by rebuilding the stack without the bottom element.
                    // Stack<T> enumerates newest-first, so iterating directly keeps the newest MaxUndoSteps
                    // entries and drops the oldest.
                    var temp = new OrderSnapshot[MaxUndoSteps];
                    int i = 0;
                    foreach (var item in _undoStack)
                    {
                        if (i >= MaxUndoSteps) break;
                        temp[i++] = item;
                    }
                    _undoStack.Clear();
                    for (int j = MaxUndoSteps - 1; j >= 0; j--)
                        _undoStack.Push(temp[j]);
                }
                _redoStack.Clear();
            }
            MarkDirty();
        }

        public OrderSnapshot? Undo(Func<OrderSnapshot> snapshotFactory)
        {
            // Note: snapshotFactory() is called outside the lock. In WPF this is safe
            // because all UI interaction is serialized on the Dispatcher thread.
            OrderSnapshot? result;
            OrderSnapshot currentState;
            lock (_lock)
            {
                if (_undoStack.Count == 0) return null;
                result = _undoStack.Pop();
            }
            currentState = snapshotFactory();
            lock (_lock)
            {
                _redoStack.Push(currentState);
            }
            return result;
        }

        public OrderSnapshot? Redo(Func<OrderSnapshot> snapshotFactory)
        {
            OrderSnapshot? result;
            OrderSnapshot currentState;
            lock (_lock)
            {
                if (_redoStack.Count == 0) return null;
                result = _redoStack.Pop();
            }
            currentState = snapshotFactory();
            lock (_lock)
            {
                _undoStack.Push(currentState);
            }
            return result;
        }

        public void MarkClean()
        {
            Action? callback = null;
            lock (_lock)
            {
                if (_isDirty)
                {
                    _isDirty = false;
                    callback = _onDirtyChanged;
                }
            }
            callback?.Invoke();
        }

        public void MarkDirty()
        {
            Action? callback = null;
            lock (_lock)
            {
                if (!_suppressDirty && !_isDirty)
                {
                    _isDirty = true;
                    callback = _onDirtyChanged;
                }
            }
            callback?.Invoke();
        }

        public void Clear()
        {
            bool wasDirty;
            lock (_lock)
            {
                _undoStack.Clear();
                _redoStack.Clear();
                wasDirty = _isDirty;
                _isDirty = false;
            }
            if (wasDirty) _onDirtyChanged?.Invoke();
        }

        public void SuppressDirtyChanges(Action action)
        {
            lock (_lock) { _suppressDirty = true; }
            try { action(); }
            finally { lock (_lock) { _suppressDirty = false; } }
        }

        public void SetDirtyCallback(Action onDirtyChanged)
        {
            lock (_lock) { _onDirtyChanged = onDirtyChanged; }
        }
    }
}
