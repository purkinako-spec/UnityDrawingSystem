using UnityEngine;

namespace DrawingSystem.Export
{
    /// <summary>
    /// Interface for raster-based image exporters.
    /// </summary>
    public interface IExporter
    {
        /// <summary>
        /// Gets the file extension associated with this exporter (e.g., "png", "jpg").
        /// </summary>
        string FileExtension { get; }

        /// <summary>
        /// Exports a <see cref="Texture2D"/> to a byte array in the target format.
        /// </summary>
        /// <param name="texture">The source texture to export. Must not be null.</param>
        /// <param name="settings">Optional export settings. If null, defaults are used.</param>
        /// <returns>A byte array containing the encoded image data.</returns>
        byte[] Export(Texture2D texture, ExportSettings settings = null);

        /// <summary>
        /// Exports a <see cref="Texture2D"/> and writes the result to a file on disk.
        /// </summary>
        /// <param name="texture">The source texture to export. Must not be null.</param>
        /// <param name="filePath">The absolute file path to write to.</param>
        /// <param name="settings">Optional export settings. If null, defaults are used.</param>
        void ExportToFile(Texture2D texture, string filePath, ExportSettings settings = null);
    }

    /// <summary>
    /// Settings that control the behavior of image exporters.
    /// </summary>
    [System.Serializable]
    public class ExportSettings
    {
        /// <summary>
        /// JPEG quality level (1–100). Higher values produce better quality at larger file sizes.
        /// Only applicable to JPEG export.
        /// </summary>
        public int Quality = 90;

        /// <summary>
        /// Whether to include the alpha channel in the exported image.
        /// Only applicable to formats that support transparency (e.g., PNG).
        /// </summary>
        public bool IncludeAlpha = true;

        /// <summary>
        /// Maximum width constraint for the exported image. The image is resized proportionally
        /// if it exceeds this value. A value of 0 means no width constraint (original size).
        /// </summary>
        public int MaxWidth = 0;

        /// <summary>
        /// Maximum height constraint for the exported image. The image is resized proportionally
        /// if it exceeds this value. A value of 0 means no height constraint (original size).
        /// </summary>
        public int MaxHeight = 0;
    }
}
