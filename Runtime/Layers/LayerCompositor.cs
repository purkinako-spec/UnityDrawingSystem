using System;
using UnityEngine;

namespace DrawingSystem.Layers
{
    /// <summary>
    /// 複数レイヤーをブレンドシェーダーを用いて1枚のRenderTextureに合成するクラス
    /// </summary>
    public class LayerCompositor : IDisposable
    {
        private Material compositeMaterial;
        private RenderTexture tempBuffer;
        private bool isDisposed;

        private static readonly int BaseTexId = Shader.PropertyToID("_BaseTex");
        private static readonly int BlendTexId = Shader.PropertyToID("_BlendTex");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int BlendModeId = Shader.PropertyToID("_BlendMode");

        public LayerCompositor()
        {
            Shader shader = Shader.Find("Hidden/DrawingSystem/LayerComposite");
            if (shader == null)
            {
                Debug.LogWarning("[DrawingSystem] LayerComposite shader not found. Falling back to default sprite or blit.");
            }
            else
            {
                compositeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
        }

        /// <summary>
        /// 全レイヤーを合成し、destination RenderTextureに書き込みます
        /// </summary>
        public void Composite(DrawingLayer[] layers, RenderTexture destination, Color backgroundColor)
        {
            if (destination == null || !destination.IsCreated()) return;

            int width = destination.width;
            int height = destination.height;

            EnsureTempBuffer(width, height);

            // 背景色で destination を初期化
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = destination;
            GL.Clear(true, true, backgroundColor);
            RenderTexture.active = prevActive;

            if (layers == null || layers.Length == 0) return;

            // 各可視レイヤーを下から順番に合成
            foreach (var layer in layers)
            {
                if (layer == null || !layer.IsVisible || layer.RenderTexture == null) continue;

                // destination を tempBuffer に退避
                Graphics.Blit(destination, tempBuffer);

                if (compositeMaterial != null)
                {
                    compositeMaterial.SetTexture(BaseTexId, tempBuffer);
                    compositeMaterial.SetTexture(BlendTexId, layer.RenderTexture);
                    compositeMaterial.SetFloat(OpacityId, layer.Opacity);
                    compositeMaterial.SetInt(BlendModeId, (int)layer.BlendMode);

                    Graphics.Blit(tempBuffer, destination, compositeMaterial);
                }
                else
                {
                    // フォールバック: 通常Blit
                    Graphics.Blit(layer.RenderTexture, destination);
                }
            }
        }

        private void EnsureTempBuffer(int width, int height)
        {
            if (tempBuffer != null && (tempBuffer.width != width || tempBuffer.height != height))
            {
                tempBuffer.Release();
                UnityEngine.Object.Destroy(tempBuffer);
                tempBuffer = null;
            }

            if (tempBuffer == null)
            {
                tempBuffer = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "LayerCompositor_TempBuffer",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                tempBuffer.Create();
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            if (compositeMaterial != null)
            {
                UnityEngine.Object.Destroy(compositeMaterial);
                compositeMaterial = null;
            }

            if (tempBuffer != null)
            {
                if (tempBuffer.IsCreated()) tempBuffer.Release();
                UnityEngine.Object.Destroy(tempBuffer);
                tempBuffer = null;
            }
        }
    }
}
