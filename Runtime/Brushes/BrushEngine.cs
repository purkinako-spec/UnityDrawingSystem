using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DrawingSystem.Brushes
{
    /// <summary>
    /// Core rendering engine that stamps brush textures onto a <see cref="RenderTexture"/>.
    /// Provides methods for stamping, stroke interpolation, and erasing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is <b>not thread-safe</b> and must only be used from the main (render) thread.
    /// It manipulates GPU resources (<see cref="Material"/>, <see cref="RenderTexture"/>,
    /// <see cref="Texture2D"/>) and uses immediate-mode GL rendering.
    /// </para>
    /// <para>
    /// Call <see cref="Dispose"/> when the engine is no longer needed to release GPU resources.
    /// </para>
    /// </remarks>
    public class BrushEngine : IDisposable
    {
        // ───────────────────────────────────────────────
        //  Constants
        // ───────────────────────────────────────────────

        private const string ShaderName = "Hidden/DrawingSystem/BrushStamp";
        private const int DefaultBrushResolution = 64;

        // Shader property IDs (cached for performance)
        private static readonly int PropBrushTex  = Shader.PropertyToID("_BrushTex");
        private static readonly int PropColor     = Shader.PropertyToID("_Color");
        private static readonly int PropBrushPos  = Shader.PropertyToID("_BrushPos");
        private static readonly int PropBrushSize = Shader.PropertyToID("_BrushSize");
        private static readonly int PropHardness  = Shader.PropertyToID("_Hardness");
        private static readonly int PropOpacity   = Shader.PropertyToID("_Opacity");
        private static readonly int PropIsEraser  = Shader.PropertyToID("_IsEraser");

        // ───────────────────────────────────────────────
        //  Fields
        // ───────────────────────────────────────────────

        private Material brushMaterial;
        private Texture2D defaultBrushTexture;
        private bool disposed;

        // ───────────────────────────────────────────────
        //  Constructor
        // ───────────────────────────────────────────────

        /// <summary>
        /// Creates a new <see cref="BrushEngine"/> instance.
        /// Locates the <c>Hidden/DrawingSystem/BrushStamp</c> shader and creates the internal
        /// brush material. Generates a default circular brush texture for use when a
        /// <see cref="BrushProfile"/> has no custom texture assigned.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the required shader cannot be found. Ensure the shader is included in
        /// the project (typically under <c>Runtime/Shaders/</c>).
        /// </exception>
        private static Shader FindBrushShader()
        {
            // 1. 通常の名前で検索
            Shader s = Shader.Find(ShaderName);
            if (s != null) return s;

            // 2. Hidden なしの代替名で検索
            s = Shader.Find("DrawingSystem/BrushStamp");
            if (s != null) return s;

            // 3. Resources からロード
            s = Resources.Load<Shader>("DrawingSystem/BrushStamp");
            if (s != null) return s;

            s = Resources.Load<Shader>("BrushStamp");
            if (s != null) return s;

            // 4. フォールバック
            Debug.LogWarning($"[BrushEngine] Shader '{ShaderName}' not found via standard paths. Attempting fallback shader.");
            return Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default") ?? Shader.Find("Unlit/Texture");
        }

        public BrushEngine()
        {
            Shader brushShader = FindBrushShader();
            if (brushShader == null)
            {
                Debug.LogError(
                    $"[BrushEngine] Shader '{ShaderName}' and fallbacks not found. " +
                    "Make sure it is included in the project and not stripped.");
                throw new InvalidOperationException(
                    $"Required shader '{ShaderName}' could not be found.");
            }

            brushMaterial = new Material(brushShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            defaultBrushTexture = GenerateDefaultBrushTexture(DefaultBrushResolution);
        }

        // ───────────────────────────────────────────────
        //  Public API
        // ───────────────────────────────────────────────

        /// <summary>
        /// Renders a single brush stamp at the specified UV position on the target texture.
        /// </summary>
        /// <param name="target">
        /// The <see cref="RenderTexture"/> to draw onto. Must not be <c>null</c>.
        /// </param>
        /// <param name="brush">
        /// The <see cref="BrushProfile"/> defining brush shape, hardness, and pressure curves.
        /// </param>
        /// <param name="uvPosition">
        /// The stamp center in UV coordinates (0–1 range, bottom-left origin).
        /// </param>
        /// <param name="color">
        /// The stroke color. Alpha is overridden by the pressure-derived opacity.
        /// </param>
        /// <param name="pressure">Pen pressure in the range 0–1.</param>
        /// <param name="size">
        /// Override size in pixels. Use <see cref="BrushProfile.GetSize"/> to compute
        /// a pressure-sensitive value before calling this method.
        /// </param>
        public void Stamp(RenderTexture target, BrushProfile brush, Vector2 uvPosition,
                          Color color, float pressure, float size)
        {
            ThrowIfDisposed();

            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (brush == null)
                throw new ArgumentNullException(nameof(brush));

            RenderStamp(target, brush, uvPosition, color, pressure, size, isEraser: false);
        }

        /// <summary>
        /// Renders an interpolated stroke between two UV positions, placing stamps at intervals
        /// determined by the brush's <see cref="BrushProfile.Spacing"/>.
        /// </summary>
        /// <param name="target">The <see cref="RenderTexture"/> to draw onto.</param>
        /// <param name="brush">The <see cref="BrushProfile"/> to use.</param>
        /// <param name="fromUV">Start position in UV coordinates.</param>
        /// <param name="toUV">End position in UV coordinates.</param>
        /// <param name="color">The stroke color.</param>
        /// <param name="fromPressure">Pen pressure at the start point (0–1).</param>
        /// <param name="toPressure">Pen pressure at the end point (0–1).</param>
        /// <param name="size">Base size in pixels for spacing calculations.</param>
        /// <returns>
        /// A list of UV positions where stamps were actually rendered. Useful for recording
        /// strokes for undo/redo or network replication.
        /// </returns>
        public List<Vector2> DrawStrokeBetween(RenderTexture target, BrushProfile brush,
                                               Vector2 fromUV, Vector2 toUV,
                                               Color color, float fromPressure, float toPressure,
                                               float size, bool isEraser = false)
        {
            ThrowIfDisposed();

            if (target == null)
                throw new ArgumentNullException(nameof(target));

            var stampPositions = new List<Vector2>();

            // 1px 最低サイズの保証
            float effectiveSize = Mathf.Max(1.0f, size);
            float brushSizeUV = effectiveSize / target.width;

            // 動的スタンプ間隔：
            // 大口径ブラシのときは間隔を適度に広げて負荷爆発を防ぐ（15%）
            // 小口径ブラシ（1px〜3px）のときは線が途切れないようピクセル密度に合わせて密にする
            float spacing;
            if (effectiveSize <= 3.0f)
            {
                spacing = 0.5f;
            }
            else if (effectiveSize >= 30.0f)
            {
                spacing = 0.15f;
            }
            else
            {
                spacing = brush != null ? Mathf.Clamp(brush.Spacing, 0.08f, 0.25f) : 0.10f;
            }

            float spacingUV = Mathf.Max(spacing * brushSizeUV, 0.5f / target.width);

            Vector2 delta = toUV - fromUV;
            float distance = delta.magnitude;

            if (distance < 0.0001f)
            {
                return stampPositions;
            }

            int numStamps = Mathf.Clamp(Mathf.CeilToInt(distance / spacingUV), 1, 128);

            // ブラシテクスチャの解決
            Texture2D brushTex = (brush != null && brush.BrushTexture != null)
                ? brush.BrushTexture
                : defaultBrushTexture;

            // マテリアル定数プロパティの設定（全スタンプ共通）
            brushMaterial.SetTexture(PropBrushTex, brushTex);
            brushMaterial.SetColor(PropColor, Color.white);
            brushMaterial.SetFloat(PropHardness, effectiveSize <= 2.0f ? 1.0f : (brush != null ? brush.Hardness : 1.0f));
            brushMaterial.SetFloat(PropOpacity, 1.0f);
            brushMaterial.SetFloat(PropIsEraser, isEraser ? 1f : 0f);

            // GPUレンダーターゲット設定（バッチ化: 全スタンプで1回だけ切り替え）
            RenderTexture previousRT = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadOrtho();

                brushMaterial.SetPass(isEraser ? 1 : 0);

                GL.Begin(GL.QUADS);

                for (int i = 1; i <= numStamps; i++)
                {
                    float t = (float)i / numStamps;
                    Vector2 pos = Vector2.Lerp(fromUV, toUV, t);
                    float pressure = Mathf.Lerp(fromPressure, toPressure, t);
                    float stampSize = brush != null ? brush.GetSize(pressure, effectiveSize) : effectiveSize;
                    stampSize = Mathf.Max(1.0f, stampSize);

                    float opacity = brush != null ? brush.GetOpacity(pressure) : 1.0f;
                    Color stampColor = new Color(color.r, color.g, color.b, opacity);

                    GL.Color(stampColor);

                    float halfU = (stampSize / target.width) * 0.5f;
                    float halfV = (stampSize / target.height) * 0.5f;

                    float left   = pos.x - halfU;
                    float right  = pos.x + halfU;
                    float bottom = pos.y - halfV;
                    float top    = pos.y + halfV;

                    GL.TexCoord2(0f, 0f);
                    GL.Vertex3(left, bottom, 0f);

                    GL.TexCoord2(1f, 0f);
                    GL.Vertex3(right, bottom, 0f);

                    GL.TexCoord2(1f, 1f);
                    GL.Vertex3(right, top, 0f);

                    GL.TexCoord2(0f, 1f);
                    GL.Vertex3(left, top, 0f);

                    stampPositions.Add(pos);
                }

                GL.End();
                GL.PopMatrix();
            }
            finally
            {
                RenderTexture.active = previousRT;
            }

            return stampPositions;
        }

        /// <summary>
        /// Erases content at the specified UV position by rendering with a destination-alpha
        /// clearing blend mode.
        /// </summary>
        /// <param name="target">The <see cref="RenderTexture"/> to erase from.</param>
        /// <param name="brush">
        /// The <see cref="BrushProfile"/> defining eraser shape and pressure curves.
        /// </param>
        /// <param name="uvPosition">
        /// The eraser center in UV coordinates (0–1 range, bottom-left origin).
        /// </param>
        /// <param name="pressure">Pen pressure in the range 0–1.</param>
        /// <param name="size">
        /// Override size in pixels. Use <see cref="BrushProfile.GetSize"/> to compute
        /// a pressure-sensitive value before calling this method.
        /// </param>
        public void Erase(RenderTexture target, BrushProfile brush, Vector2 uvPosition,
                          float pressure, float size)
        {
            ThrowIfDisposed();

            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (brush == null)
                throw new ArgumentNullException(nameof(brush));

            RenderStamp(target, brush, uvPosition, Color.white, pressure, size, isEraser: true);
        }

        /// <summary>
        /// Releases all GPU resources (material, cached textures) held by this engine.
        /// After disposal, any further method calls will throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            if (brushMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(brushMaterial);
                brushMaterial = null;
            }

            if (defaultBrushTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(defaultBrushTexture);
                defaultBrushTexture = null;
            }
        }

        // ───────────────────────────────────────────────
        //  Private Implementation
        // ───────────────────────────────────────────────

        /// <summary>
        /// Core stamp rendering routine shared by <see cref="Stamp"/> and <see cref="Erase"/>.
        /// Saves and restores <see cref="RenderTexture.active"/> to avoid corrupting other
        /// rendering state.
        /// </summary>
        private void RenderStamp(RenderTexture target, BrushProfile brush,
                                 Vector2 uvPosition, Color color, float pressure,
                                 float size, bool isEraser)
        {
            // Resolve brush texture
            Texture2D brushTex = (brush != null && brush.BrushTexture != null)
                ? brush.BrushTexture
                : defaultBrushTexture;

            // Calculate pressure-derived values
            float opacity = brush != null ? brush.GetOpacity(pressure) : 1.0f;

            float effectiveSize = Mathf.Max(1.0f, size);

            // Convert brush size from pixels to UV space
            float brushSizeU = effectiveSize / target.width;
            float brushSizeV = effectiveSize / target.height;

            // Set material properties
            brushMaterial.SetTexture(PropBrushTex, brushTex);
            brushMaterial.SetColor(PropColor, Color.white);
            brushMaterial.SetVector(PropBrushPos, new Vector4(uvPosition.x, uvPosition.y, 0f, 0f));
            brushMaterial.SetFloat(PropBrushSize, brushSizeU);
            brushMaterial.SetFloat(PropHardness, effectiveSize <= 2.0f ? 1.0f : (brush != null ? brush.Hardness : 1.0f));
            brushMaterial.SetFloat(PropOpacity, 1.0f);
            brushMaterial.SetFloat(PropIsEraser, isEraser ? 1f : 0f);

            // Save current render target
            RenderTexture previousRT = RenderTexture.active;

            try
            {
                RenderTexture.active = target;

                GL.PushMatrix();
                GL.LoadOrtho();

                // Activate the appropriate shader pass: 0 = Paint, 1 = Eraser
                brushMaterial.SetPass(isEraser ? 1 : 0);

                // Calculate quad corners in UV space
                float halfU = brushSizeU * 0.5f;
                float halfV = brushSizeV * 0.5f;

                float left   = uvPosition.x - halfU;
                float right  = uvPosition.x + halfU;
                float bottom = uvPosition.y - halfV;
                float top    = uvPosition.y + halfV;

                // Draw a textured quad at the brush position
                GL.Begin(GL.QUADS);
                GL.Color(new Color(color.r, color.g, color.b, opacity));

                GL.TexCoord2(0f, 0f);
                GL.Vertex3(left, bottom, 0f);

                GL.TexCoord2(1f, 0f);
                GL.Vertex3(right, bottom, 0f);

                GL.TexCoord2(1f, 1f);
                GL.Vertex3(right, top, 0f);

                GL.TexCoord2(0f, 1f);
                GL.Vertex3(left, top, 0f);

                GL.End();

                GL.PopMatrix();
            }
            finally
            {
                // Always restore the previous render target, even if rendering fails
                RenderTexture.active = previousRT;
            }
        }

        /// <summary>
        /// Generates a procedural circular brush texture with soft edges.
        /// The texture is a white circle with alpha falloff controlled by distance from center.
        /// </summary>
        /// <param name="resolution">
        /// The width and height of the generated texture in pixels. Defaults to 64.
        /// </param>
        /// <returns>
        /// A new <see cref="Texture2D"/> with <see cref="HideFlags.HideAndDontSave"/>.
        /// The caller is responsible for destroying it when no longer needed.
        /// </returns>
        private Texture2D GenerateDefaultBrushTexture(int resolution = 64)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float center = resolution * 0.5f;
            float maxRadius = center;
            var pixels = new Color[resolution * resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float normalizedDist = dist / maxRadius;

                    // Solid opaque core, smooth anti-aliased edge.
                    float alpha = 1f - Mathf.Clamp01((normalizedDist - 0.85f) / 0.15f);

                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return texture;
        }

        /// <summary>
        /// Throws an <see cref="ObjectDisposedException"/> if <see cref="Dispose"/> has been called.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(BrushEngine),
                    "Cannot use a disposed BrushEngine. Create a new instance.");
            }
        }
    }
}
