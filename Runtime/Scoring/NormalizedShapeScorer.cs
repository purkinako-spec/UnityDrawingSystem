using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>
    /// Position- and scale-invariant scorer with intelligent noise filtering and difficulty control.
    /// It crops the drawing to its bounding box (ignoring stray dots and tiny noise clusters),
    /// normalizes the scale/aspect-ratio, and strictly evaluates stroke overlap (IoU and proximity)
    /// without giving free points for empty background.
    /// </summary>
    [CreateAssetMenu(fileName = "NormalizedShapeScorer", menuName = "Drawing System/Scoring/Normalized Shape Scorer (Position & Scale Invariant)")]
    public class NormalizedShapeScorer : ScriptableObject, IScoringEngine
    {
        [Header("Difficulty Adjustment (難易度調整)")]
        [Tooltip("0 = Very Easy (generous line tolerance), 0.5 = Normal, 1.0 = Very Strict (pixel-perfect required).")]
        [SerializeField, Range(0f, 1f)] private float difficulty = 0.5f;

        [Header("Binarization & Background")]
        [Tooltip("Auto-detects whether the background is white/light or transparent/dark based on border sampling.")]
        [SerializeField] private bool autoDetectBackground = true;

        [Tooltip("Brightness threshold (0-1) to distinguish drawn stroke pixels from background when manual.")]
        [SerializeField, Range(0f, 1f)] private float binarizeThreshold = 0.5f;

        [Tooltip("Manual override: treats dark pixels on a light background as strokes (ignored if autoDetectBackground is true).")]
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

        /// <summary>Difficulty factor between 0 (very lenient) and 1 (strictly accurate).</summary>
        public float Difficulty
        {
            get => difficulty;
            set => difficulty = Mathf.Clamp01(value);
        }

        public virtual string Name => "Normalized Shape (Invariant)";

        public ScoringResult Evaluate(Texture2D reference, Texture2D drawn)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            if (drawn == null)
                throw new ArgumentNullException(nameof(drawn));

            var details = new Dictionary<string, float>();

            // 1. Extract cleaned binary masks (with noise removal and auto bg detection)
            bool[] refMask = ExtractCleanMask(reference, out RectInt refBox);
            bool[] drawnMask = ExtractCleanMask(drawn, out RectInt drawnBox);

            bool refEmpty = refBox.width <= 0 || refBox.height <= 0;
            bool drawnEmpty = drawnBox.width <= 0 || drawnBox.height <= 0;

            if (refEmpty && drawnEmpty)
            {
                details["IoU"] = 1f;
                details["Precision"] = 1f;
                details["Recall"] = 1f;
                return new ScoringResult { Score = 1f, ScorerName = Name, Details = details };
            }

            if (refEmpty || drawnEmpty)
            {
                details["IoU"] = 0f;
                details["Precision"] = 0f;
                details["Recall"] = 0f;
                return new ScoringResult { Score = 0f, ScorerName = Name, Details = details };
            }

            // 2. Crop and normalize both shapes to normalizedResolution x normalizedResolution
            bool[] refNorm = PostProcessNormalizedMask(
                NormalizeShape(refMask, reference.width, reference.height, refBox, normalizedResolution, paddingRatio),
                normalizedResolution);
            bool[] drawnNorm = PostProcessNormalizedMask(
                NormalizeShape(drawnMask, drawn.width, drawn.height, drawnBox, normalizedResolution, paddingRatio),
                normalizedResolution);

            // 3. Difficulty-adaptive Proximity Dilation:
            // Easy (difficulty=0) => radius ~ 12px (forgives slight hand shakes / offsets)
            // Normal (difficulty=0.5) => radius ~ 5px
            // Hard (difficulty=1.0) => radius ~ 1px (strict line alignment)
            int toleranceRadius = Mathf.RoundToInt(Mathf.Lerp(12f, 1f, difficulty));
            bool[] dilatedRef = Dilate(refNorm, normalizedResolution, normalizedResolution, toleranceRadius);
            bool[] dilatedDrawn = Dilate(drawnNorm, normalizedResolution, normalizedResolution, toleranceRadius);

            int totalNormPixels = normalizedResolution * normalizedResolution;
            int refStrokeCount = 0;
            int drawnStrokeCount = 0;
            int exactIntersection = 0;
            int exactUnion = 0;
            int drawnCoveredByRef = 0; // Precision: how much of drawn stroke is within reference tolerance
            int refCoveredByDrawn = 0; // Recall: how much of reference is covered by drawn stroke

            for (int i = 0; i < totalNormPixels; i++)
            {
                bool r = refNorm[i];
                bool d = drawnNorm[i];

                if (r) refStrokeCount++;
                if (d) drawnStrokeCount++;

                if (r && d) exactIntersection++;
                if (r || d) exactUnion++;

                if (d && dilatedRef[i]) drawnCoveredByRef++;
                if (r && dilatedDrawn[i]) refCoveredByDrawn++;
            }

            // Precision: Penalty for drawing outside / scribbling randomly
            float precision = drawnStrokeCount > 0 ? (float)drawnCoveredByRef / drawnStrokeCount : 0f;

            // Recall: Coverage of reference lines
            float recall = refStrokeCount > 0 ? (float)refCoveredByDrawn / refStrokeCount : 0f;

            // Strict IoU (exact pixel overlap)
            float exactIoU = exactUnion > 0 ? (float)exactIntersection / exactUnion : 0f;

            // Combined F1 / Jaccard-like proximity score
            float proximityScore = (precision + recall) > 0.0001f
                ? (2f * precision * recall) / (precision + recall) // F1 harmonic mean
                : 0f;

            // Blend based on difficulty:
            // Easy: mostly proximity score with generous radius
            // Hard: heavily weighted towards exact IoU + strict non-dilated precision
            float iouWeight = Mathf.Lerp(0.2f, 0.7f, difficulty);
            float baseScore = Mathf.Lerp(proximityScore, exactIoU, iouWeight);

            // Apply difficulty exponent curve:
            // e.g. pow(score, 1.0 + difficulty * 1.5) to sharpen the falloff for messy drawings
            float curveExponent = Mathf.Lerp(0.8f, 2.5f, difficulty);
            float finalScore = Mathf.Clamp01(Mathf.Pow(baseScore, curveExponent));

            details["FinalScore"] = finalScore;
            details["Difficulty"] = difficulty;
            details["ToleranceRadius"] = toleranceRadius;
            details["Precision"] = precision;
            details["Recall"] = recall;
            details["ExactIoU"] = exactIoU;

            // Generate diagnostic heatmap
            Texture2D heatmap = GenerateHeatmap(refNorm, drawnNorm, dilatedRef, normalizedResolution);

            return new ScoringResult
            {
                Score = finalScore,
                ScorerName = Name,
                Heatmap = heatmap,
                Details = details
            };
        }

        /// <summary>
        /// Hook for subclasses to post-process normalized binary masks before scoring (e.g. contour extraction).
        /// </summary>
        protected virtual bool[] PostProcessNormalizedMask(bool[] normalizedMask, int resolution)
        {
            return normalizedMask;
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

            bool isLightBackground = false;
            if (autoDetectBackground)
            {
                // Sample 4 corners and borders to detect if canvas is white/light
                float borderGraySum = 0f;
                float borderAlphaSum = 0f;
                int sampleCount = 0;

                int step = Mathf.Max(1, (w + h) / 64);
                for (int x = 0; x < w; x += step)
                {
                    Sample(pixels[x], ref borderGraySum, ref borderAlphaSum, ref sampleCount);
                    Sample(pixels[(h - 1) * w + x], ref borderGraySum, ref borderAlphaSum, ref sampleCount);
                }
                for (int y = 0; y < h; y += step)
                {
                    Sample(pixels[y * w], ref borderGraySum, ref borderAlphaSum, ref sampleCount);
                    Sample(pixels[y * w + (w - 1)], ref borderGraySum, ref borderAlphaSum, ref sampleCount);
                }

                float avgAlpha = borderAlphaSum / sampleCount;
                float avgGray = borderGraySum / sampleCount;
                isLightBackground = avgAlpha > 0.5f && avgGray > 0.5f;
            }
            else
            {
                isLightBackground = invertColors;
            }

            bool[] rawMask = new bool[total];
            for (int i = 0; i < total; i++)
            {
                Color32 c = pixels[i];
                float alpha = c.a / 255f;
                float gray = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

                bool isStroke;
                if (alpha < 0.1f)
                {
                    isStroke = false; // Transparent is always background
                }
                else if (isLightBackground)
                {
                    // Light background (white): dark pixels are strokes
                    isStroke = (1f - gray) >= binarizeThreshold;
                }
                else
                {
                    // Dark background (black/transparent): bright pixels are strokes
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

        private static void Sample(Color32 c, ref float graySum, ref float alphaSum, ref int count)
        {
            alphaSum += c.a / 255f;
            graySum += (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            count++;
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
        private static bool[] NormalizeShape(bool[] mask, int origW, int origH, RectInt box, int normRes, float padRatio)
        {
            bool[] output = new bool[normRes * normRes];

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
                        if (mask[srcRowOffset + srcX])
                        {
                            output[rowOffset + nx] = true;
                        }
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// Morphological dilation to compute tolerance zone around reference lines.
        /// </summary>
        private static bool[] Dilate(bool[] source, int width, int height, int radius)
        {
            if (radius <= 0) return (bool[])source.Clone();

            bool[] result = new bool[width * height];
            int rSq = radius * radius;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!source[y * width + x]) continue;

                    int minX = Mathf.Max(0, x - radius);
                    int maxX = Mathf.Min(width - 1, x + radius);
                    int minY = Mathf.Max(0, y - radius);
                    int maxY = Mathf.Min(height - 1, y + radius);

                    for (int ny = minY; ny <= maxY; ny++)
                    {
                        int dy = ny - y;
                        int dySq = dy * dy;
                        int row = ny * width;

                        for (int nx = minX; nx <= maxX; nx++)
                        {
                            int dx = nx - x;
                            if (dx * dx + dySq <= rSq)
                            {
                                result[row + nx] = true;
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static Texture2D GenerateHeatmap(bool[] refNorm, bool[] drawnNorm, bool[] dilatedRef, int res)
        {
            Texture2D heatmap = new Texture2D(res, res, TextureFormat.RGBA32, false);
            Color32[] mapPixels = new Color32[res * res];

            for (int i = 0; i < mapPixels.Length; i++)
            {
                bool r = refNorm[i];
                bool d = drawnNorm[i];
                bool dNear = dilatedRef[i];

                if (r && d)
                {
                    // Green: Exact stroke match
                    mapPixels[i] = new Color32(0, 255, 0, 255);
                }
                else if (d && dNear)
                {
                    // Yellow-Green: In proximity tolerance
                    mapPixels[i] = new Color32(180, 255, 0, 255);
                }
                else if (d && !dNear)
                {
                    // Red: Out of line / Scribble / Penalty
                    mapPixels[i] = new Color32(255, 30, 30, 255);
                }
                else if (r && !d)
                {
                    // Blue/Cyan: Missing reference line
                    mapPixels[i] = new Color32(60, 140, 255, 255);
                }
                else
                {
                    // Dark background
                    mapPixels[i] = new Color32(20, 20, 20, 180);
                }
            }

            heatmap.SetPixels32(mapPixels);
            heatmap.Apply();
            return heatmap;
        }
    }
}
