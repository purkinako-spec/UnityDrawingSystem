using System;
using System.IO;
using UnityEngine;

namespace DrawingSystem.Export
{
    /// <summary>
    /// Exports a <see cref="Texture2D"/> as a PNG image.
    /// Supports optional resizing, alpha removal, and RenderTexture input.
    /// </summary>
    public class PNGExporter : IExporter
    {
        /// <inheritdoc />
        public string FileExtension => "png";

        /// <inheritdoc />
        public byte[] Export(Texture2D texture, ExportSettings settings = null)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            settings = settings ?? new ExportSettings();

            Texture2D workingTexture = EnsureReadable(texture);
            bool ownsTexture = workingTexture != texture;

            try
            {
                workingTexture = ApplyResize(workingTexture, settings, ref ownsTexture);

                if (!settings.IncludeAlpha)
                {
                    workingTexture = FlattenAlphaToWhite(workingTexture, ref ownsTexture);
                }

                return workingTexture.EncodeToPNG();
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

        /// <summary>
        /// Creates a readable <see cref="Texture2D"/> copy from a <see cref="RenderTexture"/>.
        /// The caller is responsible for destroying the returned texture when finished.
        /// </summary>
        /// <param name="rt">The source RenderTexture. Must not be null.</param>
        /// <returns>A new readable Texture2D containing the contents of the RenderTexture.</returns>
        public static Texture2D ReadableFromRenderTexture(RenderTexture rt)
        {
            if (rt == null)
                throw new ArgumentNullException(nameof(rt));

            RenderTexture previousActive = RenderTexture.active;

            try
            {
                RenderTexture.active = rt;

                Texture2D readable = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                readable.Apply();

                return readable;
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        /// <summary>
        /// Ensures the given texture is readable. If the texture is not readable,
        /// creates a readable copy via a temporary RenderTexture.
        /// </summary>
        /// <param name="texture">The source texture.</param>
        /// <returns>A readable texture. May be the original or a new copy.</returns>
        internal static Texture2D EnsureReadable(Texture2D texture)
        {
            try
            {
                // Attempt to access pixel data to verify readability.
                texture.GetPixel(0, 0);
                return texture;
            }
            catch (UnityException)
            {
                // Texture is not readable; copy via RenderTexture.
                RenderTexture tmp = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
                try
                {
                    Graphics.Blit(texture, tmp);
                    return ReadableFromRenderTexture(tmp);
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(tmp);
                }
            }
        }

        /// <summary>
        /// Resizes the working texture if MaxWidth or MaxHeight constraints are set and exceeded.
        /// Uses bilinear filtering for smooth downscaling. Maintains aspect ratio.
        /// </summary>
        /// <param name="source">The source texture to potentially resize.</param>
        /// <param name="settings">Export settings containing size constraints.</param>
        /// <param name="ownsTexture">
        /// Tracks whether the caller owns the returned texture. Updated if a new texture is created.
        /// </param>
        /// <returns>The original or a resized texture.</returns>
        internal static Texture2D ApplyResize(Texture2D source, ExportSettings settings, ref bool ownsTexture)
        {
            int targetWidth = source.width;
            int targetHeight = source.height;

            if (settings.MaxWidth > 0 && targetWidth > settings.MaxWidth)
            {
                float scale = (float)settings.MaxWidth / targetWidth;
                targetWidth = settings.MaxWidth;
                targetHeight = Mathf.RoundToInt(targetHeight * scale);
            }

            if (settings.MaxHeight > 0 && targetHeight > settings.MaxHeight)
            {
                float scale = (float)settings.MaxHeight / targetHeight;
                targetHeight = settings.MaxHeight;
                targetWidth = Mathf.RoundToInt(targetWidth * scale);
            }

            targetWidth = Mathf.Max(1, targetWidth);
            targetHeight = Mathf.Max(1, targetHeight);

            if (targetWidth == source.width && targetHeight == source.height)
                return source;

            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            try
            {
                Graphics.Blit(source, rt);

                Texture2D resized = ReadableFromRenderTexture(rt);

                if (ownsTexture)
                {
                    UnityEngine.Object.DestroyImmediate(source);
                }

                ownsTexture = true;
                return resized;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Flattens the alpha channel by compositing each pixel over a white background.
        /// </summary>
        /// <param name="source">The source texture with alpha.</param>
        /// <param name="ownsTexture">
        /// Tracks ownership. Updated if a new texture is created.
        /// </param>
        /// <returns>A texture with alpha fully opaque, blended over white.</returns>
        internal static Texture2D FlattenAlphaToWhite(Texture2D source, ref bool ownsTexture)
        {
            Color[] pixels = source.GetPixels();

            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float a = c.a;
                pixels[i] = new Color(
                    c.r * a + 1f * (1f - a),
                    c.g * a + 1f * (1f - a),
                    c.b * a + 1f * (1f - a),
                    1f
                );
            }

            Texture2D flattened = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            flattened.SetPixels(pixels);
            flattened.Apply();

            if (ownsTexture)
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            ownsTexture = true;
            return flattened;
        }
    }
}
