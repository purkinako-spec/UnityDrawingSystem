using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem
{
    /// <summary>
    /// Represents the phase of an input event within a drawing gesture.
    /// </summary>
    public enum InputPhase
    {
        /// <summary>The input has just begun (finger down, mouse click, pen contact).</summary>
        Began,
        /// <summary>The input is actively moving.</summary>
        Moved,
        /// <summary>The input is active but has not moved since the last frame.</summary>
        Stationary,
        /// <summary>The input has ended normally (finger lifted, mouse released, pen lifted).</summary>
        Ended,
        /// <summary>The input was cancelled by the system.</summary>
        Cancelled
    }

    /// <summary>
    /// Raw input data captured from an input device each frame.
    /// Used to feed the drawing pipeline before conversion to stroke points.
    /// </summary>
    [Serializable]
    public struct DrawingInputData
    {
        /// <summary>Screen-space or UV coordinates of the input.</summary>
        public Vector2 Position;

        /// <summary>Pressure value in the range [0, 1]. Devices without pressure report 1.</summary>
        public float Pressure;

        /// <summary>Pen tilt vector. Zero for devices that do not support tilt.</summary>
        public Vector2 Tilt;

        /// <summary>Whether the input originates from the eraser side of a stylus.</summary>
        public bool IsEraser;

        /// <summary>Current phase of this input event.</summary>
        public InputPhase Phase;

        /// <summary>Name of the source device (e.g. "Pen (XPPenTablet)", "Mouse").</summary>
        public string DeviceName;
    }

    /// <summary>
    /// A single recorded point within a stroke, stored in UV coordinates.
    /// </summary>
    [Serializable]
    public struct StrokePoint
    {
        /// <summary>UV coordinates in the range [0, 1] on the drawing surface.</summary>
        public Vector2 Position;

        /// <summary>Pressure value in the range [0, 1].</summary>
        public float Pressure;

        /// <summary>Pen tilt vector at the time of capture.</summary>
        public Vector2 Tilt;

        /// <summary>Time in seconds since the stroke began.</summary>
        public float Timestamp;
    }

    /// <summary>
    /// Complete data for a single stroke, including brush settings and all recorded points.
    /// </summary>
    [Serializable]
    public class StrokeData
    {
        /// <summary>Identifier of the brush profile used for this stroke.</summary>
        public string BrushProfileId;

        /// <summary>Color of the stroke.</summary>
        public Color Color;

        /// <summary>Brush size (diameter) used for this stroke.</summary>
        public float BrushSize;

        /// <summary>Ordered list of points that make up this stroke.</summary>
        public List<StrokePoint> Points = new List<StrokePoint>();

        /// <summary>Index of the layer this stroke belongs to.</summary>
        public int LayerIndex;

        /// <summary>Whether this stroke was drawn in eraser mode.</summary>
        public bool IsEraser;
    }
}
