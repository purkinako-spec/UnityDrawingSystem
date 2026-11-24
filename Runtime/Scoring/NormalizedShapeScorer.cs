using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>
    /// Position- and scale-invariant scorer with intelligent noise filtering.
    /// It crops the drawing to its bounding box (ignoring stray dots and tiny noise clusters),
    /// normalizes the scale/aspect-ratio, and evaluates similarity using Shape (IoU) and MSE.
    /// </summary>
    [CreateAssetMenu(fileName = "NormalizedShapeScorer", menuName = "Drawing System/Scoring/Normalized Shape Scorer (Position & Scale Invariant)")]
    public class NormalizedShapeScorer : ScriptableObject, IScoringEngine
    {
        [Header("Binarization & Thresholds")]
        [Tooltip("Brightness threshold (0-1) to distinguish drawn stroke pixels from background.")]
        [SerializeField, Range(0f, 1f)] private float binarizeThreshold = 0.5f;

        [Tooltip("If true, treats dark pixels on a light background as strokes.")]
        [SerializeField] private bool invertColors = false;

        [Header("Noise Filtering (Anti-Stray Dot)")]
        [Tooltip("Connected components (clusters of stroke pixels) smaller than this count will be treated as stray dots and ignored.")]
        [SerializeField, Min(1)] private int minComponentPixelCount = 15;

        [Tooltip("Percentile margin (0-0.1) to cut off isolated outlier pixels when determining the bounding box.")]
        [SerializeField, Range(0f, 0.1f)] private float outlierPercentile = 0.02f;

        [Header("Normalization")]
        [Tooltip("Resolution of the normalized square texture used for comparison.")]
        [SerializeField, Min(32)] private int normalizedResolution = 256;

        [Tooltip("Padding ratio (0-0.2) around the cropped shape inside the normalized texture.")]
        [SerializeField, Range(0f, 0.2f)] private float paddingRatio = 0.05f;

        [Header("Scoring Weights")]
        [Tooltip("Weight of the shape silhouette IoU score.")]
        [SerializeField, Range(0f, 1f)] private float iouWeight = 0.7f;

        [Tooltip("Weight of the pixel-level density/proximity match score.")]
        [SerializeField, Range(0f, 1f)] private float densityWeight = 0.3f;

        public string Name => "Normalized Shape (Invariant)";

        public ScoringResult Evaluate(Texture2D reference, Texture2D drawn)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            if (drawn == null)
                throw new ArgumentNullException(nameof(drawn));

            var details = new Dictionary<string, float>();

            // 1. Extract cleaned binary masks (with noise removal)
            bool[] refMask = ExtractCleanMask(reference, out RectInt refBox);
            bool[] drawnMask = ExtractCleanMask(drawn, out RectInt drawnBox);

            // If neither has drawing, perfect match. If only one has drawing, zero match.
            bool refEmpty = refBox.width <= 0 || refBox.height <= 0;
            bool drawnEmpty = drawnBox.width <= 0 || drawnBox.height <= 0;

            if (refEmpty && drawnEmpty)
            {
                details["IoU"] = 1f;
                details["Density"] = 1f;
                return new ScoringResult { Score = 1f, ScorerName = Name, Details = details };
            }

            if (refEmpty || drawnEmpty)
            {
                details["IoU"] = 0f;
                details["Density"] = 0f;
                return new ScoringResult { Score = 0f, ScorerName = Name, Details = details };
            }

            // 2. Crop and normalize both shapes to normalizedResolution x normalizedResolution
            float[] refNorm = NormalizeShape(refMask, reference.width, reference.height, refBox, normalizedResolution, paddingRatio);
            float[] drawnNorm = NormalizeShape(drawnMask, drawn.width, drawn.height, drawnBox, normalizedResolution, paddingRatio);

            // 3. Compute IoU and Density Score
            int totalNormPixels = normalizedResolution * normalizedResolution;
            float intersection = 0f;
            float union = 0f;
            float sumDiff = 0f;

            for (int i = 0; i < totalNormPixels; i++)
            {
                float r = refNorm[i];
                float d = drawnNorm[i];

                intersection += Mathf.Min(r, d);
                union += Mathf.Max(r, d);
                sumDiff += Mathf.Abs(r - d);
            }

            float iouScore = union > 0f ? (intersection / union) : 1f;
            float densityScore = Mathf.Clamp01(1f - (sumDiff / totalNormPixels));

            float totalWeight = iouWeight + densityWeight;
            float finalScore = totalWeight > 0f
                ? Mathf.Clamp01((iouScore * iouWeight + densityScore * densityWeight) / totalWeight)
                : iouScore;

            details["IoU"] = iouScore;
            details["Density"] = densityScore;
            details["NormalizedResolution"] = normalizedResolution;

            // Generate Heatmap (Normalized resolution)
            Texture2D heatmap = GenerateHeatmap(refNorm, drawnNorm, normalizedResolution);

            return new ScoringResult
            {
                Score = finalScore,
                ScorerName = Name,
                Heatmap = heatmap,
                Details = details
            };
        }

        /// <summary>
        /// Binarizes texture, filters out small stray dot clusters, and computes the robust bounding box.
        /// </summary>
        private bool[] ExtractCleanMask(Texture2D tex, out RectInt bbox)
        {
            int w = tex.width;
            int h = tex.height;
            int total = w * h;
            Color32[] pixels = tex.GetPixels32();

            bool[] rawMask = new bool[total];
            for (int i = 0; i < total; i++)
            {
                Color32 c = pixels[i];
                float alpha = c.a / 255f;
                float gray = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

                bool isStroke;
                if (alpha < 0.05f)
                {
                    isStroke = false;
                }
                else if (invertColors)
                {
                    isStroke = (1f - gray) >= binarizeThreshold;
                }
                else
                {
                    isStroke = gray >= binarizeThreshold;
                }
                rawMask[i] = isStroke;
            }

            // Remove tiny connected components (stray dots / noise)
            bool[] cleanMask = FilterConnectedComponents(rawMask, w, h, minComponentPixelCount);

            // Compute robust bounding box (with outlier percentile trimming)
            bbox = ComputeRobustBoundingBox(cleanMask, w, h, outlierPercentile);
            return cleanMask;
        }

        /// <summary>
        /// Removes connected components that have fewer pixels than minPixelCount.
        /// </summary>
        private static bool[] FilterConnectedComponents(bool[] mask, int w, int h, int minPixelCount)
        {
            int total = w * h;
            bool[] visited = new bool[total];
            bool[] filtered = new bool[total];

            var queue = new Queue<int>();
            var currentComponent = new List<int>();

            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };

            for (int i = 0; i < total; i++)
            {
                if (!mask[i] || visited[i]) continue;

                currentComponent.Clear();
                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int curr = queue.Dequeue();
                    currentComponent.Add(curr);

                    int cx = curr % w;
                    int cy = curr / w;

                    for (int dir = 0; dir < 4; dir++)
                    {
                        int nx = cx + dx[dir];
                        int ny = cy + dy[dir];

                        if (nx >= 0 && nx < w && ny >= 0 && ny < h)
                        {
                            int nIdx = ny * w + nx;
                            if (mask[nIdx] && !visited[nIdx])
                            {
                                visited[nIdx] = true;
                                queue.Enqueue(nIdx);
                            }
                        }
                    }
                }

                if (currentComponent.Count >= minPixelCount)
                {
                    foreach (int idx in currentComponent)
                    {
                        filtered[idx] = true;
                    }
                }
            }

            return filtered;
        }

        /// <summary>
        /// Computes bounding box with percentile outlier trimming along X and Y axes.
        /// </summary>
        private static RectInt ComputeRobustBoundingBox(bool[] mask, int w, int h, float outlierRatio)
        {
            var xCoords = new List<int>();
            var yCoords = new List<int>();

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (mask[rowOffset + x])
                    {
                        xCoords.Add(x);
                        yCoords.Add(y);
                    }
                }
            }

            if (xCoords.Count == 0)
            {
                return new RectInt(0, 0, 0, 0);
            }

            xCoords.Sort();
            yCoords.Sort();

            int count = xCoords.Count;
            int trimCount = Mathf.FloorToInt(count * outlierRatio);

            int minX = xCoords[Mathf.Min(trimCount, count - 1)];
            int maxX = xCoords[Mathf.Max(0, count - 1 - trimCount)];
            int minY = yCoords[Mathf.Min(trimCount, count - 1)];
            int maxY = yCoords[Mathf.Max(0, count - 1 - trimCount)];

            return new RectInt(minX, minY, Mathf.Max(1, maxX - minX + 1), Mathf.Max(1, maxY - minY + 1));
        }

        /// <summary>
        /// Resamples and centers the bounding box region into a square normalized buffer, preserving aspect ratio.
        /// </summary>
        private static float[] NormalizeShape(bool[] mask, int origW, int origH, RectInt box, int normRes, float padRatio)
        {
            float[] output = new float[normRes * normRes];

            float padPixels = normRes * padRatio;
            float availW = normRes - (2f * padPixels);
            float availH = normRes - (2f * padPixels);

            float scale = Mathf.Min(availW / box.width, availH / box.height);
            float placedW = box.width * scale;
            float placedH = box.height * scale;

            float offsetX = (normRes - placedW) * 0.5f;
            float offsetY = (normRes - placedH) * 0.5f;

            for (int ny = 0; ny < normRes; ny++)
            {
                float origY = box.y + ((ny - offsetY) / scale);
                int srcY = Mathf.FloorToInt(origY);

                if (srcY < box.y || srcY >= box.y + box.height || srcY < 0 || srcY >= origH)
                    continue;

                int rowOffset = ny * normRes;
                int srcRowOffset = srcY * origW;

                for (int nx = 0; nx < normRes; nx++)
                {
                    float origX = box.x + ((nx - offsetX) / scale);
                    int srcX = Mathf.FloorToInt(origX);

                    if (srcX >= box.x && srcX < box.x + box.width && srcX >= 0 && srcX < origW)
                    {
                        output[rowOffset + nx] = mask[srcRowOffset + srcX] ? 1f : 0f;
                    }
                }
            }

            return output;
        }

        private static Texture2D GenerateHeatmap(float[] refNorm, float[] drawnNorm, int res)
        {
            Texture2D heatmap = new Texture2D(res, res, TextureFormat.RGBA32, false);
            Color32[] mapPixels = new Color32[res * res];

            for (int i = 0; i < mapPixels.Length; i++)
            {
                float r = refNorm[i];
                float d = drawnNorm[i];

                if (r > 0.5f && d > 0.5f)
                {
                    // Green: Match
                    mapPixels[i] = new Color32(0, 255, 0, 255);
                }
                else if (d > 0.5f && r <= 0.5f)
                {
                    // Red: Drawn outside of reference
                    mapPixels[i] = new Color32(255, 30, 30, 255);
                }
                else if (r > 0.5f && d <= 0.5f)
                {
                    // Blue/Cyan: Missing stroke
                    mapPixels[i] = new Color32(50, 150, 255, 255);
                }
                else
                {
                    // Transparent dark background
                    mapPixels[i] = new Color32(20, 20, 20, 180);
                }
            }

            heatmap.SetPixels32(mapPixels);
            heatmap.Apply();
            return heatmap;
        }
    }
}
