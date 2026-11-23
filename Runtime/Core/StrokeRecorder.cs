using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace DrawingSystem
{
    /// <summary>
    /// Records and manages stroke data during a drawing session.
    /// Provides methods to begin, build, and finalize strokes as well as
    /// query, remove, and clear the recorded history.
    /// </summary>
    public class StrokeRecorder
    {
        private readonly List<StrokeData> _strokes = new List<StrokeData>();
        private StrokeData _currentStroke;

        /// <summary>
        /// Whether a stroke is currently being recorded.
        /// </summary>
        public bool IsRecording => _currentStroke != null;

        /// <summary>
        /// Begins recording a new stroke with the specified brush settings.
        /// </summary>
        /// <param name="brushId">Identifier of the brush profile to use.</param>
        /// <param name="color">Color of the stroke.</param>
        /// <param name="brushSize">Brush diameter.</param>
        /// <param name="layerIndex">Index of the layer to draw on.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if a stroke is already being recorded. Call <see cref="EndStroke"/> first.
        /// </exception>
        public void BeginStroke(string brushId, Color color, float brushSize, int layerIndex, bool isEraser = false)
        {
            if (_currentStroke != null)
            {
                throw new InvalidOperationException(
                    "A stroke is already being recorded. Call EndStroke() before starting a new one.");
            }

            _currentStroke = new StrokeData
            {
                BrushProfileId = brushId ?? string.Empty,
                Color = color,
                BrushSize = brushSize,
                LayerIndex = layerIndex,
                IsEraser = isEraser
            };
        }

        /// <summary>
        /// Adds a point to the stroke currently being recorded.
        /// </summary>
        /// <param name="point">The <see cref="StrokePoint"/> to append.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if no stroke is currently being recorded.
        /// </exception>
        public void AddPoint(StrokePoint point)
        {
            if (_currentStroke == null)
            {
                throw new InvalidOperationException(
                    "No stroke is being recorded. Call BeginStroke() first.");
            }

            _currentStroke.Points.Add(point);
        }

        /// <summary>
        /// Ends the current stroke, stores it, and returns the completed data.
        /// </summary>
        /// <returns>The completed <see cref="StrokeData"/>, or <c>null</c> if no stroke was active.</returns>
        public StrokeData EndStroke()
        {
            if (_currentStroke == null)
            {
                Debug.LogWarning("[DrawingSystem] StrokeRecorder.EndStroke called with no active stroke.");
                return null;
            }

            StrokeData completed = _currentStroke;
            _strokes.Add(completed);
            _currentStroke = null;
            return completed;
        }

        /// <summary>
        /// Returns a read-only view of all recorded strokes.
        /// </summary>
        /// <returns>A <see cref="ReadOnlyCollection{StrokeData}"/> containing every recorded stroke.</returns>
        public ReadOnlyCollection<StrokeData> GetAllStrokes()
        {
            return _strokes.AsReadOnly();
        }

        /// <summary>
        /// Returns all recorded strokes that belong to the specified layer.
        /// </summary>
        /// <param name="layerIndex">The layer index to filter by.</param>
        /// <returns>A list of <see cref="StrokeData"/> on the given layer.</returns>
        public List<StrokeData> GetStrokesForLayer(int layerIndex)
        {
            var result = new List<StrokeData>();
            for (int i = 0; i < _strokes.Count; i++)
            {
                if (_strokes[i].LayerIndex == layerIndex)
                {
                    result.Add(_strokes[i]);
                }
            }
            return result;
        }

        /// <summary>
        /// Removes the most recently recorded stroke (used for undo operations).
        /// </summary>
        /// <returns>The removed <see cref="StrokeData"/>, or <c>null</c> if no strokes exist.</returns>
        public StrokeData RemoveLastStroke()
        {
            if (_strokes.Count == 0)
            {
                Debug.LogWarning("[DrawingSystem] StrokeRecorder.RemoveLastStroke called with no strokes.");
                return null;
            }

            int lastIndex = _strokes.Count - 1;
            StrokeData removed = _strokes[lastIndex];
            _strokes.RemoveAt(lastIndex);
            return removed;
        }

        /// <summary>
        /// Adds a previously recorded stroke back into the history (used for redo operations).
        /// </summary>
        /// <param name="stroke">The <see cref="StrokeData"/> to re-add.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="stroke"/> is <c>null</c>.</exception>
        public void AddStrokeData(StrokeData stroke)
        {
            if (stroke == null)
            {
                throw new ArgumentNullException(nameof(stroke));
            }

            _strokes.Add(stroke);
        }

        /// <summary>
        /// Clears all recorded strokes and cancels any in-progress stroke.
        /// </summary>
        public void Clear()
        {
            _currentStroke = null;
            _strokes.Clear();
        }
    }
}
