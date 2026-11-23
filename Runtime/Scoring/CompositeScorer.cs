using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>
    /// Combines multiple <see cref="IScoringEngine"/> implementations with configurable weights
    /// to produce a single composite score.
    /// </summary>
    [CreateAssetMenu(fileName = "CompositeScorer", menuName = "Drawing System/Scoring/Composite Scorer")]
    public class CompositeScorer : ScriptableObject, IScoringEngine
    {
        /// <summary>
        /// An entry pairing a scorer asset with a weight and enabled flag.
        /// </summary>
        [Serializable]
        public class ScorerEntry
        {
            /// <summary>ScriptableObject that implements <see cref="IScoringEngine"/>.</summary>
            [Tooltip("ScriptableObject that implements IScoringEngine.")]
            public ScriptableObject Scorer;

            /// <summary>Relative weight of this scorer in the composite result.</summary>
            [Tooltip("Relative weight of this scorer in the composite result.")]
            [Range(0f, 1f)] public float Weight = 1f;

            /// <summary>Whether this scorer is included in evaluation.</summary>
            [Tooltip("Whether this scorer is included in evaluation.")]
            public bool Enabled = true;
        }

        [Tooltip("List of scorers to combine with their respective weights.")]
        [SerializeField] private List<ScorerEntry> scorers = new List<ScorerEntry>();

        /// <summary>The list of scorer entries in this composite.</summary>
        public List<ScorerEntry> Scorers
        {
            get => scorers;
            set => scorers = value ?? new List<ScorerEntry>();
        }

        /// <inheritdoc />
        public string Name => "Composite";

        /// <inheritdoc />
        public ScoringResult Evaluate(Texture2D reference, Texture2D drawn)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            if (drawn == null)
                throw new ArgumentNullException(nameof(drawn));

            var details = new Dictionary<string, float>();
            var heatmaps = new List<Texture2D>();
            var weightedScores = new List<(float score, float weight)>();

            if (scorers == null || scorers.Count == 0)
            {
                Debug.LogWarning($"[{Name}] No scorers configured. Returning zero score.");
                return new ScoringResult
                {
                    Score = 0f,
                    ScorerName = Name,
                    Heatmap = null,
                    Details = details
                };
            }

            // Collect enabled scorers and compute total weight for normalization
            float totalWeight = 0f;
            foreach (ScorerEntry entry in scorers)
            {
                if (entry == null || !entry.Enabled || entry.Scorer == null)
                    continue;

                if (!(entry.Scorer is IScoringEngine))
                {
                    Debug.LogWarning($"[{Name}] Scorer '{entry.Scorer.name}' does not implement IScoringEngine. Skipping.");
                    continue;
                }

                totalWeight += entry.Weight;
            }

            if (totalWeight <= 0f)
            {
                Debug.LogWarning($"[{Name}] No enabled scorers with positive weight. Returning zero score.");
                return new ScoringResult
                {
                    Score = 0f,
                    ScorerName = Name,
                    Heatmap = null,
                    Details = details
                };
            }

            // Evaluate each enabled scorer
            foreach (ScorerEntry entry in scorers)
            {
                if (entry == null || !entry.Enabled || entry.Scorer == null)
                    continue;

                if (!(entry.Scorer is IScoringEngine engine))
                    continue;

                try
                {
                    ScoringResult subResult = engine.Evaluate(reference, drawn);

                    float normalizedWeight = entry.Weight / totalWeight;
                    weightedScores.Add((subResult.Score, normalizedWeight));

                    // Merge details with scorer name prefix
                    string prefix = subResult.ScorerName ?? engine.Name ?? entry.Scorer.name;
                    details[$"{prefix}/Score"] = subResult.Score;

                    if (subResult.Details != null)
                    {
                        foreach (var kvp in subResult.Details)
                        {
                            details[$"{prefix}/{kvp.Key}"] = kvp.Value;
                        }
                    }

                    // Collect heatmaps for averaging
                    if (subResult.Heatmap != null)
                    {
                        heatmaps.Add(subResult.Heatmap);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[{Name}] Scorer '{engine.Name}' threw an exception and will be skipped: {ex}");

                    // Recompute total weight excluding the failed scorer
                    totalWeight -= entry.Weight;
                    if (totalWeight <= 0f) totalWeight = 1f; // Prevent division by zero
                }
            }

            // Compute final weighted score (re-normalize in case some scorers were skipped)
            float finalScore = 0f;
            float actualTotalWeight = 0f;
            foreach (var (score, weight) in weightedScores)
            {
                actualTotalWeight += weight;
            }

            if (actualTotalWeight > 0f)
            {
                foreach (var (score, weight) in weightedScores)
                {
                    finalScore += score * (weight / actualTotalWeight);
                }
            }

            // Combine heatmaps by averaging pixel values
            Texture2D combinedHeatmap = CombineHeatmaps(heatmaps, reference.width, reference.height);

            return new ScoringResult
            {
                Score = Mathf.Clamp01(finalScore),
                ScorerName = Name,
                Heatmap = combinedHeatmap,
                Details = details
            };
        }

        /// <summary>
        /// Averages multiple heatmap textures into a single combined heatmap.
        /// All heatmaps are resampled to the target dimensions if needed.
        /// </summary>
        /// <param name="heatmaps">List of heatmap textures to combine.</param>
        /// <param name="targetWidth">Target width for the combined heatmap.</param>
        /// <param name="targetHeight">Target height for the combined heatmap.</param>
        /// <returns>Combined heatmap texture, or null if no heatmaps are provided.</returns>
        private static Texture2D CombineHeatmaps(List<Texture2D> heatmaps, int targetWidth, int targetHeight)
        {
            if (heatmaps == null || heatmaps.Count == 0)
                return null;

            if (heatmaps.Count == 1)
                return heatmaps[0];

            int totalPixels = targetWidth * targetHeight;
            int[] sumR = new int[totalPixels];
            int[] sumG = new int[totalPixels];
            int[] sumB = new int[totalPixels];
            int count = 0;

            List<Texture2D> toCleanup = new List<Texture2D>();

            foreach (Texture2D heatmap in heatmaps)
            {
                if (heatmap == null) continue;

                Texture2D source = heatmap;
                if (heatmap.width != targetWidth || heatmap.height != targetHeight)
                {
                    source = ResizeTexture(heatmap, targetWidth, targetHeight);
                    toCleanup.Add(source);
                }

                Color32[] pixels = source.GetPixels32();
                for (int i = 0; i < totalPixels && i < pixels.Length; i++)
                {
                    sumR[i] += pixels[i].r;
                    sumG[i] += pixels[i].g;
                    sumB[i] += pixels[i].b;
                }
                count++;
            }

            if (count == 0) return null;

            Color32[] combined = new Color32[totalPixels];
            for (int i = 0; i < totalPixels; i++)
            {
                combined[i] = new Color32(
                    (byte)(sumR[i] / count),
                    (byte)(sumG[i] / count),
                    (byte)(sumB[i] / count),
                    255
                );
            }

            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.SetPixels32(combined);
            result.Apply();

            // Cleanup resized temp textures
            foreach (Texture2D tex in toCleanup)
            {
                DestroyTexture(tex);
            }

            return result;
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
        private static void DestroyTexture(UnityEngine.Object obj)
        {
#if UNITY_EDITOR
            DestroyImmediate(obj);
#else
            Destroy(obj);
#endif
        }
    }
}
