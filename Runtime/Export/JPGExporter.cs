using System;
using System.IO;
using UnityEngine;

namespace DrawingSystem.Export
{
    /// <summary>
    /// Exports a <see cref="Texture2D"/> as a JPEG image.
    /// Always flattens alpha to a white background since JPEG does not support transparency.
    /// Supports optional resizing and RenderTexture input.
    /// </summary>
    public class JPGExporter : IExporter
    {
        /// <inheritdoc />
        public string FileExtension => "jpg";

        /// <inheritdoc />
        public byte[] Export(Texture2D texture, ExportSettings settings = null)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            settings = settings ?? new ExportSettings();

            int quality = Mathf.Clamp(settings.Quality, 1, 100);

            Texture2D workingTexture = PNGExporter.EnsureReadable(texture);
            bool ownsTexture = workingTexture != texture;

            try
            {
                workingTexture = PNGExporter.ApplyResize(workingTexture, settings, ref ownsTexture);

                // JPEG has no alpha channel; always flatten to white background.
                workingTexture = PNGExporter.FlattenAlphaToWhite(workingTexture, ref ownsTexture);

                return workingTexture.EncodeToJPG(quality);
            }
            finally
            {
                if (ownsTexture && workingTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(workingTexture);
                }
            }
        }

        /// <inheritdoc />
        public void ExportToFile(Texture2D texture, string filePath, ExportSettings settings = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path must not be null or empty.", nameof(filePath));

            byte[] data = Export(texture, settings);

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(filePath, data);
        }
    }
}
