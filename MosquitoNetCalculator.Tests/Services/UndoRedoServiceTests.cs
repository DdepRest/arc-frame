using System.Collections.Generic;
using MosquitoNetCalculator.Models;
using MosquitoNetCalculator.Services;
using Xunit;

namespace MosquitoNetCalculator.Tests.Services
{
    [Collection("UndoRedo")]
    public class UndoRedoServiceTests
    {
        private readonly UndoRedoService _service;

        public UndoRedoServiceTests()
        {
            _service = UndoRedoService.Instance;
            _service.Clear();
        }

        [Fact]
        public void Initially_CannotUndoOrRedo()
        {
            Assert.False(_service.CanUndo);
            Assert.False(_service.CanRedo);
        }

        [Fact]
        public void Initially_IsNotDirty()
        {
            Assert.False(_service.IsDirty);
        }

        [Fact]
        public void PushUndo_EnablesCanUndo()
        {
            _service.PushUndo(() => new OrderSnapshot());
            Assert.True(_service.CanUndo);
        }

        [Fact]
        public void PushUndo_MarksDirty()
        {
            _service.MarkClean();
            _service.PushUndo(() => new OrderSnapshot());
            Assert.True(_service.IsDirty);
        }

        [Fact]
        public void Undo_ReturnsSnapshot()
        {
            _service.PushUndo(() => new OrderSnapshot { Items = new List<OrderItem> { new() { Name = "Test" } } });
            var result = _service.Undo(() => new OrderSnapshot());
            Assert.NotNull(result);
            Assert.Single(result.Items!);
            Assert.Equal("Test", result.Items[0].Name);
        }

        [Fact]
        public void Undo_DisablesCanUndo_AfterLastStep()
        {
            _service.PushUndo(() => new OrderSnapshot());
            _service.Undo(() => new OrderSnapshot());
            Assert.False(_service.CanUndo);
        }

        [Fact]
        public void Undo_EnablesCanRedo()
        {
            _service.PushUndo(() => new OrderSnapshot());
            _service.Undo(() => new OrderSnapshot());
            Assert.True(_service.CanRedo);
        }

        [Fact]
        public void Redo_ReturnsSnapshot()
        {
            _service.PushUndo(() => new OrderSnapshot());
            _service.Undo(() => new OrderSnapshot { Items = new List<OrderItem> { new() { Name = "Current" } } });
            var result = _service.Redo(() => new OrderSnapshot());
            Assert.NotNull(result);
            Assert.Single(result.Items!);
            Assert.Equal("Current", result.Items[0].Name);
        }

        [Fact]
        public void Redo_DisablesCanRedo_AfterLastStep()
        {
            _service.PushUndo(() => new OrderSnapshot());
            _service.Undo(() => new OrderSnapshot());
            _service.Redo(() => new OrderSnapshot());
            Assert.False(_service.CanRedo);
        }

        [Fact]
        public void Undo_ReturnsNull_WhenEmpty()
        {
            Assert.Null(_service.Undo(() => new OrderSnapshot()));
        }

        [Fact]
        public void Redo_ReturnsNull_WhenEmpty()
        {
            Assert.Null(_service.Redo(() => new OrderSnapshot()));
        }

        [Fact]
        public void PushUndo_ClearsRedoStack()
        {
            _service.PushUndo(() => new OrderSnapshot());
            _service.Undo(() => new OrderSnapshot());
            Assert.True(_service.CanRedo);

            _service.PushUndo(() => new OrderSnapshot());
            Assert.False(_service.CanRedo);
        }

        [Fact]
        public void Clear_ResetsEverything()
        {
            _service.PushUndo(() => new OrderSnapshot());
            _service.MarkDirty();
            _service.Clear();

            Assert.False(_service.CanUndo);
            Assert.False(_service.CanRedo);
            Assert.False(_service.IsDirty);
        }

        [Fact]
        public void MarkDirty_SetsDirtyFlag()
        {
            _service.MarkClean();
            Assert.False(_service.IsDirty);
            _service.MarkDirty();
            Assert.True(_service.IsDirty);
        }

        [Fact]
        public void MarkClean_ClearsDirtyFlag()
        {
            _service.MarkDirty();
            Assert.True(_service.IsDirty);
            _service.MarkClean();
            Assert.False(_service.IsDirty);
        }

        [Fact]
        public void SuppressDirtyChanges_PreventsDirtyFlag()
        {
            _service.MarkClean();
            _service.SuppressDirtyChanges(() =>
            {
                _service.MarkDirty();
            });
            Assert.False(_service.IsDirty);
        }

        [Fact]
        public void SuppressDirtyChanges_RestoresAfterException()
        {
            _service.MarkClean();
            try
            {
                _service.SuppressDirtyChanges(() =>
                {
                    throw new System.InvalidOperationException("test");
                });
            }
            catch { }
            // After exception, suppression should be lifted
            _service.MarkDirty();
            Assert.True(_service.IsDirty);
        }

        [Fact]
        public void MaxUndoSteps_IsEnforced()
        {
            for (int i = 0; i < 35; i++)
            {
                _service.PushUndo(() => new OrderSnapshot());
            }
            Assert.True(_service.CanUndo);

            int undoCount = 0;
            while (_service.CanUndo)
            {
                _service.Undo(() => new OrderSnapshot());
                undoCount++;
            }
            Assert.Equal(30, undoCount);
        }

        [Fact]
        public void FullCycle_PushUndo_Undo_Redo_Undo()
        {
            // PushUndo saves the CURRENT state before a change.
            // The undo stack grows: [snapshot_before_change1, snapshot_before_change2, ...]
            // Undo pops the most recent snapshot (returns to that state)
            // and pushes the current state to the redo stack.

            var snapshot1 = new OrderSnapshot { Items = new List<OrderItem> { new() { Name = "Snapshot1" } } };
            var snapshot2 = new OrderSnapshot { Items = new List<OrderItem> { new() { Name = "Snapshot2" } } };

            // Save state before change 1
            _service.PushUndo(() => snapshot1);
            // Save state before change 2
            _service.PushUndo(() => snapshot2);

            // Undo: pops snapshot2 from undo, saves current state to redo
            var undoResult = _service.Undo(() => new OrderSnapshot { Items = new List<OrderItem> { new() { Name = "Current" } } });
            Assert.NotNull(undoResult);
            Assert.Equal("Snapshot2", undoResult!.Items[0].Name);

            // Redo: pops from redo ("Current"), saves current state to undo
            var redoResult = _service.Redo(() => snapshot2);
            Assert.NotNull(redoResult);
            Assert.Equal("Current", redoResult!.Items[0].Name);

            // Undo again: pops snapshot2 from undo (redo pushed it back)
            var undoResult2 = _service.Undo(() => new OrderSnapshot { Items = new List<OrderItem> { new() { Name = "Current2" } } });
            Assert.NotNull(undoResult2);
            Assert.Equal("Snapshot2", undoResult2!.Items[0].Name);
        }

        [Fact]
        public void SetDirtyCallback_IsInvoked()
        {
            int callCount = 0;
            _service.SetDirtyCallback(() => callCount++);
            _service.MarkClean();
            _service.MarkDirty();
            Assert.Equal(1, callCount);
        }
    }
}
