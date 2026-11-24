using UnityEngine;

namespace DrawingSystem.Brushes
{
    /// <summary>
    /// Defines the type of brush behavior.
    /// </summary>
    public enum BrushType
    {
        /// <summary>Standard drawing pen.</summary>
        Pen,
        /// <summary>Eraser that removes drawn content.</summary>
        Eraser,
        /// <summary>Custom brush with user-defined behavior.</summary>
        Custom
    }

    /// <summary>
    /// A <see cref="ScriptableObject"/> that defines all configurable properties for a brush,
    /// including size, opacity, pressure curves, and tilt sensitivity.
    /// </summary>
    /// <remarks>
    /// Create new brush profiles via the Unity menu: Assets → Create → Drawing System → Brush Profile.
    /// All public methods must be called from the main thread only.
    /// </remarks>
    [CreateAssetMenu(fileName = "NewBrush", menuName = "Drawing System/Brush Profile")]
    public class BrushProfile : ScriptableObject
    {
        [Header("基本設定")]

        /// <summary>
        /// Unique identifier for this brush. Auto-generated from the asset name if left empty.
        /// </summary>
        [Tooltip("Unique brush identifier. Auto-generated from asset name if empty.")]
        public string BrushId;

        /// <summary>
        /// The behavioral type of this brush (Pen, Eraser, or Custom).
        /// </summary>
        [Tooltip("The behavioral type of this brush.")]
        public BrushType Type = BrushType.Pen;

        /// <summary>
        /// Optional texture defining the brush tip shape. When <c>null</c>, a default circular tip is used.
        /// </summary>
        [Tooltip("Brush tip shape texture. Leave null for a default circle.")]
        public Texture2D BrushTexture;

        [Header("サイズ")]

        /// <summary>
        /// The base size of the brush in pixels at full pressure.
        /// </summary>
        [Range(1f, 200f)]
        [Tooltip("Base brush size in pixels at full pressure.")]
        public float BaseSize = 10f;

        /// <summary>
        /// The minimum size expressed as a ratio of <see cref="BaseSize"/>.
        /// A value of 0.1 means the brush can shrink to 10% of its base size at zero pressure.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Minimum size as a ratio of BaseSize (0 = can shrink to zero).")]
        public float MinSizeRatio = 0.1f;

        /// <summary>
        /// Animation curve mapping pen pressure (0–1) to a size interpolation factor (0–1).
        /// The evaluated value is used to lerp between <c>BaseSize * MinSizeRatio</c> and <c>BaseSize</c>.
        /// </summary>
        [Tooltip("Curve mapping pressure to size factor.")]
        public AnimationCurve PressureSizeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("不透明度")]

        /// <summary>
        /// The base opacity of the brush at full pressure. Range: 0 (transparent) to 1 (opaque).
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Base opacity at full pressure.")]
        public float BaseOpacity = 1f;

        /// <summary>
        /// Animation curve mapping pen pressure (0–1) to an opacity multiplier (0–1).
        /// The evaluated value is multiplied by <see cref="BaseOpacity"/>.
        /// </summary>
        [Tooltip("Curve mapping pressure to opacity multiplier.")]
        public AnimationCurve PressureOpacityCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        [Header("描画品質")]

        /// <summary>
        /// Controls the edge hardness of the brush. 1.0 produces a crisp edge; 0.0 produces a fully soft edge.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Edge hardness: 1 = crisp, 0 = fully soft.")]
        public float Hardness = 1.0f;

        /// <summary>
        /// Spacing between consecutive stamps, expressed as a fraction of the current brush size.
        /// Lower values produce smoother strokes at the cost of performance.
        /// </summary>
        [Range(0.01f, 1f)]
        [Tooltip("Stamp spacing relative to brush size. Lower = smoother.")]
        public float Spacing = 0.05f;

        [Header("傾き設定")]

        /// <summary>
        /// Whether this brush responds to pen tilt input.
        /// </summary>
        [Tooltip("Enable pen tilt influence on the brush.")]
        public bool UseTilt = false;

        /// <summary>
        /// How strongly pen tilt affects the brush shape. Only used when <see cref="UseTilt"/> is <c>true</c>.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Strength of tilt influence on the brush.")]
        public float TiltSensitivity = 0.5f;

        /// <summary>
        /// Calculates the actual brush size for the given pen pressure.
        /// </summary>
        /// <param name="pressure">
        /// Pen pressure in the range 0–1. Values outside this range are clamped by the animation curve.
        /// </param>
        /// <returns>
        /// The interpolated brush size between <c>BaseSize * MinSizeRatio</c> and <c>BaseSize</c>.
        /// </returns>
        public float GetSize(float pressure, float overrideBaseSize = -1f)
        {
            float baseSize = overrideBaseSize > 0f ? overrideBaseSize : BaseSize;
            if (baseSize <= 1.0f) return 1.0f; // 1px ブラシ時は筆圧による縮小で消えないよう固定
            float curveValue = PressureSizeCurve != null ? PressureSizeCurve.Evaluate(Mathf.Clamp01(pressure)) : pressure;
            return Mathf.Max(1.0f, Mathf.Lerp(baseSize * MinSizeRatio, baseSize, curveValue));
        }

        /// <summary>
        /// Calculates the actual brush opacity for the given pen pressure.
        /// </summary>
        /// <param name="pressure">
        /// Pen pressure in the range 0–1. Values outside this range are clamped by the animation curve.
        /// </param>
        /// <returns>
        /// The final opacity value (<see cref="BaseOpacity"/> multiplied by the pressure curve evaluation).
        /// </returns>
        public float GetOpacity(float pressure)
        {
            float curveVal = PressureOpacityCurve != null ? PressureOpacityCurve.Evaluate(Mathf.Clamp01(pressure)) : 1.0f;
            // 描き初めや低筆圧時に線が薄くなりすぎるのを防ぎ、しっかりとした濃度を保つ
            return BaseOpacity * Mathf.Clamp01(Mathf.Max(0.6f, curveVal));
        }

        /// <summary>
        /// Called by Unity when the asset is loaded or a value is changed in the Inspector.
        /// Auto-generates <see cref="BrushId"/> from the asset name if it is empty.
        /// </summary>
        private void OnValidate()
        {
            if (string.IsNullOrEmpty(BrushId))
            {
                BrushId = name;
            }
        }
    }
}
