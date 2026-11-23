using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem
{
    /// <summary>
    /// Represents a reversible drawing command that can be executed and undone.
    /// </summary>
    public interface IDrawCommand
    {
        /// <summary>
        /// Executes (or re-executes) the command.
        /// </summary>
        void Execute();

        /// <summary>
        /// Reverses the effect of <see cref="Execute"/>.
        /// </summary>
        void Undo();
    }

    /// <summary>
    /// Command that records a stroke into a <see cref="StrokeRecorder"/>
    /// and invokes a render callback to update the visual representation.
    /// </summary>
    public class DrawStrokeCommand : IDrawCommand
    {
        private readonly StrokeData _strokeData;
        private readonly StrokeRecorder _recorder;
        private readonly Action _onRender;

        /// <summary>
        /// Creates a new draw-stroke command.
        /// </summary>
        /// <param name="strokeData">The stroke data to record.</param>
        /// <param name="recorder">The <see cref="StrokeRecorder"/> that manages stroke history.</param>
        /// <param name="onRender">Callback invoked after execute/undo to refresh the canvas.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="strokeData"/> or <paramref name="recorder"/> is <c>null</c>.
        /// </exception>
        public DrawStrokeCommand(StrokeData strokeData, StrokeRecorder recorder, Action onRender)
        {
            _strokeData = strokeData ?? throw new ArgumentNullException(nameof(strokeData));
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            _onRender = onRender;
        }

        /// <inheritdoc />
        public void Execute()
        {
            _recorder.AddStrokeData(_strokeData);
            _onRender?.Invoke();
        }

        /// <inheritdoc />
        public void Undo()
        {
            _recorder.RemoveLastStroke();
            _onRender?.Invoke();
        }
    }

    /// <summary>
    /// Command that clears all strokes on a specific layer, capturing a snapshot
    /// of the layer texture so the operation can be reversed.
    /// </summary>
    public class ClearLayerCommand : IDrawCommand
    {
        private readonly int _layerIndex;
        private readonly RenderTexture _layerTexture;
        private Texture2D _snapshot;
        private readonly Action _onRender;

        /// <summary>
        /// Creates a new clear-layer command.
        /// </summary>
        /// <param name="layerIndex">Index of the layer to clear.</param>
        /// <param name="layerTexture">The <see cref="RenderTexture"/> backing the layer.</param>
        /// <param name="onRender">Callback invoked after execute/undo to refresh the canvas.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="layerTexture"/> is <c>null</c>.
        /// </exception>
        public ClearLayerCommand(int layerIndex, RenderTexture layerTexture, Action onRender)
        {
            _layerIndex = layerIndex;
            _layerTexture = layerTexture ?? throw new ArgumentNullException(nameof(layerTexture));
            _onRender = onRender;
        }

        /// <inheritdoc />
        public void Execute()
        {
            // Capture the current layer contents before clearing.
            CaptureSnapshot();

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _layerTexture;
                GL.Clear(true, true, Color.clear);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            _onRender?.Invoke();
        }

        /// <inheritdoc />
        public void Undo()
        {
            if (_snapshot == null)
            {
                Debug.LogWarning("[DrawingSystem] ClearLayerCommand.Undo: No snapshot available to restore.");
                return;
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _layerTexture;
                Graphics.Blit(_snapshot, _layerTexture);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            _onRender?.Invoke();
        }

        private void CaptureSnapshot()
        {
            if (_snapshot == null)
            {
                _snapshot = new Texture2D(
                    _layerTexture.width,
                    _layerTexture.height,
                    TextureFormat.RGBA32,
                    false);
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = _layerTexture;
                _snapshot.ReadPixels(
                    new Rect(0, 0, _layerTexture.width, _layerTexture.height),
                    0, 0);
                _snapshot.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
    }

    /// <summary>
    /// Manages undo and redo operations using the command pattern.
    /// Commands are pushed onto an undo stack when executed; undone commands
    /// move to a redo stack. Executing a new command clears the redo stack.
    /// </summary>
    public class UndoRedoManager
    {
        private readonly Stack<IDrawCommand> _undoStack = new Stack<IDrawCommand>();
        private readonly Stack<IDrawCommand> _redoStack = new Stack<IDrawCommand>();

        /// <summary>
        /// Maximum number of commands retained in the undo history.
        /// Oldest commands are discarded when this limit is exceeded.
        /// </summary>
        [SerializeField]
        private int _maxHistorySize = 50;

        /// <summary>
        /// Gets or sets the maximum undo history size. Must be at least 1.
        /// </summary>
        public int MaxHistorySize
        {
            get => _maxHistorySize;
            set => _maxHistorySize = Mathf.Max(1, value);
        }

        /// <summary>
        /// Fired whenever the undo/redo state changes (after execute, undo, redo, or clear).
        /// </summary>
        public event Action OnStateChanged;

        /// <summary>
        /// Whether there are any commands available to undo.
        /// </summary>
        public bool CanUndo => _undoStack.Count > 0;

        /// <summary>
        /// Whether there are any commands available to redo.
        /// </summary>
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// Executes a command, pushes it onto the undo stack, and clears the redo stack.
        /// </summary>
        /// <param name="cmd">The command to execute.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="cmd"/> is <c>null</c>.</exception>
        public void ExecuteCommand(IDrawCommand cmd)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            cmd.Execute();
            _undoStack.Push(cmd);
            _redoStack.Clear();

            EnforceHistoryLimit();
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Pushes an already-executed command onto the undo stack without executing it again.
        /// Clears the redo stack.
        /// </summary>
        /// <param name="cmd">The command that was already executed.</param>
        public void PushExecutedCommand(IDrawCommand cmd)
        {
            if (cmd == null)
            {
                throw new ArgumentNullException(nameof(cmd));
            }

            _undoStack.Push(cmd);
            _redoStack.Clear();

            EnforceHistoryLimit();
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Undoes the most recent command.
        /// </summary>
        public void Undo()
        {
            if (_undoStack.Count == 0)
            {
                Debug.LogWarning("[DrawingSystem] UndoRedoManager.Undo: Nothing to undo.");
                return;
            }

            IDrawCommand cmd = _undoStack.Pop();
            cmd.Undo();
            _redoStack.Push(cmd);
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Re-executes the most recently undone command.
        /// </summary>
        public void Redo()
        {
            if (_redoStack.Count == 0)
            {
                Debug.LogWarning("[DrawingSystem] UndoRedoManager.Redo: Nothing to redo.");
                return;
            }

            IDrawCommand cmd = _redoStack.Pop();
            cmd.Execute();
            _undoStack.Push(cmd);

            EnforceHistoryLimit();
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Clears both the undo and redo stacks.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Trims the undo stack to <see cref="MaxHistorySize"/> by discarding
        /// the oldest commands when the limit is exceeded.
        /// </summary>
        private void EnforceHistoryLimit()
        {
            if (_undoStack.Count <= _maxHistorySize)
            {
                return;
            }

            // Transfer to an array to remove from the bottom of the stack.
            var commands = _undoStack.ToArray();
            _undoStack.Clear();

            // commands[0] is the most recent (top of stack).
            // Keep only the first MaxHistorySize entries.
            int keepCount = Mathf.Min(_maxHistorySize, commands.Length);
            for (int i = keepCount - 1; i >= 0; i--)
            {
                _undoStack.Push(commands[i]);
            }
        }
    }
}
