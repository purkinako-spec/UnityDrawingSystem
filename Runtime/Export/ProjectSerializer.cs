using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DrawingSystem.Export
{
    /// <summary>
    /// Represents a full drawing project, including canvas dimensions,
    /// layer metadata, and all stroke data. Serializable to JSON via <see cref="JsonUtility"/>.
    /// </summary>
    [System.Serializable]
    public class DrawingProject
    {
        /// <summary>
        /// Format version string for forward compatibility.
        /// </summary>
        public string Version = "1.0";

        /// <summary>
        /// Width of the canvas in pixels.
        /// </summary>
        public int CanvasWidth;

        /// <summary>
        /// Height of the canvas in pixels.
        /// </summary>
        public int CanvasHeight;

        /// <summary>
        /// Ordered list of layer metadata.
        /// </summary>
        public List<LayerInfo> Layers = new List<LayerInfo>();

        /// <summary>
        /// All strokes in the project. Each stroke references its layer by <see cref="StrokeData.LayerIndex"/>.
        /// </summary>
        public List<StrokeData> Strokes = new List<StrokeData>();
    }

    /// <summary>
    /// Metadata describing a single layer in the drawing project.
    /// </summary>
    [System.Serializable]
    public class LayerInfo
    {
        /// <summary>
        /// Display name of the layer.
        /// </summary>
        public string Name;

        /// <summary>
        /// Whether the layer is visible.
        /// </summary>
        public bool Visible;

        /// <summary>
        /// Opacity of the layer, from 0 (fully transparent) to 1 (fully opaque).
        /// </summary>
        public float Opacity;

        /// <summary>
        /// Blend mode index: 0 = Normal, 1 = Multiply, 2 = Screen, 3 = Overlay.
        /// </summary>
        public int BlendMode;
    }

    /// <summary>
    /// Provides static methods to serialize and deserialize <see cref="DrawingProject"/> instances
    /// to and from JSON using Unity's built-in <see cref="JsonUtility"/>.
    /// </summary>
    public static class ProjectSerializer
    {
        /// <summary>
        /// Serializes a <see cref="DrawingProject"/> to a JSON string.
        /// </summary>
        /// <param name="project">The project to serialize. Must not be null.</param>
        /// <returns>A JSON string representing the project.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="project"/> is null.</exception>
        public static string Serialize(DrawingProject project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));

            return JsonUtility.ToJson(project, true);
        }

        /// <summary>
        /// Deserializes a <see cref="DrawingProject"/> from a JSON string.
        /// </summary>
        /// <param name="json">The JSON string to deserialize. Must not be null or empty.</param>
        /// <returns>A <see cref="DrawingProject"/> instance populated from the JSON data.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="json"/> is null or empty.</exception>
        public static DrawingProject Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON string must not be null or empty.", nameof(json));

            DrawingProject project = JsonUtility.FromJson<DrawingProject>(json);

            if (project == null)
                throw new InvalidOperationException("Failed to deserialize DrawingProject from the provided JSON.");

            // Ensure collections are never null after deserialization.
            if (project.Layers == null)
                project.Layers = new List<LayerInfo>();
            if (project.Strokes == null)
                project.Strokes = new List<StrokeData>();

            return project;
        }

        /// <summary>
        /// Serializes a <see cref="DrawingProject"/> to JSON and writes it to a file.
        /// Creates parent directories if they do not exist.
        /// </summary>
        /// <param name="project">The project to save. Must not be null.</param>
        /// <param name="filePath">The absolute file path to write to. Must not be null or empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="project"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null or empty.</exception>
        public static void SaveToFile(DrawingProject project, string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

            string json = Serialize(project);

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Loads a <see cref="DrawingProject"/> from a JSON file on disk.
        /// </summary>
        /// <param name="filePath">The absolute path to the JSON file. Must not be null or empty.</param>
        /// <returns>The deserialized <see cref="DrawingProject"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is null or empty.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
        public static DrawingProject LoadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Drawing project file not found.", filePath);

            string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            return Deserialize(json);
        }
    }
}
