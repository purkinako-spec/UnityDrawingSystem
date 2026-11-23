using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>
    /// Scores drawings by comparing binary silhouettes using Intersection over Union (IoU)
    /// and a proximity-based metric with morphological dilation.
    /// </summary>
    [CreateAssetMenu(fileName = "ShapeScorer", menuName = "Drawing System/Scoring/Shape Scorer")]
    public class ShapeScorer : ScriptableObject, IScoringEngine
    {
        [Tooltip("Grayscale threshold for binarization. Pixels above this value become 1 (foreground).")]
        [SerializeField, Range(0f, 1f)] private float binarizeThreshold = 0.5f;

        [Tooltip("Invert the binarization so dark strokes on a light background are treated as foreground.")]
        [SerializeField] private bool invertColors = false;

        [Tooltip("Dilation radius in pixels for the proximity score calculation.")]
        [SerializeField, Min(0)] private int dilationRadius = 3;

        [Tooltip("Weight of the IoU score in the final combined score.")]
        [SerializeField, Range(0f, 1f)] private float iouWeight = 0.5f;

        [Tooltip("Weight of the proximity score in the final combined score.")]
        [SerializeField, Range(0f, 1f)] private float proximityWeight = 0.5f;

        /// <summary>Grayscale threshold for binarization.</summary>
        public float BinarizeThreshold
        {
            get => binarizeThreshold;
            set => binarizeThreshold = Mathf.Clamp01(value);
        }

        /// <summary>Whether to invert colors before binarization (for dark-on-light drawings).</summary>
        public bool InvertColors
        {
            get => invertColors;
            set => invertColors = value;
        }

        /// <summary>Dilation radius in pixels for the proximity metric.</summary>
        public int DilationRadius
        {
            get => dilationRadius;
            set => dilationRadius = Mathf.Max(0, value);
        }

        /// <inheritdoc />
        public string Name => "Shape (IoU)";

        /// <inheritdoc />
        public ScoringResult Evaluate(Texture2D reference, Texture2D drawn)
        {
            if (reference == null)
                throw new System.ArgumentNullException(nameof(reference));
            if (drawn == null)
                throw new System.ArgumentNullException(nameof(drawn));

            int width = reference.width;
            int height = reference.height;

            // Resize drawn image if dimensions don't match
            Texture2D drawnResized = drawn;
            bool needsCleanup = false;
            if (drawn.width != width || drawn.height != height)
            {
                drawnResized = ResizeTexture(drawn, width, height);
                needsCleanup = true;
            }

            Color32[] refPixels = reference.GetPixels32();
            Color32[] drawnPixels = drawnResized.GetPixels32();

            int totalPixels = width * height;

            // Step 1 & 2: Convert to grayscale and binarize
            bool[] refBinary = Binarize(refPixels, totalPixels);
            bool[] drawnBinary = Binarize(drawnPixels, totalPixels);

            // Step 3 & 4: Compute IoU
            int intersection = 0;
            int union = 0;

            for (int i = 0; i < totalPixels; i++)
            {
                bool r = refBinary[i];
                bool d = drawnBinary[i];

                if (r && d) intersection++;
                if (r || d) union++;
            }

            float iou = union > 0 ? (float)intersection / union : 1f;

            // Step 5: Proximity score via dilation
            // Dilate the reference mask and check how much of the drawn mask falls within it
            bool[] dilatedRef = Dilate(refBinary, width, height, dilationRadius);

            int drawnForegroundCount = 0;
            int drawnWithinDilated = 0;

            for (int i = 0; i < totalPixels; i++)
            {
                if (drawnBinary[i])
                {
                    drawnForegroundCount++;
                    if (dilatedRef[i])
                    {
                        drawnWithinDilated++;
                    }
                }
            }

            float proximityScore = drawnForegroundCount > 0
                ? (float)drawnWithinDilated / drawnForegroundCount
                : (union == 0 ? 1f : 0f); // If no drawn pixels and no ref pixels, perfect; otherwise zero

            // Weighted combination
            float totalWeight = iouWeight + proximityWeight;
            float normIouWeight = totalWeight > 0f ? iouWeight / totalWeight : 0.5f;
            float normProxWeight = totalWeight > 0f ? proximityWeight / totalWeight : 0.5f;
            float finalScore = Mathf.Clamp01(iou * normIouWeight + proximityScore * normProxWeight);

            // Generate heatmap: green = both present, red = mismatch, dark = both absent
            Texture2D heatmap = GenerateHeatmap(refBinary, drawnBinary, dilatedRef, width, height);

            if (needsCleanup)
            {
                DestroyTexture(drawnResized);
            }

            return new ScoringResult
            {
                Score = finalScore,
                ScorerName = Name,
                Heatmap = heatmap,
                Details = new Dictionary<string, float>
                {
                    { "IoU", iou },
                    { "ProximityScore", proximityScore },
                    { "Intersection", intersection },
                    { "Union", union }
                }
            };
        }

        /// <summary>
        /// Converts pixel data to grayscale and binarizes based on threshold and inversion settings.
        /// </summary>
        private bool[] Binarize(Color32[] pixels, int count)
        {
            bool[] binary = new bool[count];
            const float inv = 1f / 255f;

            for (int i = 0; i < count; i++)
            {
                Color32 c = pixels[i];
                float gray = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * inv;

                bool foreground = gray > binarizeThreshold;
                if (invertColors) foreground = !foreground;

                binary[i] = foreground;
            }

            return binary;
        }

        /// <summary>
        /// Applies a morphological dilation to a binary mask using a square structuring element.
        /// </summary>
        /// <param name="mask">Input binary mask.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="radius">Dilation radius in pixels.</param>
        /// <returns>Dilated binary mask.</returns>
        private static bool[] Dilate(bool[] mask, int width, int height, int radius)
        {
            if (radius <= 0) return (bool[])mask.Clone();

            bool[] dilated = new bool[mask.Length];

            // Use a separable approach for better performance:
            // First pass: horizontal dilation
            bool[] temp = new bool[mask.Length];
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (temp[rowOffset + x]) continue;

                    int xMin = Mathf.Max(0, x - radius);
                    int xMax = Mathf.Min(width - 1, x + radius);

                    for (int kx = xMin; kx <= xMax; kx++)
                    {
                        if (mask[rowOffset + kx])
                        {
                            temp[rowOffset + x] = true;
                            break;
                        }
                    }
                }
            }

            // Second pass: vertical dilation on the horizontally dilated result
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (dilated[rowOffset + x]) continue;

                    int yMin = Mathf.Max(0, y - radius);
                    int yMax = Mathf.Min(height - 1, y + radius);

                    for (int ky = yMin; ky <= yMax; ky++)
                    {
                        if (temp[ky * width + x])
                        {
                            dilated[rowOffset + x] = true;
                            break;
                        }
                    }
                }
            }

            return dilated;
        }

        /// <summary>
        /// Generates a heatmap showing shape comparison results.
        /// Green = intersection (both present), Red = only in one image,
        /// Yellow = drawn pixel within dilated reference, Dark = both absent.
        /// </summary>
        private static Texture2D GenerateHeatmap(bool[] refBinary, bool[] drawnBinary, bool[] dilatedRef, int width, int height)
        {
            int totalPixels = width * height;
            Color32[] heatmapPixels = new Color32[totalPixels];

            for (int i = 0; i < totalPixels; i++)
            {
                bool r = refBinary[i];
                bool d = drawnBinary[i];

                if (r && d)
                {
                    // Intersection: bright green
                    heatmapPixels[i] = new Color32(0, 220, 0, 255);
                }
                else if (d && dilatedRef[i])
                {
                    // Drawn pixel within proximity of reference: yellow (close but not exact)
                    heatmapPixels[i] = new Color32(200, 200, 0, 255);
                }
                else if (d && !r)
                {
                    // Only in drawn (extra strokes): red
                    heatmapPixels[i] = new Color32(220, 0, 0, 255);
                }
                else if (r && !d)
                {
                    // Only in reference (missing strokes): dark red
                    heatmapPixels[i] = new Color32(140, 0, 0, 255);
                }
                else
                {
                    // Both absent: dark background
                    heatmapPixels[i] = new Color32(20, 20, 20, 255);
                }
            }

            Texture2D heatmap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            heatmap.SetPixels32(heatmapPixels);
            heatmap.Apply();
            return heatmap;
        }

        /// <summary>
        /// Resizes a texture to the specified dimensions using bilinear filtering via RenderTexture.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }

        /// <summary>
        /// Safely destroys a Unity Object in both editor and runtime contexts.
        /// </summary>
        private static void DestroyTexture(Object obj)
        {
#if UNITY_EDITOR
            DestroyImmediate(obj);
#else
            Destroy(obj);
#endif
        }
    }
}
