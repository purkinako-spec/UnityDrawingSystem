using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>
    /// Scores drawings by comparing individual pixel values between the reference and drawn images.
    /// Computes MSE-based similarity and a strict match ratio, with an optional heatmap output.
    /// </summary>
    [CreateAssetMenu(fileName = "PixelSimilarityScorer", menuName = "Drawing System/Scoring/Pixel Similarity Scorer")]
    public class PixelSimilarityScorer : ScriptableObject, IScoringEngine
    {
        [Tooltip("If true, compare RGB channels independently. If false, convert to grayscale first.")]
        [SerializeField] private bool compareColor = false;

        [Tooltip("Pixels with per-channel difference below this value (0–1) are considered matching.")]
        [SerializeField, Range(0f, 1f)] private float threshold = 0.1f;

        [Tooltip("Weight of the MSE similarity score in the final combined score.")]
        [SerializeField, Range(0f, 1f)] private float mseSimilarityWeight = 0.5f;

        [Tooltip("Weight of the strict match ratio in the final combined score.")]
        [SerializeField, Range(0f, 1f)] private float matchRatioWeight = 0.5f;

        /// <summary>Whether to compare full RGB or grayscale.</summary>
        public bool CompareColor
        {
            get => compareColor;
            set => compareColor = value;
        }

        /// <summary>Difference threshold below which a pixel pair is considered matching.</summary>
        public float Threshold
        {
            get => threshold;
            set => threshold = Mathf.Clamp01(value);
        }

        /// <inheritdoc />
        public string Name => "Pixel Similarity";

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
            double sumSquaredError = 0.0;
            int matchingPixels = 0;

            // Heatmap data
            Color32[] heatmapPixels = new Color32[totalPixels];

            float invByte = 1f / 255f;
            float thresholdSq = threshold * threshold;

            for (int i = 0; i < totalPixels; i++)
            {
                Color32 rp = refPixels[i];
                Color32 dp = drawnPixels[i];

                float diff;
                if (compareColor)
                {
                    float dr = (rp.r - dp.r) * invByte;
                    float dg = (rp.g - dp.g) * invByte;
                    float db = (rp.b - dp.b) * invByte;
                    diff = (dr * dr + dg * dg + db * db) / 3f;
                }
                else
                {
                    // Grayscale using standard luminance weights
                    float grayRef = (0.299f * rp.r + 0.587f * rp.g + 0.114f * rp.b) * invByte;
                    float grayDrawn = (0.299f * dp.r + 0.587f * dp.g + 0.114f * dp.b) * invByte;
                    float d = grayRef - grayDrawn;
                    diff = d * d;
                }

                sumSquaredError += diff;

                if (diff < thresholdSq)
                {
                    matchingPixels++;
                }

                // Heatmap: green = similar, red = different
                float diffSqrt = Mathf.Sqrt(diff);
                byte green = (byte)(Mathf.Clamp01(1f - diffSqrt) * 255);
                byte red = (byte)(Mathf.Clamp01(diffSqrt) * 255);
                heatmapPixels[i] = new Color32(red, green, 0, 255);
            }

            float mse = (float)(sumSquaredError / totalPixels);
            float mseSimilarity = 1f - Mathf.Sqrt(mse);
            float matchRatio = (float)matchingPixels / totalPixels;

            // Normalize weights
            float totalWeight = mseSimilarityWeight + matchRatioWeight;
            float normMseWeight = totalWeight > 0f ? mseSimilarityWeight / totalWeight : 0.5f;
            float normMatchWeight = totalWeight > 0f ? matchRatioWeight / totalWeight : 0.5f;

            float finalScore = Mathf.Clamp01(mseSimilarity * normMseWeight + matchRatio * normMatchWeight);

            // Build heatmap texture
            Texture2D heatmap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            heatmap.SetPixels32(heatmapPixels);
            heatmap.Apply();

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
                    { "MSE_Similarity", mseSimilarity },
                    { "MatchRatio", matchRatio }
                }
            };
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
        /// Safely destroys a texture in both editor and runtime contexts.
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
