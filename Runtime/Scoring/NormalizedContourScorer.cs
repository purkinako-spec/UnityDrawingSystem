using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>
    /// Position- and scale-invariant scorer that compares only the extracted contours (outlines)
    /// of the reference and drawn shapes, ignoring fill interiors and colors.
    /// </summary>
    [CreateAssetMenu(fileName = "NormalizedContourScorer", menuName = "Drawing System/Scoring/Normalized Contour Scorer (Outline Only)")]
    public class NormalizedContourScorer : NormalizedShapeScorer
    {
        /// <inheritdoc />
        public override string Name => "Normalized Contour (Outline Only)";

        /// <summary>
        /// Extracts 1-pixel boundary contours from the normalized binary mask using 8-neighbor erosion.
        /// </summary>
        protected override bool[] PostProcessNormalizedMask(bool[] normalizedMask, int resolution)
        {
            return ExtractContour(normalizedMask, resolution);
        }

        private static bool[] ExtractContour(bool[] mask, int resolution)
        {
            var contour = new bool[mask.Length];

            for (int y = 0; y < resolution; y++)
            {
                int rowOffset = y * resolution;
                bool isBorderY = y == 0 || y == resolution - 1;

                for (int x = 0; x < resolution; x++)
                {
                    int idx = rowOffset + x;
                    if (!mask[idx]) continue;

                    if (isBorderY || x == 0 || x == resolution - 1)
                    {
                        contour[idx] = true;
                        continue;
                    }

                    // Keep pixel as contour if any 8-connected neighbor is background
                    bool hasBackgroundNeighbor =
                        !mask[idx - 1] ||
                        !mask[idx + 1] ||
                        !mask[idx - resolution] ||
                        !mask[idx + resolution] ||
                        !mask[idx - resolution - 1] ||
                        !mask[idx - resolution + 1] ||
                        !mask[idx + resolution - 1] ||
                        !mask[idx + resolution + 1];

                    if (hasBackgroundNeighbor)
                    {
                        contour[idx] = true;
                    }
                }
            }

            return contour;
        }
    }
}
