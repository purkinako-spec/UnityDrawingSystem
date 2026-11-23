using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>
    /// Scores drawings using a simplified Structural Similarity Index (SSIM).
    /// Supports both CPU and GPU computation paths.
    /// </summary>
    [CreateAssetMenu(fileName = "StructuralScorer", menuName = "Drawing System/Scoring/Structural Scorer")]
    public class StructuralScorer : ScriptableObject, IScoringEngine
    {
        [Tooltip("Size of the local comparison window in pixels.")]
        [SerializeField, Min(2)] private int windowSize = 8;

        [Tooltip("Use compute shader for GPU-accelerated SSIM calculation when available.")]
        [SerializeField] private bool useGPU = true;

        [Tooltip("Compute shader for GPU-based structural scoring. Assign manually or load via Resources.")]
        [SerializeField] private ComputeShader scoringComputeShader;

        /// <summary>Size of the local comparison window in pixels.</summary>
        public int WindowSize
        {
            get => windowSize;
            set => windowSize = Mathf.Max(2, value);
        }

        /// <summary>Whether to attempt GPU-based computation.</summary>
        public bool UseGPU
        {
            get => useGPU;
            set => useGPU = value;
        }

        /// <inheritdoc />
        public string Name => "Structural (SSIM)";

        // SSIM constants (default values from the original paper)
        private const float K1 = 0.01f;
        private const float K2 = 0.03f;
        private const float L = 1.0f; // Dynamic range for normalized [0,1] values
        private static readonly float C1 = (K1 * L) * (K1 * L); // 0.0001
        private static readonly float C2 = (K2 * L) * (K2 * L); // 0.0009

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

            ScoringResult result;

            if (useGPU && SystemInfo.supportsComputeShaders && TryGetComputeShader(out ComputeShader shader))
            {
                result = EvaluateGPU(reference, drawnResized, width, height, shader);
            }
            else
            {
                result = EvaluateCPU(reference, drawnResized, width, height);
            }

            if (needsCleanup)
            {
                DestroyTexture(drawnResized);
            }

            return result;
        }

        /// <summary>
        /// CPU path: Divides the image into blocks and computes per-block SSIM.
        /// </summary>
        private ScoringResult EvaluateCPU(Texture2D reference, Texture2D drawn, int width, int height)
        {
            Color32[] refPixels = reference.GetPixels32();
            Color32[] drawnPixels = drawn.GetPixels32();

            float[] refGray = ConvertToGrayscale(refPixels);
            float[] drawnGray = ConvertToGrayscale(drawnPixels);

            int blocksX = Mathf.Max(1, width / windowSize);
            int blocksY = Mathf.Max(1, height / windowSize);
            int totalBlocks = blocksX * blocksY;

            float[] blockSSIM = new float[totalBlocks];
            float sumSSIM = 0f;

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    int startX = bx * windowSize;
                    int startY = by * windowSize;
                    int endX = Mathf.Min(startX + windowSize, width);
                    int endY = Mathf.Min(startY + windowSize, height);
                    int blockPixelCount = (endX - startX) * (endY - startY);

                    if (blockPixelCount == 0)
                    {
                        blockSSIM[by * blocksX + bx] = 1f;
                        sumSSIM += 1f;
                        continue;
                    }

                    // Compute local means
                    double meanRef = 0.0;
                    double meanDrawn = 0.0;
                    for (int y = startY; y < endY; y++)
                    {
                        int rowOffset = y * width;
                        for (int x = startX; x < endX; x++)
                        {
                            int idx = rowOffset + x;
                            meanRef += refGray[idx];
                            meanDrawn += drawnGray[idx];
                        }
                    }
                    meanRef /= blockPixelCount;
                    meanDrawn /= blockPixelCount;

                    // Compute variances and covariance
                    double varRef = 0.0;
                    double varDrawn = 0.0;
                    double covariance = 0.0;
                    for (int y = startY; y < endY; y++)
                    {
                        int rowOffset = y * width;
                        for (int x = startX; x < endX; x++)
                        {
                            int idx = rowOffset + x;
                            double dRef = refGray[idx] - meanRef;
                            double dDrawn = drawnGray[idx] - meanDrawn;
                            varRef += dRef * dRef;
                            varDrawn += dDrawn * dDrawn;
                            covariance += dRef * dDrawn;
                        }
                    }
                    varRef /= blockPixelCount;
                    varDrawn /= blockPixelCount;
                    covariance /= blockPixelCount;

                    // SSIM formula
                    double numerator = (2.0 * meanRef * meanDrawn + C1) * (2.0 * covariance + C2);
                    double denominator = (meanRef * meanRef + meanDrawn * meanDrawn + C1)
                                       * (varRef + varDrawn + C2);

                    float ssim = denominator > 0.0 ? (float)(numerator / denominator) : 1f;
                    ssim = Mathf.Clamp01(ssim);

                    blockSSIM[by * blocksX + bx] = ssim;
                    sumSSIM += ssim;
                }
            }

            float averageSSIM = totalBlocks > 0 ? sumSSIM / totalBlocks : 0f;

            // Generate heatmap from per-block SSIM values
            Texture2D heatmap = GenerateBlockHeatmap(blockSSIM, blocksX, blocksY, width, height);

            return new ScoringResult
            {
                Score = Mathf.Clamp01(averageSSIM),
                ScorerName = Name,
                Heatmap = heatmap,
                Details = new Dictionary<string, float>
                {
                    { "SSIM", averageSSIM },
                    { "BlocksEvaluated", totalBlocks }
                }
            };
        }

        /// <summary>
        /// GPU path: Dispatches the CSStructural kernel on the compute shader and reads back results.
        /// </summary>
        private ScoringResult EvaluateGPU(Texture2D reference, Texture2D drawn, int width, int height, ComputeShader shader)
        {
            int blocksX = Mathf.Max(1, width / windowSize);
            int blocksY = Mathf.Max(1, height / windowSize);
            int totalBlocks = blocksX * blocksY;

            int kernelIndex = shader.FindKernel("CSStructural");

            // Set textures
            shader.SetTexture(kernelIndex, "_ReferenceTex", reference);
            shader.SetTexture(kernelIndex, "_DrawnTex", drawn);
            shader.SetInt("_Width", width);
            shader.SetInt("_Height", height);
            shader.SetInt("_WindowSize", windowSize);
            shader.SetInt("_BlocksX", blocksX);
            shader.SetInt("_BlocksY", blocksY);
            shader.SetFloat("_C1", C1);
            shader.SetFloat("_C2", C2);

            // Output buffer for per-block SSIM values
            ComputeBuffer resultBuffer = new ComputeBuffer(totalBlocks, sizeof(float));
            shader.SetBuffer(kernelIndex, "_ResultBuffer", resultBuffer);

            // Dispatch one thread group per block (or use a reasonable group size)
            int groupsX = Mathf.CeilToInt(blocksX / 8f);
            int groupsY = Mathf.CeilToInt(blocksY / 8f);
            shader.Dispatch(kernelIndex, Mathf.Max(1, groupsX), Mathf.Max(1, groupsY), 1);

            // Read back results
            float[] blockSSIM = new float[totalBlocks];
            resultBuffer.GetData(blockSSIM);
            resultBuffer.Release();

            // Compute average SSIM
            float sumSSIM = 0f;
            for (int i = 0; i < totalBlocks; i++)
            {
                blockSSIM[i] = Mathf.Clamp01(blockSSIM[i]);
                sumSSIM += blockSSIM[i];
            }
            float averageSSIM = totalBlocks > 0 ? sumSSIM / totalBlocks : 0f;

            // Generate heatmap from per-block SSIM values
            Texture2D heatmap = GenerateBlockHeatmap(blockSSIM, blocksX, blocksY, width, height);

            return new ScoringResult
            {
                Score = Mathf.Clamp01(averageSSIM),
                ScorerName = Name,
                Heatmap = heatmap,
                Details = new Dictionary<string, float>
                {
                    { "SSIM", averageSSIM },
                    { "BlocksEvaluated", totalBlocks },
                    { "ComputedOnGPU", 1f }
                }
            };
        }

        /// <summary>
        /// Attempts to find or load the compute shader for GPU scoring.
        /// </summary>
        private bool TryGetComputeShader(out ComputeShader shader)
        {
            if (scoringComputeShader != null)
            {
                shader = scoringComputeShader;
                return true;
            }

            // Attempt to load from Resources
            shader = Resources.Load<ComputeShader>("Shaders/ScoringDiff");
            if (shader != null)
            {
                scoringComputeShader = shader;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Converts Color32 pixel array to normalized grayscale float array.
        /// </summary>
        private static float[] ConvertToGrayscale(Color32[] pixels)
        {
            float[] gray = new float[pixels.Length];
            const float inv = 1f / 255f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                gray[i] = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) * inv;
            }
            return gray;
        }

        /// <summary>
        /// Generates a heatmap texture from per-block SSIM values.
        /// Green indicates high similarity; red indicates low similarity.
        /// </summary>
        private Texture2D GenerateBlockHeatmap(float[] blockSSIM, int blocksX, int blocksY, int width, int height)
        {
            Texture2D heatmap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32[] heatmapPixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                int by = Mathf.Min(y / windowSize, blocksY - 1);
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int bx = Mathf.Min(x / windowSize, blocksX - 1);
                    float ssim = blockSSIM[by * blocksX + bx];

                    byte green = (byte)(Mathf.Clamp01(ssim) * 255);
                    byte red = (byte)(Mathf.Clamp01(1f - ssim) * 255);
                    heatmapPixels[rowOffset + x] = new Color32(red, green, 0, 255);
                }
            }

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
