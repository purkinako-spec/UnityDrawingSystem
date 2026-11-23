using System.Collections.Generic;
using UnityEngine;

namespace DrawingSystem.Scoring
{
    /// <summary>Result of a scoring evaluation.</summary>
    [System.Serializable]
    public struct ScoringResult
    {
        /// <summary>Overall score from 0 (no match) to 1 (perfect match).</summary>
        public float Score;

        /// <summary>Name of the scorer that produced this result.</summary>
        public string ScorerName;

        /// <summary>Optional heatmap visualization (red=different, green=similar). Can be null.</summary>
        public Texture2D Heatmap;

        /// <summary>Optional detailed sub-scores.</summary>
        public Dictionary<string, float> Details;
    }

    /// <summary>Interface for scoring engines that compare a reference image with a drawn image.</summary>
    public interface IScoringEngine
    {
        /// <summary>Display name of this scoring engine.</summary>
        string Name { get; }

        /// <summary>
        /// Evaluates the similarity between a reference image and a drawn image.
        /// </summary>
        /// <param name="reference">The target reference image.</param>
        /// <param name="drawn">The user-drawn image to compare against the reference.</param>
        /// <returns>A <see cref="ScoringResult"/> containing the score, optional heatmap, and details.</returns>
        ScoringResult Evaluate(Texture2D reference, Texture2D drawn);
    }
}
