using System;
using UnityEngine;

namespace DrawingSystem.Layers
{
    /// <summary>
    /// レイヤーのブレンドモード
    /// </summary>
    public enum LayerBlendMode
    {
        Normal = 0,
        Multiply = 1,
        Screen = 2,
        Overlay = 3
    }

    /// <summary>
    /// お絵描きシステムの単一レイヤーを表すクラス
    /// </summary>
    [System.Serializable]
    public class DrawingLayer : IDisposable
    {
        [SerializeField] private string layerName;
        [SerializeField] private bool isVisible = true;
        [SerializeField, Range(0f, 1f)] private float opacity = 1f;
        [SerializeField] private LayerBlendMode blendMode = LayerBlendMode.Normal;

        private RenderTexture renderTexture;
        private bool isDisposed;

        public string LayerName
        {
            get => layerName;
            set => layerName = value;
        }

        public bool IsVisible
        {
            get => isVisible;
            set => isVisible = value;
        }

        public float Opacity
        {
            get => opacity;
            set => opacity = Mathf.Clamp01(value);
        }

        public LayerBlendMode BlendMode
        {
            get => blendMode;
            set => blendMode = value;
        }

        public RenderTexture RenderTexture => renderTexture;

        public DrawingLayer(string name, int width, int height)
        {
            layerName = name;
            isVisible = true;
            opacity = 1f;
            blendMode = LayerBlendMode.Normal;

            renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = $"Layer_{name}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            renderTexture.Create();
            Clear();
        }

        /// <summary>
        /// レイヤーを透明にクリアします
        /// </summary>
        public void Clear()
        {
            if (renderTexture == null || !renderTexture.IsCreated()) return;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = prevActive;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            if (renderTexture != null)
            {
                if (renderTexture.IsCreated())
                {
                    renderTexture.Release();
                }
                UnityEngine.Object.Destroy(renderTexture);
                renderTexture = null;
            }
        }
    }
}
